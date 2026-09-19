using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// One baked frame of a <see cref="GameModelAnimationClip"/>: the evaluated vertex data of every
/// mesh of the model at one instant.
/// </summary>
public sealed class GameModelAnimationFrame
{
    /// <summary>
    /// Gets the per-mesh vertex data of the frame, aligned with
    /// <see cref="GameModel.Meshes"/>: entry <c>i</c> belongs to mesh <c>i</c> of the model and
    /// carries the same vertex count.
    /// </summary>
    public required IReadOnlyList<GameModelFrameMesh> Meshes { get; init; }
}
