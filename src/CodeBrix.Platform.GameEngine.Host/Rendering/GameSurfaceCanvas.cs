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
/// It owns the Tier A (CPU) <see cref="RenderSurfaceHost{TBackbuffer}"/> that the engine renders into
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

    private CodeBrixPlatformBitmapRenderSurfaceAdapter? _adapter;
    private RenderSurfaceHost<BitmapBackbuffer>? _host;
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
    /// then start the engine, to render into this control.
    /// </summary>
    public RenderSurfaceHost<BitmapBackbuffer> Host
    {
        get
        {
            EnsureHost();
            return _host!;
        }
    }

    /// <summary>
    /// Gets the Tier A (CPU) render-surface adapter that feeds engine frames to this canvas.
    /// </summary>
    public CodeBrixPlatformBitmapRenderSurfaceAdapter RenderSurfaceAdapter
    {
        get
        {
            EnsureHost();
            return _adapter!;
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

        _adapter = new CodeBrixPlatformBitmapRenderSurfaceAdapter(this, _renderWidth, _renderHeight);
        _host = new RenderSurfaceHost<BitmapBackbuffer>(_adapter);
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
