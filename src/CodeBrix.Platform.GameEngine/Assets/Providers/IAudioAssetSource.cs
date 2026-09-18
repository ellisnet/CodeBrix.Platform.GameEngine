using System;
using CodeBrix.Platform.GameEngine.Audio;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can turn audio assets into
/// <see cref="AudioResource"/> instances.
/// </summary>
public interface IAudioAssetSource
{
    /// <summary>
    /// Materializes an asset as an audio resource and registers it in the engine's
    /// <see cref="AudioResourceManager"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize. Its kind must be <see cref="GameAssetKind.Audio"/>.</param>
    /// <param name="volume">The default playback volume, where <c>1</c> is unattenuated.</param>
    /// <param name="pan">The default stereo pan, from <c>-1</c> (left) to <c>1</c> (right).</param>
    /// <returns>The registered audio resource. Calling this twice for the same asset returns the same instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented as an audio resource.</exception>
    AudioResource MaterializeAudio(GameAssetDescriptor descriptor, float volume = 1.0f, float pan = 0.0f);
}
