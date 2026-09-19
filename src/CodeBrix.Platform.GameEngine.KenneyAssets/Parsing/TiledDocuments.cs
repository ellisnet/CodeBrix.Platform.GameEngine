using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// A parsed Tiled map document (<c>.tmx</c>): its grid, the tile sets it draws from, and its layers.
/// </summary>
/// <remarks>
/// This is the map as written, not as imported: global tile ids keep their flip bits, tile set images
/// are still unresolved relative paths, and object layers are data. Turning the document into scene
/// layers is the importer's job.
/// </remarks>
internal sealed record TiledMapDocument
{
    /// <summary>
    /// Gets the archive path of the map document, or an empty string when it was parsed from text
    /// alone.
    /// </summary>
    public string DocumentPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the map orientation, which is always <c>orthogonal</c>: the parser rejects every other
    /// orientation.
    /// </summary>
    public string Orientation { get; init; } = "orthogonal";

    /// <summary>
    /// Gets the render order the map declares, such as <c>right-down</c>, or an empty string when it
    /// declares none.
    /// </summary>
    public string RenderOrder { get; init; } = string.Empty;

    /// <summary>
    /// Gets the map width, in tiles.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the map height, in tiles.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the width of one grid cell, in pixels. A tile set may use a larger tile than this.
    /// </summary>
    public required int TileWidth { get; init; }

    /// <summary>
    /// Gets the height of one grid cell, in pixels. A tile set may use a larger tile than this.
    /// </summary>
    public required int TileHeight { get; init; }

    /// <summary>
    /// Gets the tile set references, in document order.
    /// </summary>
    public IReadOnlyList<TiledTilesetReference> Tilesets { get; init; } = [];

    /// <summary>
    /// Gets the tile layers, bottom-most first.
    /// </summary>
    public IReadOnlyList<TiledTileLayer> TileLayers { get; init; } = [];

    /// <summary>
    /// Gets the object layers, in document order. They are data: nothing in them is drawn.
    /// </summary>
    public IReadOnlyList<TiledObjectLayer> ObjectLayers { get; init; } = [];

    /// <summary>
    /// Gets the image layers, in document order, noted so a caller can report them. The engine has no
    /// equivalent, so an importer cannot reproduce them.
    /// </summary>
    public IReadOnlyList<TiledImageLayer> ImageLayers { get; init; } = [];

    /// <summary>
    /// Gets the custom properties declared on the map.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;

    /// <summary>
    /// Gets the messages describing anything in the document the parser kept but could not fully
    /// represent, such as an image layer or a nested layer group. Empty for a document that parsed
    /// exactly.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// One tile set reference of a map: the global tile id its first tile takes, plus either the relative
/// path of an external tile set document or the tile set declared inline in the map.
/// </summary>
internal sealed record TiledTilesetReference
{
    /// <summary>
    /// Gets the global tile id the tile set's first tile maps to.
    /// </summary>
    public required int FirstGid { get; init; }

    /// <summary>
    /// Gets the path of the external tile set document as the map wrote it, relative to the map, or
    /// <see langword="null"/> when the tile set is inline.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the tile set declared inline in the map, or <see langword="null"/> when the map points at
    /// an external document.
    /// </summary>
    public TiledTilesetDocument? Inline { get; init; }
}

/// <summary>
/// A parsed Tiled tile set (<c>.tsx</c>, or the inline equivalent): the geometry of its tile grid, the
/// image it cuts tiles from, and anything declared about individual tiles.
/// </summary>
internal sealed record TiledTilesetDocument
{
    /// <summary>
    /// Gets the tile set name, or an empty string when it has none.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the archive path of the tile set document, or an empty string for an inline tile set or
    /// one parsed from text alone. Relative paths inside the tile set resolve against this.
    /// </summary>
    public string DocumentPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the width of one tile, in pixels.
    /// </summary>
    public required int TileWidth { get; init; }

    /// <summary>
    /// Gets the height of one tile, in pixels.
    /// </summary>
    public required int TileHeight { get; init; }

    /// <summary>
    /// Gets the number of tiles the tile set declares.
    /// </summary>
    public required int TileCount { get; init; }

