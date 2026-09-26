using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// A direct drawing that paints the last published <see cref="DrawListSnapshot"/> of a <see cref="DrawList"/> (or
/// of any snapshot source): on a scene layer (world pixels, scrolls with the camera; a pixel layer from
/// <c>Scene.AddPixelLayer</c> is the usual home) or on a view (screen pixels, a HUD or menu over everything).
/// </summary>
/// <remarks>
/// <para>
/// Commands are in the drawing's coordinate space, as for particles: world pixels in scene-layer mode, screen
/// pixels in view mode, clipped to the drawing's bounds. When the destination on screen is larger or smaller than
/// the bounds (camera zoom), the list is scaled with it.
/// </para>
/// <para>
/// Threading: the drawing only ever reads immutable snapshots. <see cref="Update"/> (engine thread, once per
/// rendered frame, after <c>AfterBackgroundTasksExecute</c> and the fixed steps) takes the source's latest
/// snapshot as <see cref="Current"/> and, when it changed, marks the drawing dirty so the CPU tier's
/// dirty-rectangle path repaints it (always dirty while the game keeps publishing, idle when it stops). The CPU
/// tier paints <see cref="Current"/> on the engine thread; the GPU tier paints the source's latest snapshot on the
/// UI thread. A source function passed to a constructor is therefore called on both threads and must only read a
/// published reference (as <see cref="DrawList.Published"/> does).
/// </para>
/// </remarks>
public sealed class DrawListDrawing : DirectDrawingBase
{
    private const int MaxCachedFonts = 64;

