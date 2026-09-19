using System;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets.Models;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Implemented by an <see cref="IGameAssetProvider"/> that can turn three-dimensional model
/// assets into engine-native <see cref="GameModel"/> data.
/// </summary>
/// <remarks>
/// <para>
/// This is the model-DATA route for <see cref="GameAssetKind.Model3D"/>: the model arrives as
/// meshes, materials and baked animation frames that a game's own renderer, or a future
/// engine-side 3D feature, uploads to a graphics device. A provider that can instead pre-render a
/// model into sprite frames for the engine's 2D drawing implements
/// <see cref="ITilesheetAssetSource"/> as well; the two routes are independent and a provider may
/// offer either or both.
/// </para>
/// <para>
/// An implementation keeps whatever parsed document it needs cached per asset key, so that
/// <see cref="MaterializeModelAnimation"/> can bake a clip later without re-reading the asset.
/// </para>
/// </remarks>
public interface IModelAssetSource
{
    /// <summary>
    /// Materializes an asset as engine-native model data.
    /// </summary>
    /// <param name="descriptor">The asset to materialize. Its kind must be
    /// <see cref="GameAssetKind.Model3D"/>.</param>
    /// <param name="options">Animation-baking options, or <see langword="null"/> for the
    /// provider's defaults, which bake nothing.</param>
    /// <returns>
    /// The model. <see cref="GameModel.AnimationNames"/> lists every animation the asset offers,
    /// whether or not it was baked; <see cref="GameModel.Animations"/> holds the clips the options
    /// asked for.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented
    /// as model data, for example because its file format is listed but not read by the
    /// provider.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset is malformed.</exception>
    GameModel MaterializeModel(GameAssetDescriptor descriptor, ModelMaterializeOptions? options = null);

    /// <summary>
    /// Bakes one animation of a model asset into vertex frames, on demand.
    /// </summary>
    /// <param name="descriptor">The asset to bake from. Its kind must be
    /// <see cref="GameAssetKind.Model3D"/>.</param>
    /// <param name="animationName">The animation to bake, as named in
    /// <see cref="GameModel.AnimationNames"/>.</param>
    /// <param name="framesPerSecond">The rate to bake at, in frames per second.</param>
    /// <returns>
    /// The baked clip, aligned with the meshes of the model the same asset materializes to, as
    /// described by the alignment guarantee of <see cref="GameModelAnimationClip"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="animationName"/> is null or
    /// whitespace, or the asset has no animation with that name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <paramref name="framesPerSecond"/> is less than one.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot be represented
    /// as model data.</exception>
    /// <exception cref="InvalidDataException">Thrown when the animation evaluates to a different
    /// geometry layout than the model itself.</exception>
    GameModelAnimationClip MaterializeModelAnimation(
        GameAssetDescriptor descriptor,
        string animationName,
        int framesPerSecond = 24);
}