    /// <summary>
    /// Gets the number of tile columns in the tile set image, or <c>0</c> for a tile set that holds one
    /// image per tile.
    /// </summary>
    public int Columns { get; init; }

    /// <summary>
    /// Gets the tile set image's path as the document wrote it, relative to the document, or an empty
    /// string for a tile set that holds one image per tile.
    /// </summary>
    public string ImagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the width the document declares for its image, in pixels, or <c>0</c> when it declares
    /// none.
    /// </summary>
    public int ImageWidth { get; init; }

    /// <summary>
    /// Gets the height the document declares for its image, in pixels, or <c>0</c> when it declares
    /// none.
    /// </summary>
    public int ImageHeight { get; init; }

    /// <summary>
    /// Gets the gap between neighbouring tiles in the image, in pixels.
    /// </summary>
    public int Spacing { get; init; }

    /// <summary>
    /// Gets the margin around the tile grid within the image, in pixels.
    /// </summary>
    public int Margin { get; init; }

    /// <summary>
    /// Gets the horizontal drawing offset the tile set declares, in pixels. Kenney uses it to line a
    /// tile set up with a grid it does not match, such as 24-pixel characters on an 18-pixel map.
    /// </summary>
    public int TileOffsetX { get; init; }

    /// <summary>
    /// Gets the vertical drawing offset the tile set declares, in pixels.
    /// </summary>
    public int TileOffsetY { get; init; }

    /// <summary>
    /// Gets the custom properties declared on the tile set.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;

    /// <summary>
    /// Gets the tiles the document says something about, by local tile id. Tiles it says nothing about
    /// are absent.
    /// </summary>
    public IReadOnlyList<TiledTilesetTile> Tiles { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this is an image-collection tile set - one image per tile
    /// rather than one grid image. The engine's tilesheets are grid images, so an importer cannot
    /// reproduce it.
    /// </summary>
    public bool IsImageCollection =>
        ImagePath.Length == 0 && Tiles.Any(t => !string.IsNullOrEmpty(t.ImagePath));
}

/// <summary>
/// What a tile set document declares about one of its tiles.
/// </summary>
internal sealed record TiledTilesetTile
{
    /// <summary>
    /// Gets the tile's identifier within its own tile set.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the tile's type or class, or <see langword="null"/> when it has none.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the path of the tile's own image as the document wrote it, relative to the document, or
    /// <see langword="null"/> for a tile cut from the tile set's grid image.
    /// </summary>
    public string? ImagePath { get; init; }

    /// <summary>
    /// Gets the custom properties declared on the tile.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;

    /// <summary>
    /// Gets a value indicating whether the tile carries per-tile collision shapes. The engine's tile
    /// collision is a rectangle, so an importer can report the shapes but not reproduce them.
    /// </summary>
    public bool HasCollisionShapes { get; init; }
}

/// <summary>
/// One tile layer of a map: the global tile ids of its cells in row-major order.
/// </summary>
internal sealed record TiledTileLayer
{
    /// <summary>
    /// Gets the layer identifier the map assigned, or <c>0</c> when it assigned none.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the layer name, or an empty string when it has none.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the position of the layer among all of the map's layers, in document order, counting tile,
    /// object and image layers alike. An importer uses it to keep z-order faithful to the map.
    /// </summary>
    public int DocumentIndex { get; init; }

    /// <summary>
    /// Gets the layer width, in tiles.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the layer height, in tiles.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the global tile ids in row-major order, with the Tiled flip bits still set; <c>0</c> is an
    /// empty cell. Use <see cref="TiledGid"/> to take the bits apart. The array is the parser's own and
    /// is not copied on access.
    /// </summary>
    public required uint[] Gids { get; init; }

    /// <summary>
    /// Gets a value indicating whether the layer is visible.
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the layer opacity, from <c>0</c> to <c>1</c>.
    /// </summary>
    public float Opacity { get; init; } = 1.0f;

    /// <summary>
    /// Gets the layer's horizontal drawing offset, in pixels.
    /// </summary>
    public float OffsetX { get; init; }

    /// <summary>
    /// Gets the layer's vertical drawing offset, in pixels.
    /// </summary>
    public float OffsetY { get; init; }

