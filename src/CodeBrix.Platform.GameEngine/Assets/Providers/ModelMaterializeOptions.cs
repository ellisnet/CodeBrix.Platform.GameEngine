using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Assets.Models;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Options that steer how an <see cref="IModelAssetSource"/> turns a
/// <see cref="GameAssetKind.Model3D"/> asset into <see cref="GameModel"/> data. Every member is
/// optional; the defaults reproduce the provider's own behaviour, which is to bake no animation.
/// </summary>
/// <remarks>
/// Baking is opt-in because its cost grows with the number of vertices multiplied by the number of
/// frames: a clip holds evaluated positions and normals for every vertex of every frame.
/// <see cref="GameModel.AnimationNames"/> is filled either way, so a caller can materialize the
/// model first and then ask for only the clips it turns out to need, through
/// <see cref="GameAssetProviderRegistry.LoadModelAnimation"/>.
/// </remarks>
public sealed record ModelMaterializeOptions
{
    /// <summary>
    /// Gets the names of the animations to bake into <see cref="GameModel.Animations"/>, or
    /// <see langword="null"/> to bake none. Names are matched case-insensitively against the
    /// animations the asset offers; an empty list also bakes none.
    /// </summary>
    public IReadOnlyList<string>? AnimationNames { get; init; }

    /// <summary>
    /// Gets a value indicating whether every animation the asset offers is baked, which overrides
    /// <see cref="AnimationNames"/>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool BakeAllAnimations { get; init; }

    /// <summary>
    /// Gets the rate the animations are baked at, in frames per second. Defaults to <c>24</c>.
    /// </summary>
    public int AnimationFramesPerSecond { get; init; } = 24;
}
