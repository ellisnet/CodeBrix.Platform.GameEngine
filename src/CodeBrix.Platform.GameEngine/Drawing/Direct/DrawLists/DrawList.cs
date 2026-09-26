using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// A per-frame list of draw commands (images, rectangles, circles, text) and hit regions, built by the game and
/// handed to the renderer as a finished copy. A <see cref="DrawListDrawing"/> paints the last published copy.
/// </summary>
/// <remarks>
/// <para>
/// The frame loop is: <see cref="Clear"/>, add this frame's commands back to front, <see cref="Publish"/>. Publish
/// copies the commands into an immutable <see cref="DrawListSnapshot"/> and swaps it into <see cref="Published"/>
/// (latest wins). The render side only ever reads a published snapshot, never this builder, so building the next
/// frame on the engine thread while the GPU tier draws on the UI thread is safe by construction.
/// </para>
/// <para>
/// Threading: build (every method except <see cref="Published"/>) on ONE thread, normally the engine thread, for
/// example in <c>Engine.AfterBackgroundTasksExecute</c> or a fixed-step handler. <see cref="Published"/> may be read
/// from any thread.
/// </para>
/// <para>
/// Pictures and typefaces are resolved when a command is added, on the building thread: an asset key goes through
/// <see cref="Images"/>, a typeface key through <see cref="FontManager"/>. The commands then hold the
/// <see cref="SKImage"/> and <see cref="SKTypeface"/> themselves, which the caller keeps alive (do not dispose a
/// tilesheet or remove a font while a published list still uses it).
/// </para>
/// </remarks>
public sealed class DrawList
{
    private readonly List<DrawCommand> _commands;
    private readonly List<DrawHitRegion> _hitRegions = new();
    private DrawListSnapshot _published = DrawListSnapshot.Empty;
    private long _number;

    /// <summary>Creates an empty list.</summary>
    /// <param name="images">
    /// The picture library for asset-key image commands; pass one library to several lists to share it. Null
    /// creates a library for this list.
    /// </param>
    /// <param name="capacity">The initial command capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is negative.</exception>
    public DrawList(DrawImageLibrary? images = null, int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Images = images ?? new DrawImageLibrary();
        _commands = new List<DrawCommand>(capacity);
    }

    /// <summary>Gets the picture library used by <see cref="Image(string, string?, double, double, double, double, double, double, DrawImageFit)"/>.</summary>
    public DrawImageLibrary Images { get; }

    /// <summary>Gets the commands added since the last <see cref="Clear"/> (the list being built, not the published one).</summary>
    public IReadOnlyList<DrawCommand> Commands => _commands;

    /// <summary>Gets the hit regions added since the last <see cref="Clear"/>.</summary>
    public IReadOnlyList<DrawHitRegion> HitRegions => _hitRegions;

    /// <summary>Gets the number of commands added since the last <see cref="Clear"/>.</summary>
    public int Count => _commands.Count;

    /// <summary>
    /// Gets the last published snapshot (<see cref="DrawListSnapshot.Empty"/> before the first publish). Safe to read
    /// from any thread; hit-test pointer input against it so the test matches what the player sees.
    /// </summary>
    public DrawListSnapshot Published => Volatile.Read(ref _published);

    /// <summary>Starts a new frame: removes every command and hit region. The published snapshot is unchanged.</summary>
    public void Clear()
    {
        _commands.Clear();
        _hitRegions.Clear();
    }

    /// <summary>
    /// Copies the commands and hit regions into a new immutable snapshot, numbers it, and makes it the
    /// <see cref="Published"/> one. The builder keeps its contents (call <see cref="Clear"/> to start the next frame).
    /// </summary>
    /// <returns>The published snapshot.</returns>
    public DrawListSnapshot Publish()
    {
        var snapshot = new DrawListSnapshot(_commands.ToArray(), _hitRegions.ToArray(), ++_number);
        Volatile.Write(ref _published, snapshot);
        return snapshot;
    }

    /// <summary>Adds a command made with the <see cref="DrawCommand"/> factories.</summary>
    /// <param name="command">The command.</param>
    public void Add(in DrawCommand command) => _commands.Add(command);

    /// <summary>Adds an image fitted into a box centred on a point. A null picture adds nothing.</summary>
    /// <param name="image">The picture, or null to skip.</param>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="fit">How the picture fills the box.</param>
    /// <returns><see langword="true"/> when a command was added.</returns>
    public bool Image(SKImage? image, double x, double y, double width, double height, double rotation = 0, double alpha = 1,
        DrawImageFit fit = DrawImageFit.Contain)
    {
        if (image == null)
            return false;

        _commands.Add(DrawCommand.ForImage(image, x, y, width, height, rotation, alpha, fit));
        return true;
    }

    /// <summary>Adds a tilesheet frame fitted into a box centred on a point. A frame with no picture adds nothing.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="fit">How the picture fills the box.</param>
    /// <returns><see langword="true"/> when a command was added.</returns>
    public bool Image(Frame frame, double x, double y, double width, double height, double rotation = 0, double alpha = 1,
        DrawImageFit fit = DrawImageFit.Contain) =>
        Image(frame.SkImage, x, y, width, height, rotation, alpha, fit);

