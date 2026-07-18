using System;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.WinUI.Graphics3DGL;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// Tier B (GPU) render-surface adapter: rasterises each engine frame on the GPU through an
/// off-screen OpenGL/GLES context from the CodeBrix.Platform Graphics3DGL add-in, reads the
/// result back to CPU pixels in a single copy, and presents it through the same
/// <see cref="GameSurfaceCanvas"/> path the Tier A (CPU) adapter uses — so letterboxing,
/// resize behaviour, and <see cref="GameSurfaceCanvas.SetRenderResolution"/> are identical
/// across tiers. Opt in with <see cref="GameSurfaceCanvas.UseGpuRendering"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine's background loop never touches GL-thread-rendered surfaces (see
/// <see cref="BackbufferBase.IsGlThreadRendered"/>), so this adapter is the frame driver: it
/// listens to <see cref="Engine.AfterFrameRender"/> — which the engine raises at the
/// <see cref="Configuration.EngineConfiguration.TargetFPS"/> cadence — and runs one GL frame
/// on the UI thread per notification (coalesced, latest-wins). All OpenGL and
/// <see cref="GRContext"/> work stays on the UI thread; <see cref="OffscreenGLContext.MakeCurrent"/>
/// saves and restores the head's own context around each frame.
/// </para>
/// <para>
/// When the running head cannot provide an OpenGL context (no driver, no GL support), the
/// adapter logs one warning and keeps rendering on the <see cref="GpuBackbuffer"/>'s built-in
/// CPU fallback surface, so a Tier B game degrades to CPU rendering instead of a black screen.
/// </para>
/// <para>
/// <see cref="GpuBackbuffer.VSync"/> has no effect on this adapter: the off-screen context has
/// no swap chain, and presentation is composited by the head. Frame pacing comes from the
/// engine's TargetFPS throttle. <see cref="GpuBackbuffer.MsaaSampleCount"/> is honoured — a
/// change re-initialises the GPU surface on the next frame.
/// </para>
/// </remarks>
public sealed class CodeBrixPlatformGpuRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
{
    private readonly GameSurfaceCanvas _canvas;
    private readonly bool _fixedResolution;

    private RenderSurfaceHostBase? _host;
    private OffscreenGLContext? _context;
    private GRContext? _grContext;
    private bool _gpuInitAttempted;
    private bool _gpuAvailable;

    // Size and MSAA the GpuBackbuffer surface was last initialized with.
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _surfaceMsaa;
    private bool _surfaceInitialized;

    // Double-buffered CPU readback targets: the GL frame reads back into one bitmap while the
    // canvas may still be painting the wrapper image over the other. All access is UI-thread.
    private SKBitmap? _readbackA;
    private SKBitmap? _readbackB;
    private bool _writeToA = true;

    // Guards the latest presented wrapper image against the engine-thread pause capture.
    private readonly object _presentGate = new();
    private SKImage? _currentImage;

    private int _tickScheduled;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformGpuRenderSurfaceAdapter"/> class.
    /// </summary>
    /// <param name="canvas">The <see cref="GameSurfaceCanvas"/> this adapter presents to.</param>
    /// <param name="fixedWidth">
    /// A fixed render width in pixels, or 0 (the default) to track the canvas width and follow window resizes.
    /// </param>
    /// <param name="fixedHeight">
    /// A fixed render height in pixels, or 0 (the default) to track the canvas height and follow window resizes.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> is null.</exception>
    public CodeBrixPlatformGpuRenderSurfaceAdapter(GameSurfaceCanvas canvas, int fixedWidth = 0, int fixedHeight = 0)
        : base(
            fixedWidth > 0 ? fixedWidth : Math.Max(1, (int)canvas.ActualWidth),
            fixedHeight > 0 ? fixedHeight : Math.Max(1, (int)canvas.ActualHeight))
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _fixedResolution = fixedWidth > 0 && fixedHeight > 0;

        // Only follow the control's size when the render resolution is not pinned; a pinned
        // resolution is letterboxed to fit by the canvas each paint.
        if (!_fixedResolution)
            _canvas.SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// Whether the off-screen OpenGL context and its <see cref="GRContext"/> were created
    /// successfully. <see langword="null"/> until the first frame attempts initialization;
    /// <see langword="false"/> means the adapter is running on the CPU fallback surface.
    /// </summary>
    public bool? IsGpuInitialized => _gpuInitAttempted ? _gpuAvailable : null;

