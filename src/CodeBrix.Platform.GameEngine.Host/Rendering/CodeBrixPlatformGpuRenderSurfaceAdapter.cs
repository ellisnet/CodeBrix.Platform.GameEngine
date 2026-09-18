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
/// GPU (GpuRendering) render-surface adapter: rasterises each engine frame on the GPU through a
/// <b>backend-neutral</b> off-screen Skia GPU context from the CodeBrix.Platform Graphics3DGL add-in
/// (<see cref="SkiaGpuContext"/> — OpenGL/GLES on the Windows, X11, Wayland and Frame Buffer heads;
/// Metal on macOS), reads the result back to CPU pixels in a single copy, and presents it through
/// the same <see cref="GameSurfaceCanvas"/> path the CpuRendering (CPU) adapter uses — so
/// letterboxing, resize behaviour, and <see cref="GameSurfaceCanvas.SetRenderResolution"/> are
/// identical across tiers. Opt in with <see cref="GameSurfaceCanvas.UseGpuRendering"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine's background loop never touches GL-thread-rendered surfaces (see
/// <see cref="BackbufferBase.IsGlThreadRendered"/>), so this adapter is the frame driver: it
/// listens to <see cref="Engine.AfterFrameRender"/> — which the engine raises at the
/// <see cref="Configuration.EngineConfiguration.TargetFPS"/> cadence — and runs one GPU frame
/// on the UI thread per notification (coalesced, latest-wins). All GPU and
/// <see cref="GRContext"/> work stays on the UI thread; <see cref="SkiaGpuContext.BeginFrame"/>
/// saves and restores the head's own context around each frame (a no-op on Metal, which has no
/// thread-current context).
/// </para>
/// <para>
/// When the running head cannot provide a GPU context (no driver, no GPU support, or macOS in
/// software-rendering mode), the adapter logs one warning and keeps rendering on the
/// <see cref="GpuBackbuffer"/>'s built-in CPU fallback surface, so a GpuRendering game degrades to
/// CPU rendering instead of a black screen.
/// </para>
/// <para>
/// <see cref="GpuBackbuffer.VSync"/> has no effect on this adapter: the off-screen context has
/// no swap chain, and presentation is composited by the head. Frame pacing comes from the
/// engine's TargetFPS throttle. <see cref="GpuBackbuffer.MsaaSampleCount"/> is honoured the next
/// time the GPU render target is allocated — that is, on an explicit render-resolution change,
/// not on a window resize, which changes presentation only.
/// </para>
/// </remarks>
public sealed class CodeBrixPlatformGpuRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
{
    private readonly GameSurfaceCanvas _canvas;

    private RenderSurfaceHostBase? _host;
    private SkiaGpuContext? _context;
    private GRContext? _grContext; // cached from _context.GrContext; owned/disposed by _context
    private bool _gpuInitAttempted;
    private bool _gpuAvailable;

    // Double-buffered CPU readback targets: the GPU frame reads back into one bitmap while the
    // canvas may still be painting the wrapper image over the other. All access is UI-thread.
    private SKBitmap? _readbackA;
    private SKBitmap? _readbackB;
    private bool _writeToA = true;

    // Guards the latest presented wrapper image against the engine-thread pause capture.
    private readonly object _presentGate = new();
    private SKImage? _currentImage;

    private int _tickScheduled;
    private bool _disposed;

    // True while the canvas is off the visual tree (window closing or page navigated away).
    // Set on the UI thread; read from the UI thread (RunGpuFrame) and the engine thread, so
    // volatile. While detached the adapter drives no frames and holds no GPU context.
    private volatile bool _detached;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformGpuRenderSurfaceAdapter"/> class.
    /// </summary>
    /// <remarks>
    /// The adapter always tracks the canvas size: that size is the presentation destination, not the
    /// render resolution. The render resolution is the host's, from
    /// <see cref="CodeBrix.Platform.GameEngine.Configuration.EngineConfiguration.RenderScale"/> or
    /// <see cref="GameSurfaceCanvas.SetRenderResolution"/>, and only a change to it reallocates the
    /// GPU render target.
    /// </remarks>
    /// <param name="canvas">The <see cref="GameSurfaceCanvas"/> this adapter presents to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> is null.</exception>
    public CodeBrixPlatformGpuRenderSurfaceAdapter(GameSurfaceCanvas canvas)
        : base(
            Math.Max(1, (int)canvas.ActualWidth),
            Math.Max(1, (int)canvas.ActualHeight),
            HasLayoutSize(canvas))
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

        // A resize changes only where and how large the frame is presented, so the adapter follows
        // the control whatever the render resolution is.
        _canvas.SizeChanged += OnSizeChanged;

