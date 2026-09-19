namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// The evaluated vertex data of one mesh at one frame of a
/// <see cref="GameModelAnimationClip"/>.
/// </summary>
/// <remarks>
/// Only the data that animation changes is carried. Texture coordinates, indices and the material
/// come from the matching <see cref="GameModelMesh"/> of the model and never change over a clip,
/// which is what lets a consumer overwrite just these two arrays per frame.
/// </remarks>
public sealed class GameModelFrameMesh
{
    /// <summary>
    /// Gets the vertex positions at this frame, three floats (x, y, z) per vertex, in the same
    /// layout and model space as <see cref="GameModelMesh.Positions"/>.
    /// </summary>
    public required float[] Positions { get; init; }

    /// <summary>
    /// Gets the vertex normals at this frame, three floats per vertex, unit length, in the same
    /// layout and model space as <see cref="GameModelMesh.Normals"/>.
    /// </summary>
    public required float[] Normals { get; init; }
}