    /// <summary>
    /// Attaches the render-surface host whose <see cref="GpuBackbuffer"/> this adapter drives,
    /// and starts listening for the engine's frame notifications. Called by
    /// <see cref="GameSurfaceCanvas"/> when it creates the host/adapter pair.
    /// </summary>
    /// <param name="host">The host that owns the GPU backbuffer this adapter renders.</param>
    internal void AttachHost(RenderSurfaceHostBase host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Engine.Instance.AfterFrameRender += OnAfterFrameRender;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed)
            return;

        var w = (int)_canvas.ActualWidth;
        var h = (int)_canvas.ActualHeight;
        if (w > 0 && h > 0)
            SetDestinationSize(w, h);
    }

    // Engine thread, once per rendered frame (TargetFPS cadence). Coalesce to a single queued
    // GL tick so a slow UI thread never accumulates a backlog of frames.
    private void OnAfterFrameRender()
    {
        if (_disposed || Interlocked.CompareExchange(ref _tickScheduled, 1, 0) != 0)
            return;

        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || !dispatcherQueue.TryEnqueue(GlTick))
            Interlocked.Exchange(ref _tickScheduled, 0);
    }

    private void GlTick()
    {
        Interlocked.Exchange(ref _tickScheduled, 0);
        RunGlFrame(renderWhilePaused: false);
    }

    // UI thread. Renders one engine frame into the GPU backbuffer and presents the readback.
    private void RunGlFrame(bool renderWhilePaused)
    {
        if (_disposed || _host is null || _host.Backbuffer is not GpuBackbuffer gpuBackbuffer)
            return;

        try
        {
            if (EnsureGpu())
            {
                using (_context!.MakeCurrent())
                {
                    EnsureBackbufferSurface(gpuBackbuffer);

                    using var gpuImage = _host.GlRenderAndSnapshot(renderWhilePaused);
                    if (gpuImage is null)
                        return;

                    // One GPU→CPU copy per frame; needs the context current.
                    ReadbackAndPresent(gpuImage);
                }
            }
            else
            {
                if (!_gpuInitAttempted)
                    return; // canvas not loaded yet — try again on the next frame notification

                // GL unavailable on this head: the GpuBackbuffer is still rendering on its CPU
                // fallback surface, so drive the same frame path without a context.
                using var image = _host.GlRenderAndSnapshot(renderWhilePaused);
                if (image is null)
                    return;

                ReadbackAndPresent(image);
            }

            gpuBackbuffer.RecordFrame();
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "Tier B GPU frame failed.");
        }
    }

    // UI thread. Creates the off-screen GL context and GRContext once the canvas is loaded.
    private bool EnsureGpu()
    {
        if (_gpuInitAttempted)
            return _gpuAvailable;

        // TryCreate needs a live XamlRoot; before the canvas is loaded, skip without latching
        // the attempt so the next frame retries.
        if (!_canvas.IsLoaded || _canvas.XamlRoot is null)
            return false;

        _gpuInitAttempted = true;

        try
        {
            if (!OffscreenGLContext.TryCreate(_canvas.XamlRoot, out _context))
            {
                Engine.Logger.LogWarning(
                    "Tier B GPU rendering is unavailable on this head (no off-screen OpenGL context); " +
                    "falling back to CPU rendering of the GPU backbuffer.");
                return false;
            }

            // OffscreenGLContext.CreateGrContext() makes this off-screen context current itself,
            // owns the desktop-GL-vs-GLES flavor branch, and throws InvalidOperationException if
            // neither GL flavor can build a GRContext for it (caught below → CPU fallback). Its
            // managed GetProcAddress "assembled interface" path is safe again as of
            // CodeBrix.Platform 1.0.199.897, which filters the egl* names that X11's
            // glXGetProcAddress otherwise resolved to non-null garbage stubs (the SIGSEGV that
            // previously forced the GRGlInterface.Create() native-path workaround here).
            _grContext = _context.CreateGrContext();

            _gpuAvailable = true;
        }
        catch (Exception ex)
        {
            Engine.Logger.LogWarning(ex,
                "Tier B GPU initialization failed; falling back to CPU rendering of the GPU backbuffer.");
            _context?.Dispose();
            _context = null;
            _grContext = null;
            _gpuAvailable = false;
        }

        return _gpuAvailable;
    }

    // UI thread, context current. (Re)creates the GPU surface when the destination size or the
    // MSAA sample count changed since the last initialization.
    private void EnsureBackbufferSurface(GpuBackbuffer gpuBackbuffer)
    {
        var msaa = gpuBackbuffer.MsaaSampleCount;
        if (_surfaceInitialized && _surfaceWidth == Width && _surfaceHeight == Height && _surfaceMsaa == msaa)
            return;

        gpuBackbuffer.Initialize(_grContext!, Width, Height);
        _surfaceWidth = Width;
        _surfaceHeight = Height;
        _surfaceMsaa = msaa;
        _surfaceInitialized = true;
    }

    // UI thread (context current when the image is GPU-backed). Reads the frame back into the
    // write-side bitmap, wraps it zero-copy, and presents it.
    private void ReadbackAndPresent(SKImage image)
    {
        var w = image.Width;
        var h = image.Height;
        if (w <= 0 || h <= 0)
            return;

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var target = _writeToA ? _readbackA : _readbackB;

        if (target is null || target.Width != w || target.Height != h)
        {
            target?.Dispose();
            target = new SKBitmap(info);
            if (_writeToA)
                _readbackA = target;
            else
                _readbackB = target;
        }

        if (!image.ReadPixels(info, target.GetPixels(), target.RowBytes, 0, 0))
        {
            Engine.Logger.LogWarning("Tier B GPU frame readback failed.");
            return;
        }

        _writeToA = !_writeToA;

        var wrapper = SKImage.FromPixels(info, target.GetPixels(), target.RowBytes);
        Present(wrapper, new SKRectI(0, 0, w, h), SKRect.Create(0, 0, Width, Height));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called by this adapter's own GL frame on the UI thread; the engine loop never presents
    /// GL-thread-rendered surfaces itself.
    /// </remarks>
    public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
    {
        if (_disposed)
        {
            bufferImage.Dispose();
            return;
        }

        lock (_presentGate)
        {
            var old = _currentImage;
            _currentImage = bufferImage;
            if (old is not null && !ReferenceEquals(old, bufferImage))
                old.Dispose();
        }

        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
            _canvas.SetImage(bufferImage, bufferRect);
        else
            dispatcherQueue.TryEnqueue(() => _canvas.SetImage(bufferImage, bufferRect));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns an independent CPU copy of the newest frame this adapter presented, made under
    /// the same gate the present path uses — safe to call from the engine thread during the
    /// pause transition. The caller owns (and must dispose) the returned image.
    /// </remarks>
    public override SKImage? CaptureLatestPresentedFrame()
    {
        lock (_presentGate)
        {
            if (_disposed || _currentImage is null)
                return null;

            var info = new SKImageInfo(_currentImage.Width, _currentImage.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var copy = new SKBitmap(info);
            if (!_currentImage.ReadPixels(info, copy.GetPixels(), copy.RowBytes, 0, 0))
            {
                copy.Dispose();
                return null;
            }

            copy.SetImmutable();
            return SKImage.FromBitmap(copy);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs one GL frame with the engine's pause guard bypassed, so scene changes made by
    /// <see cref="Engine.Paused"/> handlers (a pause overlay, for example) reach the screen —
    /// the Tier B equivalent of the final frame the engine renders for CPU surfaces after the
    /// pause transition. Posted to the UI thread by the engine.
    /// </remarks>
    public override void PresentPausedFrame(RenderSurfaceHostBase host)
    {
        if (_disposed || !ReferenceEquals(host, _host))
            return;

        RunGlFrame(renderWhilePaused: true);
    }

    /// <summary>
    /// Releases the adapter's resources: unhooks the engine and canvas events and disposes the
    /// readback buffers, the presented image, the <see cref="GRContext"/>, and the off-screen
    /// GL context (with the context current, as GL teardown requires).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Engine.Instance.AfterFrameRender -= OnAfterFrameRender;
        _canvas.SizeChanged -= OnSizeChanged;

        lock (_presentGate)
        {
            _currentImage?.Dispose();
            _currentImage = null;
        }

        if (_context is not null && _grContext is not null)
        {
            using (_context.MakeCurrent())
            {
                _grContext.Dispose();
            }
        }

        _grContext = null;
        _context?.Dispose();
        _context = null;

        _readbackA?.Dispose();
        _readbackA = null;
        _readbackB?.Dispose();
        _readbackB = null;
    }
}
