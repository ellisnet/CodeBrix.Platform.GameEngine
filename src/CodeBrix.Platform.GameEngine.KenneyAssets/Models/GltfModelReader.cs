using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CodeBrix.Graphics3D.Gltf2.Geometry;
using CodeBrix.Graphics3D.Gltf2.Geometry.VertexTypes;
using CodeBrix.Graphics3D.Gltf2.Schema2;
using CodeBrix.Graphics3D.Gltf2.Validation;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// Reads the glTF models of a Kenney pack - <c>.glb</c> and <c>.gltf</c> - into the engine's
/// format-neutral <see cref="GameModel"/> family, and bakes their animations into
/// <see cref="GameModelAnimationClip"/> frames on demand.
/// </summary>
/// <remarks>
/// <para>
/// DEPENDENCY RESOLUTION IS STRICT. Kenney's kits ship a GLB whose texture still sits beside it as a
/// separate file, and a kit can carry several same-named textures in different folders - the brick
/// kit holds three <c>Textures/colormap.png</c>, one per model format. A referenced file is
/// therefore resolved only against the model's own folder and the folders above it, never by bare
/// file name, and a reference that does not resolve fails the read with the unresolved path in the
/// message instead of guessing.
/// </para>
/// <para>
/// ANIMATED MODELS take a different route through the reader than static ones. A static model is
/// built by walking the scene's nodes and baking each node's world matrix into its vertices, which
/// keeps the source's own vertex sharing, normals and indices. An animated model is built from an
/// EVALUATED pose instead, grouped by material, so that the rest pose and every baked frame have
/// exactly the same vertex layout - the alignment guarantee
/// <see cref="GameModelAnimationClip.IsCompatibleWith"/> checks. Rigid-part animation and skinning
/// both work, because the evaluation is the glTF runtime's own.
/// </para>
/// <para>
/// Parsed documents of animated models are CACHED, keyed by the pack source and the asset's path,
/// because baking a clip after the model was read must produce frames aligned with that same model.
/// A static model's document is not cached: it offers no clips to bake later, and a Kenney kit can
/// hold hundreds of them. <see cref="Dispose"/> releases the cache.
/// </para>
/// <para>
/// The reader is safe to use from several threads; a private lock guards the cache and the load that
/// fills it.
/// </para>
/// </remarks>
internal sealed class GltfModelReader : IDisposable
{
    /// <summary>
    /// The largest number of frames a single clip is baked to, however long the animation is and
    /// however high the requested frame rate. A clip longer than this is sampled at a lower
    /// effective rate rather than allowed to consume unbounded memory.
    /// </summary>
    internal const int MaxBakedFrames = 600;

    /// <summary>
    /// The smallest number of frames a clip is baked to, so that even an instantaneous animation
    /// produces something a consumer can play.
    /// </summary>
    internal const int MinBakedFrames = 2;

    private readonly Dictionary<string, CachedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private bool _disposed;

    /// <summary>
    /// Gets the number of parsed glTF documents the reader is holding on to.
    /// </summary>
    internal int CachedDocumentCount
    {
        get
        {
            lock (_gate) { return _documents.Count; }
        }
    }

    /// <summary>
    /// Reads a glTF asset into an engine model, baking the animation clips the options asked for.
    /// </summary>
    /// <param name="entry">The catalogued asset to read; its kind must be
    /// <see cref="GameAssetKind.Model3D"/> in glTF form.</param>
    /// <param name="options">
    /// Which animations to bake and at what rate, or <see langword="null"/> to bake none.
    /// </param>
    /// <returns>
    /// The model. <see cref="GameModel.AnimationNames"/> always lists every animation the asset
    /// offers, whether or not it was baked; <see cref="GameModel.Animations"/> holds the clips that
    /// were.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an animation the options named is not one the
    /// asset offers; the message lists the names it does offer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <see cref="ModelMaterializeOptions.AnimationFramesPerSecond"/> is less than one.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a glTF model -
    /// the <c>.fbx</c>, <c>.obj</c>, <c>.mtl</c>, <c>.dae</c> and <c>.stl</c> copies Kenney ships
    /// beside it are catalogued for discovery only.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model references a buffer or texture
    /// that does not resolve inside the pack; the message names the unresolved path.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset is not a loadable glTF document,
    /// or carries no triangle geometry.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the reader has been disposed.</exception>
    internal GameModel Read(KenneyAssetEntry entry, ModelMaterializeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        int framesPerSecond = options?.AnimationFramesPerSecond ?? 24;

        if (options is not null && framesPerSecond < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                framesPerSecond,
                "The animation frame rate must be at least one frame per second.");
        }

