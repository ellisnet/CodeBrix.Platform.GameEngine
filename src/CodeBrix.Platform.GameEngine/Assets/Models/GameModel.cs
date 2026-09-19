using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// A three-dimensional model in engine-native, renderer-neutral form: triangle meshes in model
/// space, the materials they reference, the bounds a camera frames them with, and the animation
/// clips that were baked for them.
/// </summary>
/// <remarks>
/// <para>
/// A model is pure data. It holds no graphics-device handles and nothing to dispose, it is
/// immutable once built, and it is safe to share between threads. The engine does not draw 3D
/// itself: a model is either data for the game's own renderer, or the input a provider
/// pre-renders into sprite frames (see
/// <see cref="Providers.ModelRenderOptions"/>).
/// </para>
/// <para>
/// THE THREE CONTRACT GUARANTEES every <see cref="Providers.IModelAssetSource"/> honours, so a
/// consumer can upload a model to a graphics device once and then animate it by overwriting
/// vertex data:
/// </para>
/// <para>
/// (1) ALIGNMENT — in every clip of <see cref="Animations"/>, and in every clip the provider
/// bakes later for the same asset, <c>Frame.Meshes[i]</c> corresponds to <c>Meshes[i]</c> of this
/// model and carries the same vertex count, in the same order. A consumer therefore allocates
/// its buffers once from <see cref="Meshes"/> and overwrites positions and normals per frame.
/// </para>
/// <para>
/// (2) UPLOAD-FRIENDLY PAYLOADS — geometry is flat <see cref="float"/> arrays, indices are
/// <see cref="uint"/>, and base-colour textures are RGBA8888 bytes
/// (<see cref="GameModelMaterial.BaseColorTextureRgba"/>). Nothing needs unpacking or
/// reinterpreting before it is handed to a graphics device.
/// </para>
/// <para>
/// (3) TIMING BELONGS TO THE CONSUMER — a clip carries its
/// <see cref="GameModelAnimationClip.Duration"/> and
/// <see cref="GameModelAnimationClip.FrameRate"/> and samples <c>[0, Duration)</c>, so a looping
/// clip never repeats its end pose. The model holds no playback state.
/// </para>
/// <para>
/// <see cref="AnimationNames"/> lists every clip the source asset offers, whether or not it was
/// baked; <see cref="Animations"/> holds only the clips that were baked on request. Baking is
/// opt-in because its cost grows with vertices multiplied by frames, so a caller asks for the
/// clips it needs, either through <see cref="Providers.ModelMaterializeOptions"/> up front or
/// through <see cref="Providers.GameAssetProviderRegistry.LoadModelAnimation"/> later.
/// </para>
/// </remarks>
public sealed class GameModel
{
    private readonly IReadOnlyList<string> _animationNames = [];
    private readonly IReadOnlyList<GameModelAnimationClip> _animations = [];

    /// <summary>
    /// Gets the model name the source asset carried, or <see langword="null"/> when it had none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the triangle meshes of the model, in the order the source asset declared them. This
    /// order is the alignment contract every animation clip of the model follows.
    /// </summary>
    public required IReadOnlyList<GameModelMesh> Meshes { get; init; }

    /// <summary>
    /// Gets the materials the meshes reference through
    /// <see cref="GameModelMesh.MaterialIndex"/>. Empty when every mesh uses the default
    /// material.
    /// </summary>
    public required IReadOnlyList<GameModelMaterial> Materials { get; init; }

    /// <summary>
    /// Gets the minimum corner of the axis-aligned bounding box of the model, in model space.
    /// </summary>
    public required Vector3 BoundsMin { get; init; }

    /// <summary>
    /// Gets the maximum corner of the axis-aligned bounding box of the model, in model space.
    /// </summary>
    public required Vector3 BoundsMax { get; init; }

    /// <summary>
    /// Gets the centre of the axis-aligned bounding box.
    /// </summary>
    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;

    /// <summary>
    /// Gets the radius of the sphere that encloses the axis-aligned bounding box, which is what
    /// a camera fits a view to.
    /// </summary>
    public float BoundsRadius => (BoundsMax - BoundsMin).Length() * 0.5f;

    /// <summary>
    /// Gets the point to orbit the model around, or <see langword="null"/> to fall back to
    /// <see cref="BoundsCenter"/>.
    /// </summary>
    /// <remarks>
    /// Providers normally set this to the vertex centroid, which is weighted toward where the
    /// geometry is dense. A model with a sparse extremity (a tall antenna, say) then turns in
    /// place instead of swinging around the corner of its bounding box.
    /// </remarks>
    public Vector3? Pivot { get; init; }

    /// <summary>
    /// Gets the total number of triangles across every mesh.
    /// </summary>
    public int TriangleCount
    {
        get
        {
            int count = 0;

            foreach (var mesh in Meshes)
                count += mesh.TriangleCount;

            return count;
        }
    }

    /// <summary>
    /// Gets the total number of vertices across every mesh.
    /// </summary>
    public int VertexCount
    {
        get
        {
            int count = 0;

            foreach (var mesh in Meshes)
                count += mesh.VertexCount;

            return count;
        }
    }

    /// <summary>
    /// Gets the names of every animation the source asset offers, baked or not. Never
    /// <see langword="null"/>; empty for a model with no animations.
    /// </summary>
    /// <remarks>
    /// A name from this list is what
    /// <see cref="Providers.GameAssetProviderRegistry.LoadModelAnimation"/> and
    /// <see cref="Providers.IModelAssetSource.MaterializeModelAnimation"/> accept.
    /// </remarks>
    public IReadOnlyList<string> AnimationNames
    {
        get => _animationNames;
        init => _animationNames = value ?? [];
    }

    /// <summary>
    /// Gets the animation clips that were baked for this model. Never <see langword="null"/>;
    /// empty unless baking was asked for, because baking is opt-in.
    /// </summary>
    public IReadOnlyList<GameModelAnimationClip> Animations
    {
        get => _animations;
        init => _animations = value ?? [];
    }

    /// <summary>
    /// Finds a baked animation clip of this model by name.
    /// </summary>
    /// <param name="name">The clip name, compared with
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.</param>
    /// <param name="clip">The matching clip when the method returns <see langword="true"/>;
    /// otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <see cref="Animations"/> holds a clip with that name;
    /// otherwise <see langword="false"/>, which also happens when the name is listed in
    /// <see cref="AnimationNames"/> but was never baked.
    /// </returns>
    public bool TryGetAnimation(string name, [NotNullWhen(true)] out GameModelAnimationClip? clip)
    {
        clip = null;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var candidate in Animations)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                clip = candidate;

                return true;
            }
        }

        return false;
    }
}
