using System.Collections.Generic;
using System.Drawing;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// A single object read from a tile map's object layer. This is data only: the import never turns
/// an object into an engine entity, it hands the values to the game to act on.
/// </summary>
public sealed record TiledObject
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
    /// Gets the object's rectangle in map pixels, as written in the map document.
    /// </summary>
    /// <remarks>
    /// This is the UNROTATED rectangle: <see cref="Rotation"/> turns the object about its own origin
    /// and is not folded into these values. A tile object is written with its BOTTOM edge in the map's
    /// <c>y</c> attribute, which is the map format's own convention rather than an error.
    /// </remarks>
    public RectangleF Bounds { get; init; }

    /// <summary>
    /// Gets the object's rotation in degrees, clockwise, as written in the map document. Zero for an
    /// object the map did not turn.
    /// </summary>
    /// <remarks>
    /// The rotation is about the object's own origin, which is the point the map's <c>x</c> and
    /// <c>y</c> attributes name, so a game rotates <see cref="Bounds"/> about that corner rather than
    /// about its centre.
    /// </remarks>
    public float Rotation { get; init; }

    /// <summary>
    /// Gets a value indicating whether the map marks the object as visible. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// An import never acts on this, because it never turns an object into an engine entity; it is
    /// reported so a game can honour what the map author decided.
    /// </remarks>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the tile a TILE OBJECT draws, or <see langword="null"/> when the object is a shape, a
    /// point or a piece of text rather than a tile.
    /// </summary>
    /// <remarks>
    /// A tile object names a tile of one of the map's tile sets, optionally flipped, so the same
    /// <see cref="TiledTileInfo"/> that describes a tile of a tile layer describes it here: the tile
    /// set it came from, its global and tile-set-local identifiers, the properties declared on the
    /// tile and on its tile set, and the three flip flags.
    /// <see cref="TiledTileInfo.LayerName"/> is the object layer's name, and
    /// <see cref="TiledTileInfo.Column"/> and <see cref="TiledTileInfo.Row"/> are both zero, because
    /// an object sits at pixel coordinates rather than in a grid cell.
    /// </remarks>
    public TiledTileInfo? Tile { get; init; }

    /// <summary>
    /// Gets the custom properties defined on the object.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
