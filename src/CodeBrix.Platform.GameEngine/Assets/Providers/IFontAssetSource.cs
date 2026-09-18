using System;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can turn font assets into
/// <see cref="SKTypeface"/> instances.
/// </summary>
public interface IFontAssetSource
{
    /// <summary>
    /// Materializes an asset as a typeface and registers it in the engine's
    /// <see cref="Rendering.Text.FontManager"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize. Its kind must be <see cref="GameAssetKind.Font"/>.</param>
    /// <returns>The registered typeface. Calling this twice for the same asset returns the same instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented as a typeface.</exception>
    SKTypeface MaterializeFont(GameAssetDescriptor descriptor);
}