    private readonly Func<DrawListSnapshot?> _source;
    private readonly Dictionary<(SKTypeface Typeface, float Size), SKFont> _fonts = new();
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };
    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _textPaint = new() { IsAntialias = true };
    private DrawListSnapshot _current = DrawListSnapshot.Empty;

    /// <summary>Creates a drawing on a scene layer that paints a draw list's published snapshots.</summary>
    /// <param name="renderSurfaceHost">The render surface host.</param>
    /// <param name="sceneLayer">The scene layer.</param>
    /// <param name="worldBounds">The layer area to draw in, in world pixels.</param>
    /// <param name="list">The draw list.</param>
    /// <param name="nickname">An optional name for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="list"/> or <paramref name="renderSurfaceHost"/> is null.</exception>
    public DrawListDrawing(RenderSurfaceHostBase renderSurfaceHost, SceneLayer sceneLayer, Rectangle worldBounds, DrawList list,
        string? nickname = null)
        : this(renderSurfaceHost, sceneLayer, worldBounds, SourceOf(list), nickname)
    {
    }

    /// <summary>Creates a drawing on a view (screen space) that paints a draw list's published snapshots.</summary>
    /// <param name="renderSurfaceHost">The render surface host.</param>
    /// <param name="view">The view.</param>
    /// <param name="screenBounds">The screen area to draw in, in pixels.</param>
    /// <param name="list">The draw list.</param>
    /// <param name="nickname">An optional name for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="list"/> or <paramref name="renderSurfaceHost"/> is null.</exception>
    public DrawListDrawing(RenderSurfaceHostBase renderSurfaceHost, View view, Rectangle screenBounds, DrawList list,
        string? nickname = null)
        : this(renderSurfaceHost, view, screenBounds, SourceOf(list), nickname)
    {
    }

    /// <summary>
    /// Creates a drawing on a scene layer that paints whatever snapshot a source returns, for a game that publishes
    /// several lists together in one object of its own. The source is called on the engine thread and, on the GPU
    /// tier, on the UI thread: it must only read a published reference. A null result paints nothing.
    /// </summary>
    /// <param name="renderSurfaceHost">The render surface host.</param>
    /// <param name="sceneLayer">The scene layer.</param>
    /// <param name="worldBounds">The layer area to draw in, in world pixels.</param>
    /// <param name="source">Returns the latest published snapshot.</param>
    /// <param name="nickname">An optional name for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="renderSurfaceHost"/> is null.</exception>
    public DrawListDrawing(RenderSurfaceHostBase renderSurfaceHost, SceneLayer sceneLayer, Rectangle worldBounds,
        Func<DrawListSnapshot?> source, string? nickname = null)
        : base(Checked(renderSurfaceHost, source), DirectDrawingMode.SceneLayer, sceneLayer, null, null, worldBounds, nickname)
    {
        _source = source;
    }

    /// <summary>
    /// Creates a drawing on a view (screen space) that paints whatever snapshot a source returns. The source is called
    /// on the engine thread and, on the GPU tier, on the UI thread: it must only read a published reference. A null
    /// result paints nothing.
    /// </summary>
    /// <param name="renderSurfaceHost">The render surface host.</param>
    /// <param name="view">The view.</param>
    /// <param name="screenBounds">The screen area to draw in, in pixels.</param>
    /// <param name="source">Returns the latest published snapshot.</param>
    /// <param name="nickname">An optional name for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="renderSurfaceHost"/> is null.</exception>
    public DrawListDrawing(RenderSurfaceHostBase renderSurfaceHost, View view, Rectangle screenBounds,
        Func<DrawListSnapshot?> source, string? nickname = null)
        : base(Checked(renderSurfaceHost, source), DirectDrawingMode.View, null, view, screenBounds, null, nickname)
    {
        _source = source;
    }

    /// <summary>
    /// Gets or sets how pictures are sampled when scaled. Defaults to <see cref="ImageFilterQuality.Low"/> (linear,
    /// no mipmaps): mipmaps of a raster picture are rebuilt on every draw on the CPU tier, which costs far more than
    /// it improves sprites drawn near their own size.
    /// </summary>
    public ImageFilterQuality FilterQuality { get; set; } = ImageFilterQuality.Low;

    /// <summary>
    /// Gets the snapshot taken by the last <see cref="Update"/> (the one the CPU tier paints);
    /// <see cref="DrawListSnapshot.Empty"/> before the first update.
    /// </summary>
    public DrawListSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Advances fades and reveals, takes the source's latest snapshot as <see cref="Current"/>, and marks the drawing
    /// dirty when that snapshot is a different one than before.
    /// </summary>
    /// <param name="tick">The engine tick.</param>
    public override void Update(long tick)
    {
        base.Update(tick);

        var latest = _source() ?? DrawListSnapshot.Empty;
        if (!ReferenceEquals(latest, _current))
        {
            Volatile.Write(ref _current, latest);
            ForceRefresh();
        }
    }

    /// <inheritdoc />
    protected override void OnDraw(BackbufferBase backbuffer, RectangleF destRectScreen)
    {
        var canvas = backbuffer.Canvas;
        if (canvas == null || destRectScreen.Width <= 0 || destRectScreen.Height <= 0)
            return;

        //The CPU tier paints on the engine thread only what Update marked dirty, so it must paint that same
        //  snapshot; the GPU tier repaints everything each frame on the UI thread, so the newest one is right there
        var snapshot = backbuffer.IsGlThreadRendered
            ? _source() ?? DrawListSnapshot.Empty
            : Volatile.Read(ref _current);
        var commands = snapshot.CommandArray;
        if (commands.Length == 0)
            return;

        var source = Mode == DirectDrawingMode.SceneLayer ? WorldBounds : ScreenBounds;
        var scaleX = source.Width > 0 ? destRectScreen.Width / source.Width : 1f;
        var scaleY = source.Height > 0 ? destRectScreen.Height / source.Height : 1f;
        var sampling = FilterQuality.ToSamplingOptions();

        canvas.Save();
        canvas.ClipRect(new SKRect(destRectScreen.Left, destRectScreen.Top, destRectScreen.Right, destRectScreen.Bottom));
        canvas.Translate(destRectScreen.Left, destRectScreen.Top);
        if (scaleX != 1f || scaleY != 1f)
            canvas.Scale(scaleX, scaleY);
        canvas.Translate(-source.Left, -source.Top);

        for (var i = 0; i < commands.Length; i++)
        {
            ref readonly var command = ref commands[i];
            if (command.Alpha <= 0)
                continue;

            var rotated = command.Rotation != 0;
            if (rotated)
            {
                canvas.Save();
                canvas.RotateDegrees(command.Rotation, command.X, command.Y);
            }

            switch (command.Kind)
            {
                case DrawCommandKind.Image:
                    DrawImage(canvas, command, sampling);
                    break;
                case DrawCommandKind.Rectangle:
                    DrawRectangle(canvas, command);
                    break;
                case DrawCommandKind.Circle:
                    DrawCircle(canvas, command);
                    break;
                case DrawCommandKind.Text:
                    DrawText(canvas, command);
                    break;
            }

            if (rotated)
                canvas.Restore();
        }

        canvas.Restore();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imagePaint.Dispose();
            _fillPaint.Dispose();
            _strokePaint.Dispose();
            _textPaint.Dispose();
            ClearFonts();
        }

        base.Dispose(disposing);
    }

    //Checks the source BEFORE the base constructor registers the drawing with DirectDrawingManager, so a rejected
    //  call never leaves a half-built drawing behind for the engine to update
    private static RenderSurfaceHostBase Checked(RenderSurfaceHostBase renderSurfaceHost, Func<DrawListSnapshot?> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return renderSurfaceHost;
    }

    private static Func<DrawListSnapshot?> SourceOf(DrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return () => list.Published;
    }

    private static SKColor WithAlpha(SKColor color, float alpha) =>
        alpha >= 1f ? color : color.WithAlpha((byte)Math.Clamp(color.Alpha * alpha, 0f, 255f));

    private static SKRect BoxOf(in DrawCommand command) =>
        new(command.X - (command.Width / 2), command.Y - (command.Height / 2), command.X + (command.Width / 2),
            command.Y + (command.Height / 2));

    private void DrawImage(SKCanvas canvas, in DrawCommand command, SKSamplingOptions sampling)
    {
        var image = command.Image;
        if (image == null || image.Width <= 0 || image.Height <= 0)
            return;

        SKRect box;
        if (command.Fit == DrawImageFit.Stretch)
        {
            box = BoxOf(command);
        }
        else
        {
            var scale = Math.Min(command.Width / image.Width, command.Height / image.Height);
            var halfWidth = image.Width * scale / 2f;
            var halfHeight = image.Height * scale / 2f;
            box = new SKRect(command.X - halfWidth, command.Y - halfHeight, command.X + halfWidth, command.Y + halfHeight);
        }

        _imagePaint.Color = SKColors.White.WithAlpha((byte)(255 * command.Alpha));
        canvas.DrawImage(image, box, sampling, _imagePaint);
    }

    private void DrawRectangle(SKCanvas canvas, in DrawCommand command)
    {
        var rect = BoxOf(command);
        if (command.Color.Alpha != 0)
        {
            _fillPaint.Color = WithAlpha(command.Color, command.Alpha);
            canvas.DrawRoundRect(rect, command.CornerRadius, command.CornerRadius, _fillPaint);
        }

        if (command.StrokeColor.Alpha != 0 && command.StrokeWidth > 0)
        {
            _strokePaint.Color = WithAlpha(command.StrokeColor, command.Alpha);
            _strokePaint.StrokeWidth = command.StrokeWidth;
            canvas.DrawRoundRect(rect, command.CornerRadius, command.CornerRadius, _strokePaint);
        }
    }

    private void DrawCircle(SKCanvas canvas, in DrawCommand command)
    {
        var radius = command.Width / 2;
        if (command.Color.Alpha != 0)
        {
            _fillPaint.Color = WithAlpha(command.Color, command.Alpha);
            canvas.DrawCircle(command.X, command.Y, radius, _fillPaint);
        }

        if (command.StrokeColor.Alpha != 0 && command.StrokeWidth > 0)
        {
            _strokePaint.Color = WithAlpha(command.StrokeColor, command.Alpha);
            _strokePaint.StrokeWidth = command.StrokeWidth;
            canvas.DrawCircle(command.X, command.Y, radius, _strokePaint);
        }
    }

    private void DrawText(SKCanvas canvas, in DrawCommand command)
    {
        if (string.IsNullOrEmpty(command.Text) || command.Typeface == null || command.FontSize <= 0)
            return;

        var font = FontFor(command.Typeface, command.FontSize);
        var metrics = font.Metrics;
        var baseline = command.Y - ((metrics.Ascent + metrics.Descent) / 2f);
        _textPaint.Color = WithAlpha(command.Color, command.Alpha);
        canvas.DrawText(command.Text, command.X, baseline, command.TextAlign, font, _textPaint);
    }

    //Painting runs on one thread at a time (the engine thread on the CPU tier, the UI thread on the GPU tier), so
    //  the font cache needs no lock; it is emptied when a game animates through many sizes
    private SKFont FontFor(SKTypeface typeface, float size)
    {
        var key = (typeface, size);
        if (_fonts.TryGetValue(key, out var font))
            return font;

        if (_fonts.Count >= MaxCachedFonts)
            ClearFonts();

        font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        _fonts[key] = font;
        return font;
    }

    private void ClearFonts()
    {
        foreach (var font in _fonts.Values)
            font.Dispose();

        _fonts.Clear();
    }
}