        CachedDocument document = LoadDocument(entry);
        IReadOnlyList<string> wanted = ResolveWantedAnimations(document, entry, options);

        if (wanted.Count == 0) { return document.RestPose; }

        List<GameModelAnimationClip> clips = new(wanted.Count);
        foreach (string name in wanted)
        {
            clips.Add(BakeClipCore(document, entry, name, framesPerSecond));
        }

        return WithAnimations(document.RestPose, clips);
    }

    /// <summary>
    /// Bakes one animation of a glTF asset into vertex frames, without re-reading the model.
    /// </summary>
    /// <param name="entry">The catalogued asset the animation belongs to.</param>
    /// <param name="animationName">The animation's name, matched case-insensitively against the
    /// names the asset offers.</param>
    /// <param name="framesPerSecond">The rate to sample the animation at.</param>
    /// <returns>
    /// The baked clip. It is aligned with the model <see cref="Read"/> returns for the same asset,
    /// so <see cref="GameModelAnimationClip.IsCompatibleWith"/> holds for that model.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="animationName"/> is null,
    /// empty or whitespace, or names an animation the asset does not offer; the message lists the
    /// names it does offer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <paramref name="framesPerSecond"/> is less than one.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a glTF model.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the model references a buffer or texture
    /// that does not resolve inside the pack; the message names the unresolved path.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset is not a loadable glTF document,
    /// or an animated pose does not match the rest pose's vertex layout.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the reader has been disposed.</exception>
    internal GameModelAnimationClip BakeClip(
        KenneyAssetEntry entry, string animationName, int framesPerSecond = 24)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, 1);

        CachedDocument document = LoadDocument(entry);

        return BakeClipCore(document, entry, animationName, framesPerSecond);
    }

    /// <summary>
    /// Releases every parsed glTF document the reader cached. The models and clips it already
    /// handed out are plain data and stay usable.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _documents.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// Checks that an asset is a glTF model this reader can handle.
    /// </summary>
    /// <param name="entry">The catalogued asset to check.</param>
    /// <param name="key">The registry key to name in the exception, or <see langword="null"/> to
    /// name the asset's own key path.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a glTF model.</exception>
    internal static void EnsureGltfModel(KenneyAssetEntry entry, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        bool isGltf = entry.Kind == GameAssetKind.Model3D
            && entry.IsMaterializable
            && (entry.Extension.Equals("glb", StringComparison.OrdinalIgnoreCase)
                || entry.Extension.Equals("gltf", StringComparison.OrdinalIgnoreCase));

        if (!isGltf)
        {
            throw new UnsupportedGameAssetException(
                entry.Kind == GameAssetKind.Model3D ? GameAssetKind.Model3D : entry.Kind,
                key ?? entry.KeyPath);
        }
    }

    private static GameModel WithAnimations(GameModel model, IReadOnlyList<GameModelAnimationClip> clips) =>
        new()
        {
            Name = model.Name,
            Meshes = model.Meshes,
            Materials = model.Materials,
            BoundsMin = model.BoundsMin,
            BoundsMax = model.BoundsMax,
            Pivot = model.Pivot,
            AnimationNames = model.AnimationNames,
            Animations = clips,
        };

    private static IReadOnlyList<string> ResolveWantedAnimations(
        CachedDocument document, KenneyAssetEntry entry, ModelMaterializeOptions? options)
    {
        if (options is null) { return []; }

        if (options.BakeAllAnimations) { return document.AnimationNames; }

        IReadOnlyList<string>? requested = options.AnimationNames;
        if (requested is null || requested.Count == 0) { return []; }

        List<string> resolved = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in requested)
        {
            string match = MatchAnimationName(document, entry, name);
            if (seen.Add(match)) { resolved.Add(match); }
        }

        return resolved;
    }

    private static string MatchAnimationName(
        CachedDocument document, KenneyAssetEntry entry, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            foreach (string available in document.AnimationNames)
            {
                if (available.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    //The asset's own spelling wins, so a region name or a clip name is stable
                    //  however the caller cased its request
                    return available;
                }
            }
        }

        string names = document.AnimationNames.Count == 0
            ? "the model has no animations"
            : $"available animations: {string.Join(", ", document.AnimationNames)}";

        throw new ArgumentException(
            $"The model '{entry.KeyPath}' has no animation named '{requested}' ({names}).",
            nameof(requested));
    }

    private CachedDocument LoadDocument(KenneyAssetEntry entry)
    {
        EnsureGltfModel(entry);

        string cacheKey = $"{entry.Pack.SourcePath}|{entry.Path}";

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_documents.TryGetValue(cacheKey, out CachedDocument? cached)) { return cached; }

            CachedDocument document = BuildDocument(entry);

            //Only an animated model's document earns a place in the cache: it is what a later
            //  BakeClip call needs in order to produce frames aligned with the rest pose. A static
            //  model has nothing to bake, and a kit such as the brick kit holds hundreds of them.
            if (document.AnimationNames.Count > 0) { _documents[cacheKey] = document; }

            return document;
        }
    }

    private static CachedDocument BuildDocument(KenneyAssetEntry entry)
    {
        ModelRoot root = ReadRoot(entry);

        if (root.LogicalAnimations.Count == 0)
        {
            return new CachedDocument(root, ConvertStatic(root), [], null);
        }

        List<string> names = new(root.LogicalAnimations.Count);
        for (int i = 0; i < root.LogicalAnimations.Count; i++)
        {
            string? name = root.LogicalAnimations[i].Name;
            names.Add(string.IsNullOrWhiteSpace(name) ? $"animation {i + 1}" : name);
        }

        (GameModel restPose, List<Material?> materialOrder) = ConvertEvaluated(root, animation: null, time: 0f);

        return new CachedDocument(root, WithAnimationNames(restPose, names), names, materialOrder);
    }

    private static GameModel WithAnimationNames(GameModel model, IReadOnlyList<string> names) =>
        new()
        {
            Name = model.Name,
            Meshes = model.Meshes,
            Materials = model.Materials,
            BoundsMin = model.BoundsMin,
            BoundsMax = model.BoundsMax,
            Pivot = model.Pivot,
            AnimationNames = names,
        };

    private static ModelRoot ReadRoot(KenneyAssetEntry entry)
    {
        IKenneyArchive archive = entry.Pack.Archive;
        string? unresolved = null;

        ReadContext context = ReadContext.Create(assetName =>
        {
            string requested = Uri.UnescapeDataString(assetName ?? string.Empty);
            string? resolved = archive.ResolveDependencyPath(entry.Path, requested, strict: true);

            if (resolved is null)
            {
                unresolved = requested;

                throw new FileNotFoundException(
                    DependencyMessage(entry, requested), requested);
            }

            return new ArraySegment<byte>(archive.ReadBytes(resolved));
        });

        //Kenney's models come from exporters that write the occasional schema slip; TryFix repairs
        //  what it can while loading rather than refusing a model a game can use.
        context.Validation = ValidationMode.TryFix;

        try
        {
            using Stream stream = entry.Open();

            return context.ReadSchema2(stream);
        }
        catch (Exception exception)
        {
            if (unresolved is not null)
            {
                throw new FileNotFoundException(
                    DependencyMessage(entry, unresolved), unresolved, exception);
            }

            if (exception is InvalidDataException or FileNotFoundException) { throw; }

            throw new InvalidDataException(
                $"'{entry.Path}' in '{entry.Pack.SourcePath}' is not a loadable glTF model.",
                exception);
        }
    }

    private static string DependencyMessage(KenneyAssetEntry entry, string requested) =>
        $"The model '{entry.Path}' references '{requested}', which was not found in "
        + $"'{entry.Pack.SourcePath}' relative to the model's own folder or any folder above it.";

    private static Scene RequireScene(ModelRoot root)
    {
        Scene? scene = root.DefaultScene
            ?? (root.LogicalScenes.Count > 0 ? root.LogicalScenes[0] : null);

        return scene ?? throw new InvalidDataException("The glTF model has no scene.");
    }

    private static GameModel ConvertStatic(ModelRoot root)
    {
        Scene scene = RequireScene(root);

        List<GameModelMaterial> materials = new(root.LogicalMaterials.Count);
        foreach (Material material in root.LogicalMaterials)
        {
            materials.Add(ConvertMaterial(material));
        }

        List<GameModelMesh> meshes = [];
        BoundsAccumulator bounds = new();

        //glTF is a tree of nodes, each with a local transform, so a mesh's final position is its
        //  node's accumulated world matrix. Flattening the tree and baking each node's world matrix
        //  into its vertices is what lets a consumer draw the arrays as they are.
        foreach (Node node in Walk(scene.VisualChildren))
        {
            if (node.Mesh is null) { continue; }

            Matrix4x4 worldMatrix = node.WorldMatrix;

            foreach (MeshPrimitive primitive in node.Mesh.Primitives)
            {
                GameModelMesh? converted = ConvertPrimitive(primitive, worldMatrix);
                if (converted is null) { continue; }

                meshes.Add(converted);
                bounds.AddPositions(converted.Positions);
            }
        }

        if (meshes.Count == 0)
        {
            throw new InvalidDataException("The glTF model contains no triangle geometry.");
        }

        return new GameModel
        {
            Name = scene.Name,
            Meshes = meshes,
            Materials = materials,
            BoundsMin = bounds.Min,
            BoundsMax = bounds.Max,
            Pivot = bounds.Centroid,
        };
    }

    private static IEnumerable<Node> Walk(IEnumerable<Node> nodes)
    {
        foreach (Node node in nodes)
        {
            yield return node;

            foreach (Node child in Walk(node.VisualChildren))
            {
                yield return child;
            }
        }
    }

    private static GameModelMesh? ConvertPrimitive(MeshPrimitive primitive, Matrix4x4 worldMatrix)
    {
        Accessor? positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null) { return null; }

        List<(int A, int B, int C)> triangles = [];
        foreach ((int a, int b, int c) in primitive.GetTriangleIndices())
        {
            triangles.Add((a, b, c));
        }

        if (triangles.Count == 0) { return null; }

        IList<Vector3> sourcePositions = positionAccessor.AsVector3Array();
        IList<Vector3>? sourceNormals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        IList<Vector2>? sourceTexCoords = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        int vertexCount = sourcePositions.Count;

        float[] positions = new float[vertexCount * 3];
        float[] texCoords = new float[vertexCount * 2];

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 world = Vector3.Transform(sourcePositions[i], worldMatrix);
            positions[i * 3] = world.X;
            positions[(i * 3) + 1] = world.Y;
            positions[(i * 3) + 2] = world.Z;

            if (sourceTexCoords is not null)
            {
                texCoords[i * 2] = sourceTexCoords[i].X;
                texCoords[(i * 2) + 1] = sourceTexCoords[i].Y;
            }
        }

        uint[] indices = new uint[triangles.Count * 3];
        for (int i = 0; i < triangles.Count; i++)
        {
            indices[i * 3] = (uint)triangles[i].A;
            indices[(i * 3) + 1] = (uint)triangles[i].B;
            indices[(i * 3) + 2] = (uint)triangles[i].C;
        }

        float[] normals;
        if (sourceNormals is not null)
        {
            //TransformNormal is exact for the rigid transforms and uniform scales real exports use
            normals = new float[vertexCount * 3];
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 normal = Vector3.TransformNormal(sourceNormals[i], worldMatrix);
                normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitY;
                normals[i * 3] = normal.X;
                normals[(i * 3) + 1] = normal.Y;
                normals[(i * 3) + 2] = normal.Z;
            }
        }
        else
        {
            normals = GenerateSmoothNormals(positions, indices);
        }

        return new GameModelMesh
        {
            Positions = positions,
            Normals = normals,
            TexCoords = texCoords,
            Indices = indices,
            MaterialIndex = primitive.Material?.LogicalIndex ?? -1,
        };
    }

    /// <summary>
    /// Generates per-vertex normals for geometry that carries none, by accumulating face normals
    /// weighted by face area.
    /// </summary>
    /// <param name="positions">The vertex positions, three floats per vertex.</param>
    /// <param name="indices">The triangle indices, three per triangle.</param>
    /// <returns>The generated normals, three unit-length floats per vertex.</returns>
    internal static float[] GenerateSmoothNormals(float[] positions, uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);

        float[] normals = new float[positions.Length];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int ia = (int)indices[i] * 3;
            int ib = (int)indices[i + 1] * 3;
            int ic = (int)indices[i + 2] * 3;

            if (ia + 2 >= positions.Length || ib + 2 >= positions.Length || ic + 2 >= positions.Length)
            {
                continue;
            }

            Vector3 a = new(positions[ia], positions[ia + 1], positions[ia + 2]);
            Vector3 b = new(positions[ib], positions[ib + 1], positions[ib + 2]);
            Vector3 c = new(positions[ic], positions[ic + 1], positions[ic + 2]);

            //The cross product's length is proportional to the face area, which is the weighting
            Vector3 faceNormal = Vector3.Cross(b - a, c - a);

            foreach (int offset in (ReadOnlySpan<int>)[ia, ib, ic])
            {
                normals[offset] += faceNormal.X;
                normals[offset + 1] += faceNormal.Y;
                normals[offset + 2] += faceNormal.Z;
            }
        }

        for (int i = 0; i + 2 < normals.Length; i += 3)
        {
            Vector3 normal = new(normals[i], normals[i + 1], normals[i + 2]);
            normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitY;
            normals[i] = normal.X;
            normals[i + 1] = normal.Y;
            normals[i + 2] = normal.Z;
        }

        return normals;
    }

    private static (GameModel Model, List<Material?> MaterialOrder) ConvertEvaluated(
        ModelRoot root, Animation? animation, float time)
    {
        List<EvaluatedGroup> groups = EvaluateGroups(root, animation, time);

        if (groups.Count == 0)
        {
            throw new InvalidDataException("The glTF model contains no triangle geometry.");
        }

        List<GameModelMaterial> materials = [];
        List<Material?> materialOrder = new(groups.Count);
        List<GameModelMesh> meshes = new(groups.Count);
        BoundsAccumulator bounds = new();

        foreach (EvaluatedGroup group in groups)
        {
            int materialIndex = -1;
            if (group.Material is not null)
            {
                materialIndex = materials.Count;
                materials.Add(ConvertMaterial(group.Material));
            }

            materialOrder.Add(group.Material);

            float[] positions = [.. group.Positions];
            uint[] indices = new uint[positions.Length / 3];
            for (int i = 0; i < indices.Length; i++) { indices[i] = (uint)i; }

            meshes.Add(new GameModelMesh
            {
                Positions = positions,
                Normals = [.. group.Normals],
                TexCoords = [.. group.TexCoords],
                Indices = indices,
                MaterialIndex = materialIndex,
            });

            bounds.AddPositions(positions);
        }

        GameModel model = new()
        {
            Name = root.DefaultScene?.Name,
            Meshes = meshes,
            Materials = materials,
            BoundsMin = bounds.Min,
            BoundsMax = bounds.Max,
            Pivot = bounds.Centroid,
        };

        return (model, materialOrder);
    }

    private GameModelAnimationClip BakeClipCore(
        CachedDocument document, KenneyAssetEntry entry, string animationName, int framesPerSecond)
    {
        string name = MatchAnimationName(document, entry, animationName);
        int index = 0;
        for (int i = 0; i < document.AnimationNames.Count; i++)
        {
            if (document.AnimationNames[i].Equals(name, StringComparison.Ordinal)) { index = i; break; }
        }

        Animation animation = document.Root.LogicalAnimations[index];
        float duration = MathF.Max(animation.Duration, 1f / framesPerSecond);
        int frameCount = Math.Clamp(
            (int)MathF.Ceiling(duration * framesPerSecond), MinBakedFrames, MaxBakedFrames);

        List<GameModelAnimationFrame> frames = new(frameCount);
        for (int frame = 0; frame < frameCount; frame++)
        {
            //Sampled over [0, duration) so a looping clip does not repeat its end pose
            float time = duration * frame / frameCount;
            frames.Add(BakeFrame(document, name, animation, time));
        }

        return new GameModelAnimationClip
        {
            Name = name,
            Duration = duration,
            FrameRate = framesPerSecond,
            Frames = frames,
        };
    }

    private static GameModelAnimationFrame BakeFrame(
        CachedDocument document, string animationName, Animation animation, float time)
    {
        List<Material?> materialOrder = document.MaterialOrder
            ?? throw new InvalidDataException("The glTF model carries no animated geometry.");

        List<EvaluatedGroup> groups = EvaluateGroups(document.Root, animation, time);
        List<GameModelFrameMesh> meshes = new(materialOrder.Count);

        for (int i = 0; i < materialOrder.Count; i++)
        {
            Material? material = materialOrder[i];
            EvaluatedGroup? group = null;

            foreach (EvaluatedGroup candidate in groups)
            {
                if (ReferenceEquals(candidate.Material, material)) { group = candidate; break; }
            }

            if (group is null || group.Positions.Count != document.RestPose.Meshes[i].Positions.Length)
            {
                throw new InvalidDataException(
                    $"Animation '{animationName}' evaluates to a different geometry layout than the "
                    + "rest pose.");
            }

            meshes.Add(new GameModelFrameMesh
            {
                Positions = [.. group.Positions],
                Normals = [.. group.Normals],
            });
        }

        return new GameModelAnimationFrame { Meshes = meshes };
    }

    /// <summary>
    /// Evaluates a scene, optionally under an animation at a point in time, into one flat
    /// triangle-soup vertex bucket per material.
    /// </summary>
    /// <param name="root">The parsed glTF document.</param>
    /// <param name="animation">The animation to evaluate under, or <see langword="null"/> for the
    /// rest pose.</param>
    /// <param name="time">The time in seconds to evaluate the animation at.</param>
    /// <returns>The buckets, in a deterministic order for a given document.</returns>
    /// <remarks>
    /// The bucket order follows the first appearance of each material in the evaluated triangle
    /// stream, which is stable for a given document. That is what keeps frames baked at different
    /// times aligned with the rest pose.
    /// </remarks>
    private static List<EvaluatedGroup> EvaluateGroups(ModelRoot root, Animation? animation, float time)
    {
        Scene scene = RequireScene(root);

        List<EvaluatedGroup> groups = [];
        Dictionary<Material, int> indexByMaterial = [];
        int nullMaterialIndex = -1;

        foreach ((IVertexBuilder a, IVertexBuilder b, IVertexBuilder c, Material material) in
            Toolkit.EvaluateTriangles(scene, null, animation, time))
        {
            int groupIndex;

            if (material is null)
            {
                if (nullMaterialIndex < 0)
                {
                    nullMaterialIndex = groups.Count;
                    groups.Add(new EvaluatedGroup(null));
                }

                groupIndex = nullMaterialIndex;
            }
            else if (!indexByMaterial.TryGetValue(material, out groupIndex))
            {
                groupIndex = groups.Count;
                indexByMaterial[material] = groupIndex;
                groups.Add(new EvaluatedGroup(material));
            }

            EvaluatedGroup group = groups[groupIndex];
            AppendVertex(group, a);
            AppendVertex(group, b);
            AppendVertex(group, c);
        }

        return groups;
    }

    private static void AppendVertex(EvaluatedGroup group, IVertexBuilder vertex)
    {
        IVertexGeometry geometry = vertex.GetGeometry();
        Vector3 position = geometry.GetPosition();
        group.Positions.Add(position.X);
        group.Positions.Add(position.Y);
        group.Positions.Add(position.Z);

        Vector3 normal = geometry.TryGetNormal(out Vector3 sourceNormal) ? sourceNormal : Vector3.UnitY;
        normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitY;
        group.Normals.Add(normal.X);
        group.Normals.Add(normal.Y);
        group.Normals.Add(normal.Z);

        IVertexMaterial material = vertex.GetMaterial();
        Vector2 uv = material.MaxTextCoords > 0 ? material.GetTexCoord(0) : Vector2.Zero;
        group.TexCoords.Add(uv.X);
        group.TexCoords.Add(uv.Y);
    }

    private static GameModelMaterial ConvertMaterial(Material material)
    {
        MaterialChannel? baseColor = material.FindChannel("BaseColor");

        byte[]? textureRgba = null;
        int textureWidth = 0;
        int textureHeight = 0;

        byte[]? imageBytes = ReadImageBytes(baseColor);
        if (imageBytes is not null
            && GltfTextureDecoder.TryDecode(imageBytes, out byte[] rgba, out int width, out int height))
        {
            textureRgba = rgba;
            textureWidth = width;
            textureHeight = height;
        }

        GameModelAlphaMode alphaMode = material.Alpha switch
        {
            AlphaMode.MASK => GameModelAlphaMode.Mask,
            AlphaMode.BLEND => GameModelAlphaMode.Blend,
            _ => GameModelAlphaMode.Opaque,
        };

        //KHR_materials_transmission glass - a lens or a viewfinder - is alphaMode OPAQUE yet
        //  see-through. Nothing here implements real transmission, so a transmissive material is
        //  treated as translucent, which reads far better than an opaque solid. The channel exists
        //  only when an exporter wrote the extension, so its presence is a reliable glass signal.
        if (alphaMode == GameModelAlphaMode.Opaque && material.FindChannel("Transmission") is not null)
        {
            alphaMode = GameModelAlphaMode.Blend;
        }

        MaterialChannel? metallicRoughness = material.FindChannel("MetallicRoughness");

        return new GameModelMaterial
        {
            Name = material.Name,
            AlphaMode = alphaMode,
            BaseColorFactor = baseColor?.Color ?? Vector4.One,
            AlphaCutoff = material.AlphaCutoff,
            BaseColorTextureRgba = textureRgba,
            BaseColorTextureWidth = textureWidth,
            BaseColorTextureHeight = textureHeight,
            MetallicFactor = metallicRoughness?.GetFactor("MetallicFactor") ?? 1.0f,
            RoughnessFactor = metallicRoughness?.GetFactor("RoughnessFactor") ?? 1.0f,
            DoubleSided = material.DoubleSided,
        };
    }

    private static byte[]? ReadImageBytes(MaterialChannel? channel)
    {
        Image? image = channel?.Texture?.PrimaryImage;
        if (image is null) { return null; }

        ReadOnlyMemory<byte> content = image.Content.Content;

        return content.Length > 0 ? content.ToArray() : null;
    }

    //Accumulates an axis-aligned bounding box and a vertex centroid while geometry is converted
    private struct BoundsAccumulator
    {
        private Vector3 _min = new(float.PositiveInfinity);
        private Vector3 _max = new(float.NegativeInfinity);
        private Vector3 _sum = Vector3.Zero;
        private long _count;

        public BoundsAccumulator() { }

        public Vector3 Min => _count > 0 ? _min : Vector3.Zero;

        public Vector3 Max => _count > 0 ? _max : Vector3.Zero;

        public Vector3? Centroid => _count > 0 ? _sum / _count : null;

        public void AddPositions(float[] positions)
        {
            for (int i = 0; i + 2 < positions.Length; i += 3)
            {
                Vector3 position = new(positions[i], positions[i + 1], positions[i + 2]);
                _min = Vector3.Min(_min, position);
                _max = Vector3.Max(_max, position);
                _sum += position;
                _count++;
            }
        }
    }

    //One material's worth of an evaluated scene: flat triangle-soup vertex lists
    private sealed class EvaluatedGroup(Material? material)
    {
        public Material? Material { get; } = material;

        public List<float> Positions { get; } = [];

        public List<float> Normals { get; } = [];

        public List<float> TexCoords { get; } = [];
    }

    //A parsed document with everything a later bake needs to stay aligned with the rest pose
    private sealed class CachedDocument(
        ModelRoot root,
        GameModel restPose,
        IReadOnlyList<string> animationNames,
        List<Material?>? materialOrder)
    {
        public ModelRoot Root { get; } = root;

        public GameModel RestPose { get; } = restPose;

        public IReadOnlyList<string> AnimationNames { get; } = animationNames;

        //Null for a static model, which is never evaluated and never baked
        public List<Material?>? MaterialOrder { get; } = materialOrder;
    }
}
