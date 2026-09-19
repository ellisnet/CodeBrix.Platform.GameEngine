using System.Collections.Generic;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Assets.Models;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Builds the small, deterministic model values the model-data and asset-provider tests need. The
/// geometry is meaningless but structurally correct: the arrays of a mesh are parallel, indices
/// address real vertices, and a clip built for a model satisfies the alignment guarantee.
/// </summary>
internal static class TestGameModels
{
    /// <summary>
    /// Builds a mesh with <paramref name="vertexCount"/> vertices and one triangle per three of
    /// them.
    /// </summary>
    internal static GameModelMesh Mesh(int vertexCount = 3, int materialIndex = -1)
    {
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var texCoords = new float[vertexCount * 2];
        var indices = new uint[vertexCount / 3 * 3];

        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            positions[vertex * 3] = vertex;
            positions[(vertex * 3) + 1] = vertex * 0.5f;
            positions[(vertex * 3) + 2] = vertex * 0.25f;
            normals[(vertex * 3) + 1] = 1.0f;
            texCoords[vertex * 2] = vertex * 0.125f;
        }

        for (int index = 0; index < indices.Length; index++)
            indices[index] = (uint)index;

        return new GameModelMesh
        {
            Positions = positions,
            Normals = normals,
            TexCoords = texCoords,
            Indices = indices,
            MaterialIndex = materialIndex
        };
    }

    /// <summary>
    /// Builds a model, defaulting to one three-vertex mesh and a bounding box of two units in
    /// every direction.
    /// </summary>
    internal static GameModel Model(
        IReadOnlyList<GameModelMesh>? meshes = null,
        IReadOnlyList<GameModelMaterial>? materials = null,
        IReadOnlyList<string>? animationNames = null,
        IReadOnlyList<GameModelAnimationClip>? animations = null,
        Vector3? boundsMin = null,
        Vector3? boundsMax = null,
        Vector3? pivot = null,
        string? name = "test-model")
    {
        return new GameModel
        {
            Name = name,
            Meshes = meshes ?? [Mesh()],
            Materials = materials ?? [],
            BoundsMin = boundsMin ?? new Vector3(-1.0f, -1.0f, -1.0f),
            BoundsMax = boundsMax ?? new Vector3(1.0f, 1.0f, 1.0f),
            Pivot = pivot,
            AnimationNames = animationNames!,
            Animations = animations!
        };
    }

    /// <summary>
    /// Builds the vertex data of one mesh at one frame, with <paramref name="vertexCount"/>
    /// vertices.
    /// </summary>
    internal static GameModelFrameMesh FrameMesh(int vertexCount)
    {
        return new GameModelFrameMesh
        {
            Positions = new float[vertexCount * 3],
            Normals = new float[vertexCount * 3]
        };
    }

    /// <summary>
    /// Builds one frame holding a mesh per entry of <paramref name="vertexCounts"/>.
    /// </summary>
    internal static GameModelAnimationFrame Frame(params int[] vertexCounts)
    {
        var meshes = new List<GameModelFrameMesh>(vertexCounts.Length);

        foreach (int vertexCount in vertexCounts)
            meshes.Add(FrameMesh(vertexCount));

        return new GameModelAnimationFrame { Meshes = meshes };
    }

    /// <summary>
    /// Builds a clip from explicit frames, defaulting to four frames of one three-vertex mesh over
    /// one second.
    /// </summary>
    internal static GameModelAnimationClip Clip(
        string name = "walk",
        float duration = 1.0f,
        int frameRate = 4,
        IReadOnlyList<GameModelAnimationFrame>? frames = null)
    {
        return new GameModelAnimationClip
        {
            Name = name,
            Duration = duration,
            FrameRate = frameRate,
            Frames = frames ?? [Frame(3), Frame(3), Frame(3), Frame(3)]
        };
    }

    /// <summary>
    /// Builds a clip whose every frame lines up with the meshes of <paramref name="model"/>, which
    /// is what a provider is required to produce.
    /// </summary>
    internal static GameModelAnimationClip ClipFor(
        GameModel model,
        string name = "walk",
        int frameCount = 4,
        float duration = 1.0f,
        int frameRate = 4)
    {
        var vertexCounts = new int[model.Meshes.Count];

        for (int mesh = 0; mesh < model.Meshes.Count; mesh++)
            vertexCounts[mesh] = model.Meshes[mesh].VertexCount;

        var frames = new List<GameModelAnimationFrame>(frameCount);

        for (int frame = 0; frame < frameCount; frame++)
            frames.Add(Frame(vertexCounts));

        return Clip(name, duration, frameRate, frames);
    }
}
