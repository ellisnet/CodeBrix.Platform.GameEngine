using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.SkiaSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct; //was previously: Gondwana.Drawing.Direct;
/// <summary>
/// Renders an SVG resource directly by rasterizing lazily and caching the bitmap per destination size.
/// </summary>
public sealed class DirectSvg : DirectDrawingMovableBase
{
    private readonly SvgResource _svgResource;
    private readonly SKPaint _paint = new()
    {
        IsAntialias = true,
        BlendMode = SKBlendMode.SrcOver
    };

    private ImageFilterQuality _filterQuality = ImageFilterQuality.Medium;

    private SKBitmap? _cachedBitmap;
    private int _cachedWidth;
    private int _cachedHeight;

    private DirectSvg(SvgResource svgResource,
                      RenderSurfaceHostBase renderSurfaceHost,
                      DirectDrawingMode mode,
                      SceneLayer? sceneLayer,
                      View? view,
                      Rectangle? screenBounds,
                      Rectangle? worldBounds,
                      string? nickname = null)
        : base(renderSurfaceHost, mode, sceneLayer, view, screenBounds, worldBounds, nickname)
    {
        _svgResource = svgResource ?? throw new ArgumentNullException(nameof(svgResource));
    }

    /// <summary>
    /// Initializes a new world-space <see cref="DirectSvg"/>.
    /// </summary>
    public DirectSvg(SvgResource svgResource,
                     RenderSurfaceHostBase renderSurfaceHost,
                     SceneLayer sceneLayer,
                     Rectangle worldBounds,
                     string? nickname = null)
        : this(svgResource, renderSurfaceHost, DirectDrawingMode.SceneLayer, sceneLayer, null, null, worldBounds, nickname)
    { }

    /// <summary>
    /// Initializes a new screen-space <see cref="DirectSvg"/>.
    /// </summary>
    public DirectSvg(SvgResource svgResource,
                     RenderSurfaceHostBase renderSurfaceHost,
                     View view,
                     Rectangle screenBounds,
                     string? nickname = null)
        : this(svgResource, renderSurfaceHost, DirectDrawingMode.View, null, view, screenBounds, null, nickname)
    { }

    /// <summary>
    /// Sets the filter quality used when drawing the cached SVG bitmap.
    /// </summary>
    public DirectSvg SetFilterQuality(ImageFilterQuality quality)
    {
        _filterQuality = quality;
        ForceRefresh();
        return this;
    }

    /// <inheritdoc />
    protected override void OnDraw(BackbufferBase backbuffer, RectangleF destRectScreen)
    {
        int width = Math.Max(1, (int)MathF.Round(destRectScreen.Width));
        int height = Math.Max(1, (int)MathF.Round(destRectScreen.Height));

        if (_cachedBitmap is null || _cachedWidth != width || _cachedHeight != height)
        {
            _cachedBitmap = _svgResource.Rasterize(width, height);
            _cachedWidth = width;
            _cachedHeight = height;
        }

        backbuffer.Canvas.DrawBitmap(_cachedBitmap, destRectScreen.ToPixelAlignedRect().ToSKRect(), _filterQuality.ToSamplingOptions(), _paint);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Bitmap lifetime is owned by SvgResource's per-size cache.
            _cachedBitmap = null;
            _paint.Dispose();
        }

        base.Dispose(disposing);
    }
}
