using System;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// One drawing instruction on a <see cref="DrawList"/>: an image, a rectangle, a circle or a line of text, with its
/// position, size, rotation and opacity. A command holds only values and references to immutable Skia objects
/// (the <see cref="SKImage"/> and <see cref="SKTypeface"/> were resolved when it was made), so a published list can
/// be drawn on another thread while the next one is being built.
/// </summary>
/// <remarks>
/// Positions are the centre of the shape (images, rectangles, circles) or the anchor point of a text line, in the
/// coordinates of the drawing that paints the list: world pixels for a scene-layer drawing, screen pixels for a
/// view drawing. Rotation is clockwise degrees about that point. Create commands with the static factories here or
/// with the matching <see cref="DrawList"/> methods.
/// </remarks>
public readonly struct DrawCommand
{
    private DrawCommand(DrawCommandKind kind, float x, float y, float width, float height, float rotation, float alpha,
        SKImage? image = null, DrawImageFit fit = DrawImageFit.Contain, SKColor color = default, SKColor strokeColor = default,
        float strokeWidth = 0, float cornerRadius = 0, string? text = null, SKTypeface? typeface = null, float fontSize = 0,
        SKTextAlign textAlign = SKTextAlign.Center)
    {
        Kind = kind;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Rotation = rotation;
        Alpha = alpha;
        Image = image;
        Fit = fit;
        Color = color;
        StrokeColor = strokeColor;
        StrokeWidth = strokeWidth;
        CornerRadius = cornerRadius;
        Text = text;
        Typeface = typeface;
        FontSize = fontSize;
        TextAlign = textAlign;
    }

    /// <summary>Gets what the command draws.</summary>
    public DrawCommandKind Kind { get; }

    /// <summary>Gets the centre X (images, rectangles, circles) or the anchor X of a text line.</summary>
    public float X { get; }

    /// <summary>Gets the centre Y (images, rectangles, circles) or the middle of a text line.</summary>
    public float Y { get; }

    /// <summary>Gets the box width (images, rectangles) or the diameter (circles); 0 for text.</summary>
    public float Width { get; }

    /// <summary>Gets the box height (images, rectangles) or the diameter (circles); 0 for text.</summary>
    public float Height { get; }

    /// <summary>Gets the clockwise rotation in degrees about (<see cref="X"/>, <see cref="Y"/>).</summary>
    public float Rotation { get; }

    /// <summary>Gets the opacity, 0 to 1, applied on top of the colors' own alpha.</summary>
    public float Alpha { get; }

    /// <summary>Gets the picture of an image command; <see langword="null"/> for the other kinds.</summary>
    public SKImage? Image { get; }

    /// <summary>Gets how an image command fills its box.</summary>
    public DrawImageFit Fit { get; }

    /// <summary>Gets the fill color (rectangles, circles) or the text color. A fully transparent fill draws no fill.</summary>
    public SKColor Color { get; }

    /// <summary>Gets the outline color (rectangles, circles). A fully transparent color draws no outline.</summary>
    public SKColor StrokeColor { get; }

    /// <summary>Gets the outline width (rectangles, circles); 0 draws no outline.</summary>
    public float StrokeWidth { get; }

    /// <summary>Gets the corner radius of a rectangle.</summary>
    public float CornerRadius { get; }

    /// <summary>Gets the text of a text command; <see langword="null"/> for the other kinds.</summary>
    public string? Text { get; }

    /// <summary>Gets the typeface of a text command; <see langword="null"/> for the other kinds.</summary>
    public SKTypeface? Typeface { get; }

    /// <summary>Gets the font size of a text command.</summary>
    public float FontSize { get; }

    /// <summary>Gets where <see cref="X"/> sits on a text line: its left edge, centre or right edge.</summary>
    public SKTextAlign TextAlign { get; }

    /// <summary>Creates an image command: a picture fitted into a box centred on a point.</summary>
    /// <param name="image">The picture. It stays owned by the caller and must outlive every list that uses it.</param>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    /// <param name="alpha">Opacity, 0 to 1 (clamped).</param>
    /// <param name="fit">How the picture fills the box.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
    public static DrawCommand ForImage(SKImage image, double x, double y, double width, double height, double rotation = 0,
        double alpha = 1, DrawImageFit fit = DrawImageFit.Contain)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new DrawCommand(DrawCommandKind.Image, (float)x, (float)y, (float)width, (float)height, (float)rotation,
            Clamp01(alpha), image: image, fit: fit);
    }

    /// <summary>Creates a rectangle command: a rectangle centred on a point, filled and/or outlined.</summary>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="fill">The fill color; fully transparent (the default of <see cref="SKColor"/>) for no fill.</param>
    /// <param name="stroke">The outline color; fully transparent for no outline.</param>
    /// <param name="strokeWidth">The outline width; 0 for no outline.</param>
    /// <param name="cornerRadius">The corner radius.</param>
    /// <param name="alpha">Opacity, 0 to 1 (clamped).</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    /// <returns>The command.</returns>
    public static DrawCommand ForRectangle(double x, double y, double width, double height, SKColor fill,
        SKColor stroke = default, double strokeWidth = 0, double cornerRadius = 0, double alpha = 1, double rotation = 0) =>
        new(DrawCommandKind.Rectangle, (float)x, (float)y, (float)width, (float)height, (float)rotation, Clamp01(alpha),
            color: fill, strokeColor: stroke, strokeWidth: (float)Math.Max(0, strokeWidth),
            cornerRadius: (float)Math.Max(0, cornerRadius));

    /// <summary>Creates a circle command: a circle centred on a point, filled and/or outlined.</summary>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="fill">The fill color; fully transparent for no fill.</param>
    /// <param name="stroke">The outline color; fully transparent (the default) for no outline.</param>
    /// <param name="strokeWidth">The outline width; 0 for no outline.</param>
    /// <param name="alpha">Opacity, 0 to 1 (clamped).</param>
    /// <returns>The command.</returns>
    public static DrawCommand ForCircle(double x, double y, double radius, SKColor fill, SKColor stroke = default,
        double strokeWidth = 0, double alpha = 1)
    {
        var diameter = (float)Math.Max(0, radius * 2);
        return new DrawCommand(DrawCommandKind.Circle, (float)x, (float)y, diameter, diameter, 0, Clamp01(alpha),
            color: fill, strokeColor: stroke, strokeWidth: (float)Math.Max(0, strokeWidth));
    }

    /// <summary>Creates a text command: one line of text anchored at a point.</summary>
    /// <param name="text">The text (drawn on one line; line breaks are not interpreted).</param>
    /// <param name="x">The anchor X: the left edge, centre or right edge of the line, as <paramref name="align"/> says.</param>
    /// <param name="y">The middle of the line (half way between the font's ascent and descent).</param>
    /// <param name="typeface">The typeface. It stays owned by the caller (normally <c>FontManager</c>).</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The text color.</param>
    /// <param name="align">Where <paramref name="x"/> sits on the line.</param>
    /// <param name="alpha">Opacity, 0 to 1 (clamped).</param>
    /// <param name="rotation">Clockwise degrees about the anchor point.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="typeface"/> is null.</exception>
    public static DrawCommand ForText(string? text, double x, double y, SKTypeface typeface, double size, SKColor color,
        SKTextAlign align = SKTextAlign.Center, double alpha = 1, double rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        return new DrawCommand(DrawCommandKind.Text, (float)x, (float)y, 0, 0, (float)rotation, Clamp01(alpha),
            color: color, text: text ?? string.Empty, typeface: typeface, fontSize: (float)Math.Max(0, size), textAlign: align);
    }

    private static float Clamp01(double value) => value > 0 ? (value < 1 ? (float)value : 1f) : 0f;
}
