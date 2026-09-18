using System;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can import tile map documents into a
/// <see cref="Scene"/>.
/// </summary>
public interface ITiledMapAssetSource
{
    /// <summary>
    /// Imports a tile map asset into the supplied scene, adding one
    /// <see cref="SceneLayer"/> per tile layer of the map.
    /// </summary>
    /// <param name="descriptor">The asset to import. Its kind must be <see cref="GameAssetKind.TiledMap"/>.</param>
    /// <param name="scene">The scene that receives the imported layers.</param>
    /// <param name="options">Z-order, parallax, collision and filtering options, or <see langword="null"/> for the provider's defaults.</param>
    /// <returns>The layers, tilesheets, object data and warnings produced by the import.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> or <paramref name="scene"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the map uses a feature the importer cannot represent.</exception>
    TiledMapImport MaterializeTiledMap(GameAssetDescriptor descriptor, Scene scene, TiledMapImportOptions? options = null);
}
