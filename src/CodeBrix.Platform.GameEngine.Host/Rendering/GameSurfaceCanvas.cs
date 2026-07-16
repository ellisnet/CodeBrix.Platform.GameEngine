using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// A CodeBrix.Platform SkiaSharp canvas control that displays the game engine's rendered frames.
/// The paired <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/> feeds it the latest
/// backbuffer image; this control blits that image onto its Skia surface each paint.
/// </summary>
public class GameSurfaceCanvas : SKXamlCanvas
{
    private readonly object _gate = new();
    private SKImage? _currentImage;
    private SKRectI _currentBufferRect;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSurfaceCanvas"/> class.
    /// </summary>
    public GameSurfaceCanvas()
    {
        PaintSurface += OnPaintSurface;
        SizeChanged += (_, _) => Invalidate();
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
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        SKImage? image;
        SKRectI src;
        lock (_gate)
        {
            image = _currentImage;
            src = _currentBufferRect;
        }

        if (image is null)
            return;

        var srcRect = new SKRect(src.Left, src.Top, src.Right, src.Bottom);
        var dstRect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
        canvas.DrawImage(image, srcRect, dstRect, new SKSamplingOptions(SKFilterMode.Nearest), null);
    }
}