        // Stop driving frames (and tear the GPU context down while its window is still alive)
        // whenever the canvas leaves the visual tree, and resume when it returns; see
        // OnCanvasUnloaded for why this cannot wait until Dispose.
        _canvas.Unloaded += OnCanvasUnloaded;
        _canvas.Loaded += OnCanvasLoaded;
    }

    // A canvas created but not laid out yet reports a zero size: the adapter then starts on a
    // placeholder, and the first real layout establishes the logical render resolution once.
    private static bool HasLayoutSize(GameSurfaceCanvas canvas)
        => canvas is not null && canvas.ActualWidth >= 1 && canvas.ActualHeight >= 1;

    /// <summary>
    /// Whether the off-screen GPU context and its <see cref="GRContext"/> were created
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

        // Frame notifications only flow while the canvas is in the visual tree; if the host is
        // (unusually) created while the canvas is unloaded, OnCanvasLoaded arms them later.
        if (!_detached)
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

    // UI thread. The canvas left the visual tree: the window is closing or the page navigated
    // away. Two things must happen NOW rather than at Dispose time:
    //  * Frame driving must stop. The engine may well keep cycling (stopping it is the
    //    application's call, not this adapter's), and every AfterFrameRender would keep posting
    //    a GPU tick to the dispatcher — after the last window closes, that continuous posting
    //    can keep a head's message loop from ever draining its queue and exiting (observed as a
    //    zombie process on the Win32-Skia head).
    //  * The GPU surface and context must be torn down while the window they were created
    //    against is still alive — on WGL the off-screen context is built on the window's own
    //    device context, so once the window is destroyed every MakeCurrent fails ("The handle
    //    is invalid") and the GPU resources can no longer be released at all. Unloaded fires
    //    before the native window is destroyed, so this is the last reliable moment.
    // The canvas coming back (OnCanvasLoaded) re-arms frame driving, and EnsureGpu lazily
    // rebuilds the context on the next frame notification.
    private void OnCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || _detached)
            return;

        _detached = true; // RunGpuFrame's guard: an already-queued GpuTick becomes a no-op
        Engine.Instance.AfterFrameRender -= OnAfterFrameRender;
        ReleaseGpu();
    }

    // UI thread. The canvas re-entered the visual tree after a detach: resume frame driving.
    // The GPU context is deliberately NOT rebuilt here — EnsureGpu re-creates it on the next
    // frame notification, once the canvas has a live XamlRoot again.
    private void OnCanvasLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_detached)
            return;

        _detached = false;
        if (_host is not null)
            Engine.Instance.AfterFrameRender += OnAfterFrameRender;
    }

    // Engine thread, once per rendered frame (TargetFPS cadence). Coalesce to a single queued
    // GPU tick so a slow UI thread never accumulates a backlog of frames.
    private void OnAfterFrameRender()
    {
        if (_disposed || Interlocked.CompareExchange(ref _tickScheduled, 1, 0) != 0)
            return;

        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || !dispatcherQueue.TryEnqueue(GpuTick))
            Interlocked.Exchange(ref _tickScheduled, 0);
    }

    private void GpuTick()
    {
        Interlocked.Exchange(ref _tickScheduled, 0);
        RunGpuFrame(renderWhilePaused: false);
    }

    // UI thread. Renders one engine frame into the GPU backbuffer and presents the readback.
    // The _detached guard covers every frame driver — a GpuTick that was already queued when
    // the canvas unloaded, and the engine-posted PresentPausedFrame — so no GPU work (and no
    // context re-creation) can happen against a window that is going away.
    private void RunGpuFrame(bool renderWhilePaused)
    {
        if (_disposed || _detached || _host is null || _host.Backbuffer is not GpuBackbuffer gpuBackbuffer)
            return;

        try
        {
            if (EnsureGpu())
            {
                using (_context!.BeginFrame())
                {
                    // Allocates the GPU render target on the first frame, and afterwards only when
                    // the context changed or an explicit render-resolution request is waiting; a
                    // window resize never reaches this.
                    gpuBackbuffer.EnsureInitialized(_grContext!);

                    using var gpuImage = _host.GlRenderAndSnapshot(renderWhilePaused);
                    if (gpuImage is null)
                        return;

                    // One GPU→CPU copy per frame; needs the context current (a no-op on Metal).
                    ReadbackAndPresent(gpuImage);
                }
            }
            else
            {
                if (!_gpuInitAttempted)
                    return; // canvas not loaded yet — try again on the next frame notification

                // GPU context unavailable on this head: the GpuBackbuffer is still rendering on its
                // CPU fallback surface, so drive the same frame path without a context.
                using var image = _host.GlRenderAndSnapshot(renderWhilePaused);
                if (image is null)
                    return;

                ReadbackAndPresent(image);
            }

            gpuBackbuffer.RecordFrame();
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "GPU rendering frame failed.");
        }
    }

    // UI thread. Creates the backend-neutral off-screen GPU context and its GRContext once the
    // canvas is loaded (OpenGL/GLES on the Windows/Linux heads, Metal on macOS).
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
            // SkiaGpuContext resolves the head's GPU backend behind one API: on macOS the head's
            // Skia-on-Metal provider (a separate GRContext on its own command queue, on the
            // compositor's device); on every other head an off-screen OpenGL/GLES context. It
            // returns false when no GPU context is available (for example macOS in software mode),
            // in which case we fall back to CPU rendering of the GPU backbuffer.
            if (!SkiaGpuContext.TryCreate(_canvas.XamlRoot, out _context))
            {
                var warning =
                    "GPU rendering is unavailable on this head (no off-screen GPU context); " +
                    "falling back to CPU rendering of the GPU backbuffer.";

                // On Windows the usual cause is a missing OpenGL driver; Microsoft's free "OpenCL and
                // OpenGL Compatibility Pack" can supply one, so hint at it (Windows only).
                if (OperatingSystem.IsWindows())
                {
                    warning +=
                        " On Windows, installing the free Microsoft \"OpenCL and OpenGL Compatibility " +
                        "Pack\" (https://apps.microsoft.com/detail/9NQPSL29BFFF) might enable GPU rendering.";
                }

                Engine.Logger.LogWarning(warning);
                return false;
            }

            // The facade owns the GRContext lifetime (it disposes it inside a frame scope); this is
            // just a cached reference for EnsureBackbufferSurface.
            _grContext = _context.GrContext;
            _gpuAvailable = true;

            // Record the chosen backend once, so which API is actually in use (OpenGL vs Metal) is
            // obvious from the log alone.
            Engine.Logger.LogInformation("GPU rendering initialized (backend: {Backend}).", _context.Backend);
        }
        catch (Exception ex)
        {
            Engine.Logger.LogWarning(ex,
                "GPU rendering initialization failed; falling back to CPU rendering of the GPU backbuffer.");
            _context?.Dispose();
            _context = null;
            _grContext = null;
            _gpuAvailable = false;
        }

        return _gpuAvailable;
    }

    // UI thread (context current when the image is GPU-backed). Reads the frame back into the
    // write-side bitmap, wraps it zero-copy, and presents it. The image is the LOGICAL backbuffer
    // snapshot, so the readback bitmaps are the logical size too and a window resize neither
    // reallocates them nor changes how many pixels are copied.
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
            Engine.Logger.LogWarning("GPU rendering frame readback failed.");
            return;
        }

        _writeToA = !_writeToA;

        var wrapper = SKImage.FromPixels(info, target.GetPixels(), target.RowBytes);
        Present(wrapper, new SKRectI(0, 0, w, h), Presentation.DestinationRect);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called by this adapter's own GL frame on the UI thread; the engine loop never presents
    /// GL-thread-rendered surfaces itself. The canvas repaints its whole surface on every paint, so
    /// it is handed the complete frame and draws it through the shared presentation transform
    /// rather than blitting <paramref name="bufferRect"/> into <paramref name="destRect"/>.
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
            _canvas.SetImage(bufferImage);
        else
            dispatcherQueue.TryEnqueue(() => _canvas.SetImage(bufferImage));
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
    /// Runs one GPU frame with the engine's pause guard bypassed, so scene changes made by
    /// <see cref="Engine.Paused"/> handlers (a pause overlay, for example) reach the screen —
    /// the GPU-rendering equivalent of the final frame the engine renders for CPU surfaces after the
    /// pause transition. Posted to the UI thread by the engine.
    /// </remarks>
    public override void PresentPausedFrame(RenderSurfaceHostBase host)
    {
        if (_disposed || !ReferenceEquals(host, _host))
            return;

        RunGpuFrame(renderWhilePaused: true);
    }

    // UI thread. Tears the GPU state down in the order GL requires: first the backbuffer's
    // GPU surface, inside a frame scope (disposing it needs its GRContext current), then the
    // context itself (SkiaGpuContext.Dispose runs its own frame scope; both scopes are no-ops
    // on Metal). Resets the init latch so EnsureGpu can rebuild everything on a later frame.
    private void ReleaseGpu()
    {
        if (_gpuAvailable && _context is not null)
        {
            try
            {
                using (_context.BeginFrame())
                {
                    (_host?.Backbuffer as GpuBackbuffer)?.ReleaseGpuSurface();
                }
            }
            catch (Exception ex)
            {
                Engine.Logger.LogWarning(ex, "GPU backbuffer surface teardown failed.");
            }
        }

        // The SkiaGpuContext owns the GRContext and disposes it inside a frame scope; we only hold a
        // cached reference to it, so just drop that and dispose the context.
        _grContext = null;
        _context?.Dispose();
        _context = null;

        _gpuInitAttempted = false;
        _gpuAvailable = false;
    }

    /// <summary>
    /// Releases the adapter's resources: unhooks the engine and canvas events and disposes the
    /// readback buffers, the presented image, and the GPU surface and context (each inside a
    /// frame scope, as GL teardown requires; no-op scopes on Metal).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Engine.Instance.AfterFrameRender -= OnAfterFrameRender;
        _canvas.SizeChanged -= OnSizeChanged;
        _canvas.Unloaded -= OnCanvasUnloaded;
        _canvas.Loaded -= OnCanvasLoaded;

        lock (_presentGate)
        {
            _currentImage?.Dispose();
            _currentImage = null;
        }

        ReleaseGpu();

        _readbackA?.Dispose();
        _readbackA = null;
        _readbackB?.Dispose();
        _readbackB = null;
    }
}