    /// <summary>
    /// Gets the layer's horizontal parallax factor, where <c>1</c> means it moves with the camera.
    /// </summary>
    public float ParallaxX { get; init; } = 1.0f;

    /// <summary>
    /// Gets the layer's vertical parallax factor, where <c>1</c> means it moves with the camera.
    /// </summary>
    public float ParallaxY { get; init; } = 1.0f;

    /// <summary>
    /// Gets the custom properties declared on the layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;
}

/// <summary>
/// One object layer of a map, with the objects it holds. Objects are data: an importer hands them to
/// the game rather than turning them into engine entities.
/// </summary>
internal sealed record TiledObjectLayer
{
    /// <summary>
    /// Gets the layer identifier the map assigned, or <c>0</c> when it assigned none.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the layer name, or an empty string when it has none.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the position of the layer among all of the map's layers, in document order.
    /// </summary>
    public int DocumentIndex { get; init; }

    /// <summary>
    /// Gets a value indicating whether the layer is visible.
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the layer's opacity, from 0 (fully transparent) to 1 (fully opaque).
    /// </summary>
    public float Opacity { get; init; } = 1.0f;

    /// <summary>
    /// Gets the layer's horizontal drawing offset, in pixels.
    /// </summary>
    public float OffsetX { get; init; }

    /// <summary>
    /// Gets the layer's vertical drawing offset, in pixels.
    /// </summary>
    public float OffsetY { get; init; }

    /// <summary>
    /// Gets the objects of the layer, in document order.
    /// </summary>
    public IReadOnlyList<TiledMapObject> Objects { get; init; } = [];

    /// <summary>
    /// Gets the custom properties declared on the layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;
}

/// <summary>
/// One object of an object layer, as the map wrote it.
/// </summary>
internal sealed record TiledMapObject
{
    /// <summary>
    /// Gets the identifier the map assigned to the object.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the object's name, or an empty string when it is unnamed.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object's type or class, or an empty string when it has none.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object's left edge in map pixels.
    /// </summary>
    public float X { get; init; }

    /// <summary>
    /// Gets the object's top edge in map pixels. For a tile object Tiled writes the BOTTOM edge here,
    /// which is the map's convention, not an error.
    /// </summary>
    public float Y { get; init; }

    /// <summary>
    /// Gets the object's width in pixels, or <c>0</c> for a point.
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// Gets the object's height in pixels, or <c>0</c> for a point.
    /// </summary>
    public float Height { get; init; }

    /// <summary>
    /// Gets the object's rotation in degrees, clockwise.
    /// </summary>
    public float Rotation { get; init; }

    /// <summary>
    /// Gets the global tile id the object draws, flip bits included, or <c>0</c> when the object is not
    /// a tile object.
    /// </summary>
    public uint Gid { get; init; }

    /// <summary>
    /// Gets a value indicating whether the object is visible.
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the custom properties declared on the object.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;
}

/// <summary>
/// One image layer of a map, noted so a caller can report it. The engine has no per-layer background
/// image, so an importer cannot reproduce it.
/// </summary>
internal sealed record TiledImageLayer
{
    /// <summary>
    /// Gets the layer identifier the map assigned, or <c>0</c> when it assigned none.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the layer name, or an empty string when it has none.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the position of the layer among all of the map's layers, in document order.
    /// </summary>
    public int DocumentIndex { get; init; }

    /// <summary>
    /// Gets the image path as the map wrote it, relative to the map, or an empty string when the layer
    /// names no image.
    /// </summary>
    public string ImagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the layer is visible.
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the layer's horizontal drawing offset, in pixels.
    /// </summary>
    public float OffsetX { get; init; }

    /// <summary>
    /// Gets the layer's vertical drawing offset, in pixels.
    /// </summary>
    public float OffsetY { get; init; }

    /// <summary>
    /// Gets the custom properties declared on the layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = TiledProperties.Empty;
}

/// <summary>
/// The empty property dictionary every Tiled document type defaults to.
/// </summary>
internal static class TiledProperties
{
    /// <summary>
    /// Gets a shared, empty, case-insensitive property dictionary.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Empty { get; } =
        new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
}
