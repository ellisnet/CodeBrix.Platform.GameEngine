using System;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// A CodeBrix.Platform SkiaSharp canvas control that displays the game engine's rendered frames.
/// It owns the CpuRendering (CPU) <see cref="RenderSurfaceHost{TBackbuffer}"/> that the engine renders into
/// (exposed via <see cref="Host"/>); the paired <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/>
/// feeds it the latest backbuffer image, which this control blits onto its Skia surface each paint.
/// </summary>
/// <remarks>
/// Place this control in a XAML page, create a <see cref="CodeBrix.Platform.GameEngine.Scenes.Scene"/>,
/// call <c>Host.Bind(scene)</c>, then start the engine — or derive a game host from
/// <see cref="Hosting.CodeBrixGameHost"/>, which performs that wiring for you.
/// </remarks>
public class GameSurfaceCanvas : SKXamlCanvas
{
    private readonly object _gate = new();
    private SKImage? _currentImage;
    private SKRectI _currentBufferRect;

    private RenderSurfaceAdapterBase? _adapter;
    private RenderSurfaceHost<BackbufferBase>? _host;
    private GameSurfaceCanvasPixelFramePresenter? _presenter;
    private bool _ensureFocusApplied;
    private bool _useGpuRendering;
    private int _renderWidth;
    private int _renderHeight;

    // Quiet period after the last SizeChanged before live presenting resumes.
    private const double ResizeSettleMilliseconds = 500;
    private readonly DispatcherTimer _resizeSettleTimer;
    private bool _isResizing;
    private bool _firstStarted;

