using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// A parsed Kenney <c>TextureAtlas</c> XML document: the sheet image it describes plus the named
/// rectangles cut from it.
/// </summary>
/// <remarks>
/// The rectangles are arbitrary, not a uniform grid, which is why a materialized atlas becomes one
/// single-tile tilesheet region per frame rather than one gridded region.
/// </remarks>
internal sealed record SpriteAtlasDocument
{
    /// <summary>
    /// Gets the atlas name: the XML file's name without its extension.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the archive path of the XML document.
    /// </summary>
    public required string DocumentPath { get; init; }

    /// <summary>
    /// Gets the archive path of the sheet image. It is the document's own <c>imagePath</c> resolved
    /// against the XML's folder until <see cref="SpriteAtlasParser.TryResolveImagePath"/> replaces it
    /// with a path that exists in the archive.
    /// </summary>
    public required string ImagePath { get; init; }

    /// <summary>
    /// Gets the sheet image path exactly as the document declared it, which some Kenney bundles ship
    /// stale.
    /// </summary>
    public required string DeclaredImagePath { get; init; }

    /// <summary>
    /// Gets the named frames of the atlas, in document order.
    /// </summary>
    public IReadOnlyList<SpriteAtlasFrame> Frames { get; init; } = [];
}

/// <summary>
/// One named rectangle inside a sprite sheet image, as declared by a <c>SubTexture</c> element.
/// </summary>
internal sealed record SpriteAtlasFrame
{
    /// <summary>
    /// Gets the frame name, which in Kenney's bundles is the original sprite's file name and so
    /// usually carries a <c>.png</c> suffix.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the left edge of the frame within the sheet image, in pixels.
    /// </summary>
    public required int X { get; init; }

    /// <summary>
    /// Gets the top edge of the frame within the sheet image, in pixels.
    /// </summary>
    public required int Y { get; init; }

    /// <summary>
    /// Gets the width of the frame, in pixels.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height of the frame, in pixels.
    /// </summary>
    public required int Height { get; init; }
}
