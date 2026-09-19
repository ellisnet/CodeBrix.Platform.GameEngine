using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// The result of importing a tile map into a scene through
/// <see cref="ITiledMapAssetSource.MaterializeTiledMap"/>.
/// </summary>
public sealed record TiledMapImport
{
    /// <summary>
    /// Gets the scene the layers were added to.
    /// </summary>
    public required Scene Scene { get; init; }

    /// <summary>
    /// Gets the imported layers, in map order (bottom first).
    /// </summary>
    public IReadOnlyList<SceneLayer> Layers { get; init; } = [];

    /// <summary>
    /// Gets the tilesheets the import registered, one per tile set the map REFERENCES, in the order
    /// the map references them.
    /// </summary>
    /// <remarks>
    /// A tile set the map references is prepared whether or not an imported layer draws a tile from
    /// it, so this list does not change with <see cref="TiledMapImportOptions.LayerFilter"/>.
    /// </remarks>
    public IReadOnlyList<Tilesheet> Tilesheets { get; init; } = [];

    /// <summary>
    /// Gets the object layers captured as data, empty when
    /// <see cref="TiledMapImportOptions.ImportObjectLayers"/> was turned off.
    /// </summary>
    public IReadOnlyList<TiledObjectGroup> ObjectGroups { get; init; } = [];

    /// <summary>
    /// Gets the size of the whole map in pixels.
    /// </summary>
    public Size MapSizePx { get; init; }

    /// <summary>
    /// Gets the map's grid tile size in pixels.
    /// </summary>
    public Size TileSize { get; init; }

    /// <summary>
    /// Gets the messages describing anything the import could not reproduce exactly, such as a
    /// layer opacity the engine has no equivalent for. Empty when the import was exact.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