    /// <summary>
    /// Occurs once, the first time this canvas reaches a non-zero layout size — the point at which the
    /// surface is laid out and ready. Consumers hook this to run their game-startup logic (for example,
    /// pinning the render resolution and starting the engine).
    /// </summary>
    public event FirstStartedEventHandler? FirstStarted;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSurfaceCanvas"/> class.
    /// </summary>
    public GameSurfaceCanvas()
    {
        // A transparent background makes the whole surface hit-testable for pointer input,
        // even before the first frame has been painted.
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
        PaintSurface += OnPaintSurface;

        _resizeSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ResizeSettleMilliseconds) };
        _resizeSettleTimer.Tick += OnResizeSettled;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Raise FirstStarted once, when the surface first has a real (non-zero) size.
        if (!_firstStarted && ActualWidth > 0 && ActualHeight > 0)
        {
            _firstStarted = true;
            FirstStarted?.Invoke(this, new FirstStartedEventArgs(e.NewSize));
        }

        // A resize drag raises SizeChanged continuously. While it does, suppress live engine
        // presents (see SetImage) so a backlog of full-size blits doesn't pile up on the UI
        // thread and cause the "resize, go chunky, then catch up" behaviour; instead just re-blit
        // the last frame (letterboxed) at the new size. Live presenting resumes once the size
        // settles (no SizeChanged for ResizeSettleMilliseconds).
        _isResizing = true;
        _resizeSettleTimer.Stop();
        _resizeSettleTimer.Start();
        Invalidate();
    }

    private void OnResizeSettled(object? sender, object e)
    {
        _resizeSettleTimer.Stop();
        _isResizing = false;
        Invalidate();
    }

    /// <summary>
    /// Gets the engine render-surface host bound to this canvas. Create a
    /// <see cref="CodeBrix.Platform.GameEngine.Scenes.Scene"/> and call <c>Host.Bind(scene)</c>,
    /// then start the engine, to render into this control. The host's backbuffer is a
    /// <see cref="BitmapBackbuffer"/> (CpuRendering, the default) or a <see cref="GpuBackbuffer"/>
    /// when <see cref="UseGpuRendering"/> was set first.
    /// </summary>
    public RenderSurfaceHost<BackbufferBase> Host
    {
        get
        {
            EnsureHost();
            return _host!;
        }
    }

    /// <summary>
    /// Gets the render-surface adapter that feeds engine frames to this canvas: a
    /// <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/> (CpuRendering CPU, the default) or a
    /// <see cref="CodeBrixPlatformGpuRenderSurfaceAdapter"/> (GpuRendering-OpenGL GPU) when
    /// <see cref="UseGpuRendering"/> was set first.
    /// </summary>
    public RenderSurfaceAdapterBase RenderSurfaceAdapter
    {
        get
        {
            EnsureHost();
            return _adapter!;
        }
    }

    /// <summary>
    /// Opts this canvas into GpuRendering-OpenGL (GPU) rendering: the engine's scene is rasterised by the GPU
    /// through an off-screen OpenGL/GLES context and read back for presentation, instead of being
    /// rendered on the CPU (CpuRendering, the default). Set this before the first access to
    /// <see cref="Host"/> — like <see cref="SetRenderResolution"/>, it configures how the
    /// host/adapter pair is created. Letterboxing, resize behaviour, and input mapping are
    /// identical across tiers. On a head without OpenGL support the adapter logs a warning and
    /// falls back to CPU rendering automatically.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when changed after the scene pipeline has been created (the tier cannot change once
    /// the engine is rendering).
    /// </exception>
    public bool UseGpuRendering
    {
        get => _useGpuRendering;
        set
        {
            if (value == _useGpuRendering)
                return;

            if (_host is not null)
                throw new InvalidOperationException(
                    $"{nameof(UseGpuRendering)} must be set before the first access to {nameof(Host)}; " +
                    "the render tier cannot change once the scene pipeline exists.");

            _useGpuRendering = value;
        }
    }

    /// <summary>
    /// Pins the engine render resolution to a fixed size, independent of this control's on-screen size.
    /// Each frame is then scaled to fit the control while preserving the render aspect ratio, centered,
    /// with black letterbox/pillarbox bars where the control's aspect ratio differs.
    /// </summary>
    /// <remarks>
    /// Call this before the first access to <see cref="Host"/> (that is, before the engine starts).
    /// Pass a non-positive width or height (the default) to instead track this control's size, so the
    /// render resolution follows the window.
    /// </remarks>
    /// <param name="width">The fixed render width in pixels, or 0 to track the control width.</param>
    /// <param name="height">The fixed render height in pixels, or 0 to track the control height.</param>
    public void SetRenderResolution(int width, int height)
    {
        _renderWidth = width;
        _renderHeight = height;
    }

    private void EnsureHost()
    {
        if (_host is not null)
            return;

        // Presenter mode and the scene pipeline are mutually exclusive per canvas — two
        // paths must never fight over one surface.
        if (_presenter is not null)
            throw new InvalidOperationException(
                $"This {nameof(GameSurfaceCanvas)} is in presenter mode ({nameof(UsePixelFramePresenter)}); its scene-pipeline {nameof(Host)} is unavailable.");

        if (_useGpuRendering)
        {
            var gpuAdapter = new CodeBrixPlatformGpuRenderSurfaceAdapter(this, _renderWidth, _renderHeight);
            _adapter = gpuAdapter;
            _host = new RenderSurfaceHost<BackbufferBase>(gpuAdapter, (w, h) => new GpuBackbuffer(w, h));
            gpuAdapter.AttachHost(_host);
        }
        else
        {
            _adapter = new CodeBrixPlatformBitmapRenderSurfaceAdapter(this, _renderWidth, _renderHeight);
            _host = new RenderSurfaceHost<BackbufferBase>(_adapter, (w, h) => new BitmapBackbuffer(w, h));
        }
    }

    /// <summary>
    /// Switches this canvas to presenter mode for software-rendered (framebuffer-style)
    /// games and returns its <see cref="PixelFramePresenter"/>: configure it, then hand it
    /// whole CPU-rendered frames once per tic from the game's own loop. This does NOT create
    /// the engine's scene/backbuffer pipeline — a framebuffer game never pays for it.
    /// </summary>
    /// <remarks>
    /// Presenter mode and the scene pipeline are mutually exclusive per canvas: after this
    /// call, touching <see cref="Host"/> or <see cref="RenderSurfaceAdapter"/> throws — and
    /// vice versa. Calling this again returns the same presenter.
    /// </remarks>
    public PixelFramePresenter UsePixelFramePresenter()
    {
        if (_presenter is { } existing)
            return existing;

        if (_host is not null)
            throw new InvalidOperationException(
                $"This {nameof(GameSurfaceCanvas)} already runs the scene pipeline ({nameof(Host)}); presenter mode is unavailable.");

        _presenter = new GameSurfaceCanvasPixelFramePresenter(this);
        return _presenter;
    }

    /// <summary>
    /// Makes this canvas reliably keyboard-focusable for game input: focusable as a tab
    /// stop, focused as soon as it is loaded, and re-focused whenever it is pointer-pressed —
    /// the same recipe the keyboard input adapter applies. Games that read the keyboard
    /// through their own path can call this once instead of rewriting the recipe. Idempotent.
    /// </summary>
    public void EnsureFocus()
    {
        if (_ensureFocusApplied)
            return;
        _ensureFocusApplied = true;

        IsTabStop = true;
        if (IsLoaded)
            Focus(FocusState.Programmatic);
        Loaded += (_, _) => Focus(FocusState.Programmatic);
        PointerPressed += (_, _) => Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Hides or restores the pointer cursor while it is over this canvas, using the WinUI
    /// disposed-cursor hide convention on <see cref="UIElement.ProtectedCursor"/> (which only
    /// a subclass can reach — that is why this helper exists). Heads without cursor-hide
    /// support simply keep showing the cursor.
    /// </summary>
    /// <param name="hidden">True to hide the cursor over this canvas; false to restore the default.</param>
    public void SetPointerCursorHidden(bool hidden)
    {
        if (hidden)
        {
            // WinUI convention: a disposed InputCursor assigned to ProtectedCursor means "hide".
            var cursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            cursor.Dispose();
            ProtectedCursor = cursor;
        }
        else
        {
            ProtectedCursor = null;
        }
    }

    /// <summary>
    /// Converts a point in this canvas's element coordinates (as pointer events report them)
    /// to logical frame-buffer coordinates of the presenter's current configuration —
    /// pointer aiming across the letterbox mapping. Returns null when not in presenter mode
    /// or before the first painted frame; the result may lie outside the frame when the
    /// point is over the letterbox bars.
    /// </summary>
    /// <param name="canvasPoint">The point in this element's coordinate space.</param>
    public global::Windows.Foundation.Point? WindowToBuffer(global::Windows.Foundation.Point canvasPoint)
    {
        if (_presenter is not { } presenter)
            return null;

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var mapped = presenter.WindowToBuffer(new SKPoint((float)(canvasPoint.X * scale), (float)(canvasPoint.Y * scale)));
        return mapped is { } point ? new global::Windows.Foundation.Point(point.X, point.Y) : null;
    }

    /// <summary>
    /// Converts logical frame-buffer coordinates to this canvas's element coordinates (the
    /// inverse of <see cref="WindowToBuffer"/>). Returns null when not in presenter mode or
    /// before the first painted frame.
    /// </summary>
    /// <param name="bufferPoint">The point in logical frame coordinates.</param>
    public global::Windows.Foundation.Point? BufferToWindow(global::Windows.Foundation.Point bufferPoint)
    {
        if (_presenter is not { } presenter)
            return null;

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var mapped = presenter.BufferToWindow(new SKPoint((float)bufferPoint.X, (float)bufferPoint.Y));
        return mapped is { } point ? new global::Windows.Foundation.Point(point.X / scale, point.Y / scale) : null;
    }

    /// <summary>
    /// Sets the image to be presented on the next paint. Called by the render-surface adapter
    /// on the UI thread.
    /// </summary>
    /// <param name="image">The backbuffer image to present, or <c>null</c> to clear.</param>
    /// <param name="bufferRect">The source region within <paramref name="image"/> to present.</param>
    internal void SetImage(SKImage? image, SKRectI bufferRect)
    {
        lock (_gate)
        {
            _currentImage = image;
            _currentBufferRect = bufferRect;
        }

        // Don't push live engine frames while the window is actively resizing; the resize itself
        // re-blits the most recent frame at the new size. This is re-enabled when the size settles.
        if (!_isResizing)
            Invalidate();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Presenter mode: the presenter draws the game's latest CPU frame and the scene-host
        // path below stays untouched.
        if (_presenter is { } presenter)
        {
            presenter.DrawCurrentFrame(canvas, e.Info.Width, e.Info.Height);
            return;
        }

        SKImage? image;
        lock (_gate)
        {
            image = _currentImage;
        }

        if (image is null)
            return;

        float surfaceW = e.Info.Width;
        float surfaceH = e.Info.Height;
        float imageW = image.Width;
        float imageH = image.Height;

        if (surfaceW <= 0f || surfaceH <= 0f || imageW <= 0f || imageH <= 0f)
            return;

        // Draw the whole backbuffer image, scaled to fit the surface while preserving the
        // backbuffer's aspect ratio, centered. Where the surface aspect differs from the
        // backbuffer aspect, the cleared-black background shows as letterbox/pillarbox bars.
        float scale = Math.Min(surfaceW / imageW, surfaceH / imageH);
        float drawW = imageW * scale;
        float drawH = imageH * scale;
        float offsetX = (surfaceW - drawW) * 0.5f;
        float offsetY = (surfaceH - drawH) * 0.5f;

        var srcRect = new SKRect(0f, 0f, imageW, imageH);
        var dstRect = new SKRect(offsetX, offsetY, offsetX + drawW, offsetY + drawH);
        canvas.DrawImage(image, srcRect, dstRect, new SKSamplingOptions(SKFilterMode.Nearest), null);
    }
}

/// <summary>
/// Provides data for the <see cref="GameSurfaceCanvas.FirstStarted"/> event.
/// </summary>
public sealed class FirstStartedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FirstStartedEventArgs"/> class.
    /// </summary>
    /// <param name="newSize">The canvas size, in pixels, at the first non-zero layout.</param>
    public FirstStartedEventArgs(global::Windows.Foundation.Size newSize) => NewSize = newSize;

    /// <summary>
    /// Gets the canvas size, in pixels, at the first non-zero layout.
    /// </summary>
    public global::Windows.Foundation.Size NewSize { get; }
}

/// <summary>
/// Represents the method that handles the <see cref="GameSurfaceCanvas.FirstStarted"/> event.
/// </summary>
/// <param name="sender">The <see cref="GameSurfaceCanvas"/> that has reached its first layout size.</param>
/// <param name="e">The event data.</param>
public delegate void FirstStartedEventHandler(object sender, FirstStartedEventArgs e);