    /// <summary>
    /// Adds a picture looked up by asset key and frame name through <see cref="Images"/>, fitted into a box centred on
    /// a point. A picture that cannot be found adds nothing (it is logged once and listed in
    /// <see cref="DrawImageLibrary.Missing"/>).
    /// </summary>
    /// <param name="assetKey">The tilesheet name or asset key.</param>
    /// <param name="frameName">The frame name, or null for a whole picture.</param>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="fit">How the picture fills the box.</param>
    /// <returns><see langword="true"/> when a command was added.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assetKey"/> is null or whitespace.</exception>
    public bool Image(string assetKey, string? frameName, double x, double y, double width, double height, double rotation = 0,
        double alpha = 1, DrawImageFit fit = DrawImageFit.Contain) =>
        Image(Images.Get(assetKey, frameName), x, y, width, height, rotation, alpha, fit);

    /// <summary>Adds a rectangle centred on a point, filled and/or outlined.</summary>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="fill">The fill color; fully transparent for no fill.</param>
    /// <param name="stroke">The outline color; fully transparent (the default) for no outline.</param>
    /// <param name="strokeWidth">The outline width; 0 for no outline.</param>
    /// <param name="cornerRadius">The corner radius.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="rotation">Clockwise degrees about the centre.</param>
    public void Rectangle(double x, double y, double width, double height, SKColor fill, SKColor stroke = default,
        double strokeWidth = 0, double cornerRadius = 0, double alpha = 1, double rotation = 0) =>
        _commands.Add(DrawCommand.ForRectangle(x, y, width, height, fill, stroke, strokeWidth, cornerRadius, alpha, rotation));

    /// <summary>Adds a rectangle given by its edges, filled and/or outlined.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="fill">The fill color; fully transparent for no fill.</param>
    /// <param name="stroke">The outline color; fully transparent (the default) for no outline.</param>
    /// <param name="strokeWidth">The outline width; 0 for no outline.</param>
    /// <param name="cornerRadius">The corner radius.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    public void Rectangle(SKRect rect, SKColor fill, SKColor stroke = default, double strokeWidth = 0, double cornerRadius = 0,
        double alpha = 1) =>
        Rectangle(rect.MidX, rect.MidY, rect.Width, rect.Height, fill, stroke, strokeWidth, cornerRadius, alpha);

    /// <summary>Adds a circle centred on a point, filled and/or outlined.</summary>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="fill">The fill color; fully transparent for no fill.</param>
    /// <param name="stroke">The outline color; fully transparent (the default) for no outline.</param>
    /// <param name="strokeWidth">The outline width; 0 for no outline.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    public void Circle(double x, double y, double radius, SKColor fill, SKColor stroke = default, double strokeWidth = 0,
        double alpha = 1) =>
        _commands.Add(DrawCommand.ForCircle(x, y, radius, fill, stroke, strokeWidth, alpha));

    /// <summary>Adds one line of text in a typeface registered with <see cref="FontManager"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The anchor X: the left edge, centre or right edge of the line, as <paramref name="align"/> says.</param>
    /// <param name="y">The middle of the line.</param>
    /// <param name="typefaceKey">The <see cref="FontManager"/> key of the typeface.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The text color.</param>
    /// <param name="align">Where <paramref name="x"/> sits on the line.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="rotation">Clockwise degrees about the anchor point.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="typefaceKey"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no font is registered under <paramref name="typefaceKey"/>.</exception>
    public void Text(string? text, double x, double y, string typefaceKey, double size, SKColor color,
        SKTextAlign align = SKTextAlign.Center, double alpha = 1, double rotation = 0) =>
        Text(text, x, y, FontManager.Instance.Get(typefaceKey), size, color, align, alpha, rotation);

    /// <summary>Adds one line of text in a typeface the caller holds.</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The anchor X: the left edge, centre or right edge of the line, as <paramref name="align"/> says.</param>
    /// <param name="y">The middle of the line.</param>
    /// <param name="typeface">The typeface.</param>
    /// <param name="size">The font size.</param>
    /// <param name="color">The text color.</param>
    /// <param name="align">Where <paramref name="x"/> sits on the line.</param>
    /// <param name="alpha">Opacity, 0 to 1.</param>
    /// <param name="rotation">Clockwise degrees about the anchor point.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="typeface"/> is null.</exception>
    public void Text(string? text, double x, double y, SKTypeface typeface, double size, SKColor color,
        SKTextAlign align = SKTextAlign.Center, double alpha = 1, double rotation = 0) =>
        _commands.Add(DrawCommand.ForText(text, x, y, typeface, size, color, align, alpha, rotation));

    /// <summary>Adds a hit region (it draws nothing) centred on a point, for pointer tests against <see cref="Published"/>.</summary>
    /// <param name="x">The centre X.</param>
    /// <param name="y">The centre Y.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="id">What the region stands for (a button name, a link, ...).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public void HitRegion(double x, double y, double width, double height, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _hitRegions.Add(new DrawHitRegion((float)x, (float)y, (float)width, (float)height, id));
    }
}
