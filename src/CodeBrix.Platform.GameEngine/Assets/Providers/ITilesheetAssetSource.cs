using System;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can turn image, sprite-atlas or vector
/// assets into <see cref="Tilesheet"/> instances.
/// </summary>
public interface ITilesheetAssetSource
{
    /// <summary>
    /// Materializes an asset as a tilesheet and registers it in the engine's
    /// <see cref="TilesheetRegistry"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize. Its kind must be
    /// <see cref="GameAssetKind.Image"/>, <see cref="GameAssetKind.SpriteAtlas"/> or
    /// <see cref="GameAssetKind.Vector"/>.</param>
    /// <param name="options">Grid, collision and rasterization options, or <see langword="null"/> for the provider's defaults.</param>
    /// <returns>The registered tilesheet. Calling this twice for the same asset returns the same instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented as a tilesheet.</exception>
    Tilesheet MaterializeTilesheet(GameAssetDescriptor descriptor, TilesheetMaterializeOptions? options = null);
}
