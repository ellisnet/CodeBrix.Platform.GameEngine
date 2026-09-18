using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Everything a <see cref="TiledMapImportOptions.CollisionSelector"/> is told about one tile of an
/// imported map.
/// </summary>
public sealed record TiledTileInfo
{
    /// <summary>
    /// Gets the name of the map layer the tile belongs to.
    /// </summary>
    public required string LayerName { get; init; }

    /// <summary>
    /// Gets the zero-based column of the tile within the layer grid.
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Gets the zero-based row of the tile within the layer grid.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// Gets the map-wide tile identifier, with any flip flags already removed.
    /// </summary>
    public int GlobalTileId { get; init; }

    /// <summary>
    /// Gets the tile's identifier within its own tile set.
    /// </summary>
    public int LocalTileId { get; init; }

    /// <summary>
    /// Gets the name of the tile set the tile came from.
    /// </summary>
    public string TilesetName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tile's type or class as declared in its tile set, or <see langword="null"/> when
    /// it has none.
    /// </summary>
    public string? TileType { get; init; }

    /// <summary>
    /// Gets the custom properties declared on the tile itself.
    /// </summary>
    public IReadOnlyDictionary<string, string> TileProperties { get; init; } = EmptyProperties;

    /// <summary>
    /// Gets the custom properties declared on the tile's tile set.
    /// </summary>
    public IReadOnlyDictionary<string, string> TilesetProperties { get; init; } = EmptyProperties;

    /// <summary>
    /// Gets a value indicating whether the map flipped the tile horizontally.
    /// </summary>
    public bool FlippedHorizontally { get; init; }

    /// <summary>
    /// Gets a value indicating whether the map flipped the tile vertically.
    /// </summary>
    public bool FlippedVertically { get; init; }

    /// <summary>
    /// Gets a value indicating whether the map flipped the tile diagonally.
    /// </summary>
    public bool FlippedDiagonally { get; init; }

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
