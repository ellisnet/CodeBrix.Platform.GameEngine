using System;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can turn image, sprite-atlas, vector or
/// three-dimensional model assets into <see cref="Tilesheet"/> instances.
/// </summary>
/// <remarks>
/// A <see cref="GameAssetKind.Model3D"/> asset is pre-rendered into sprite frames the engine's
/// two-dimensional drawing can use, laid out as <see cref="ModelRenderOptions"/> describes. That
/// route is optional even for a provider that reads models: one which only hands out model data
/// implements <see cref="IModelAssetSource"/> and leaves the kind out of the tilesheet route by not
/// rendering it.
/// </remarks>
public interface ITilesheetAssetSource
{
    /// <summary>
    /// Materializes an asset as a tilesheet and registers it in the engine's
    /// <see cref="TilesheetRegistry"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize. Its kind must be
    /// <see cref="GameAssetKind.Image"/>, <see cref="GameAssetKind.SpriteAtlas"/>,
    /// <see cref="GameAssetKind.Vector"/> or <see cref="GameAssetKind.Model3D"/>.</param>
    /// <param name="options">Grid, collision, rasterization and model-rendering options, or <see langword="null"/> for the provider's defaults.</param>
    /// <returns>The registered tilesheet. Calling this twice for the same asset returns the same instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented as a tilesheet.</exception>
    Tilesheet MaterializeTilesheet(GameAssetDescriptor descriptor, TilesheetMaterializeOptions? options = null);
}
