namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// One triangle mesh of a <see cref="GameModel"/>: flat vertex arrays, triangle indices and the
/// material the triangles are drawn with.
/// </summary>
/// <remarks>
/// <para>
/// COORDINATE CONTRACT — the vertices are in MODEL space, with every node transform of the
/// source asset already applied, so a consumer draws the arrays as they are and needs no node
/// hierarchy. The space is right-handed with +Y up and +Z toward the viewer, which is the glTF
/// convention; a provider for a format that uses another handedness or up-axis converts its
/// geometry before it fills these arrays.
/// </para>
/// <para>
/// The arrays are parallel: vertex <c>v</c> occupies <c>Positions[3v .. 3v + 2]</c>,
/// <c>Normals[3v .. 3v + 2]</c> and <c>TexCoords[2v .. 2v + 1]</c>. They are laid out for direct
/// upload to a graphics device and are never shortened or reordered by an animation clip of the
/// same model.
/// </para>
/// </remarks>
public sealed class GameModelMesh
{
    /// <summary>
    /// Gets the vertex positions, three floats (x, y, z) per vertex, in model space.
    /// </summary>
    public required float[] Positions { get; init; }

    /// <summary>
    /// Gets the vertex normals, three floats per vertex, unit length, in model space.
    /// </summary>
    public required float[] Normals { get; init; }

    /// <summary>
    /// Gets the vertex texture coordinates, two floats (u, v) per vertex. Filled with zeros when
    /// the source asset carried none.
    /// </summary>
    public required float[] TexCoords { get; init; }

    /// <summary>
    /// Gets the triangle indices into the vertex arrays, three per triangle.
    /// </summary>
    public required uint[] Indices { get; init; }

    /// <summary>
    /// Gets the index of this mesh's material in <see cref="GameModel.Materials"/>, or
    /// <c>-1</c> when the mesh uses the default material.
    /// </summary>
    public int MaterialIndex { get; init; } = -1;

    /// <summary>
    /// Gets the number of vertices in the mesh.
    /// </summary>
    public int VertexCount => Positions.Length / 3;

    /// <summary>
    /// Gets the number of triangles in the mesh.
    /// </summary>
    public int TriangleCount => Indices.Length / 3;
}
