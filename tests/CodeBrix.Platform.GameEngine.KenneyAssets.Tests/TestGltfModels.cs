using System;
using System.Collections.Generic;
using System.Numerics;
using CodeBrix.Graphics3D.Gltf2.Geometry;
using CodeBrix.Graphics3D.Gltf2.Geometry.VertexTypes;
using CodeBrix.Graphics3D.Gltf2.Materials;
using CodeBrix.Graphics3D.Gltf2.Scenes;
using CodeBrix.Graphics3D.Gltf2.Schema2;
using CodeBrix.Platform.GameEngine.Assets.Models;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Synthetic models the model tests need and no Kenney bundle provides: hand-built
/// <see cref="GameModel"/> shapes whose silhouette, colour and depth ordering are known in advance,
/// a skinned glTF binary built with the toolkit, and the pixel measurements the renderer tests
/// assert on.
/// </summary>
internal static class TestGltfModels
{
    /// <summary>
    /// A quad in the XZ-facing plane, two units across, centred on the origin, facing +Z - the
    /// direction the camera looks from at yaw 0.
    /// </summary>
    /// <param name="color">The material's base colour factor.</param>
    /// <param name="alphaMode">The material's alpha mode.</param>
    /// <param name="alphaCutoff">The alpha cutoff a masked material tests against.</param>
    /// <returns>The model.</returns>
    public static GameModel Quad(
        Vector4? color = null,
        GameModelAlphaMode alphaMode = GameModelAlphaMode.Opaque,
        float alphaCutoff = 0.5f)
    {
        //Counter-clockwise seen from +Z, which is a front face
        float[] positions =
        [
            -1f, -1f, 0f,
             1f, -1f, 0f,
             1f,  1f, 0f,
            -1f,  1f, 0f,
        ];
        float[] normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];
        float[] texCoords = [0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];

        return new GameModel
        {
            Name = "quad",
            Meshes =
            [
                new GameModelMesh
                {
                    Positions = positions,
                    Normals = normals,
                    TexCoords = texCoords,
                    Indices = indices,
                    MaterialIndex = 0,
                },
            ],
            Materials =
            [
                new GameModelMaterial
                {
                    Name = "flat",
                    BaseColorFactor = color ?? new Vector4(1f, 0f, 0f, 1f),
                    AlphaMode = alphaMode,
                    AlphaCutoff = alphaCutoff,
                },
            ],
            BoundsMin = new Vector3(-1f, -1f, 0f),
            BoundsMax = new Vector3(1f, 1f, 0f),
            Pivot = Vector3.Zero,
        };
    }

    /// <summary>
    /// A wedge that is mirror-symmetric about the XY plane (z becomes -z) and deliberately
    /// asymmetric along X, so that the view from one direction and the view from the opposite
    /// direction are horizontal mirrors of each other rather than trivially identical.
    /// </summary>
    /// <returns>The model.</returns>
    public static GameModel SymmetricWedge()
    {
        List<float> positions = [];
        List<float> normals = [];
        List<uint> indices = [];

        //A tall slab on the -X side and a short one on the +X side, each mirrored in z
        AddBox(positions, normals, indices, new Vector3(-0.9f, 0f, -0.4f), new Vector3(-0.1f, 2f, 0.4f));
        AddBox(positions, normals, indices, new Vector3(0.1f, 0f, -0.4f), new Vector3(0.9f, 0.7f, 0.4f));

        float[] positionArray = [.. positions];

        return new GameModel
        {
            Name = "wedge",
            Meshes =
            [
                new GameModelMesh
                {
                    Positions = positionArray,
                    Normals = [.. normals],
                    TexCoords = new float[positionArray.Length / 3 * 2],
                    Indices = [.. indices],
                    MaterialIndex = 0,
                },
            ],
            Materials =
            [
                new GameModelMaterial { Name = "wedge", BaseColorFactor = new Vector4(0.2f, 0.6f, 1f, 1f) },
            ],
            BoundsMin = new Vector3(-0.9f, 0f, -0.4f),
            BoundsMax = new Vector3(0.9f, 2f, 0.4f),
            Pivot = new Vector3(0f, 1f, 0f),
        };
    }

    /// <summary>
    /// Two overlapping quads facing the camera at yaw 0, the second nearer than the first, so that
    /// the depth buffer decides which colour the overlap shows.
    /// </summary>
    /// <param name="farColor">The colour of the further quad.</param>
    /// <param name="nearColor">The colour of the nearer quad.</param>
    /// <returns>The model.</returns>
    public static GameModel OverlappingQuads(Vector4 farColor, Vector4 nearColor)
    {
        return new GameModel
        {
            Name = "overlap",
            Meshes =
            [
                FacingQuad(-1.2f, -0.6f, 1.2f, 0.6f, z: -0.5f, materialIndex: 0),
                FacingQuad(-0.6f, -1.2f, 0.6f, 1.2f, z: 0.5f, materialIndex: 1),
            ],
            Materials =
            [
                new GameModelMaterial { Name = "far", BaseColorFactor = farColor },
                new GameModelMaterial { Name = "near", BaseColorFactor = nearColor },
            ],
            BoundsMin = new Vector3(-1.2f, -1.2f, -0.5f),
            BoundsMax = new Vector3(1.2f, 1.2f, 0.5f),
            Pivot = Vector3.Zero,
        };
    }

    /// <summary>
    /// Builds a clip whose frames scale a model's rest pose up, so that later frames need more room
    /// in a cell than the rest pose does.
    /// </summary>
    /// <param name="model">The model to build frames for.</param>
    /// <param name="frameCount">The number of frames.</param>
    /// <param name="maximumScale">The factor the last frame is scaled by.</param>
    /// <param name="name">The clip's name.</param>
    /// <returns>The clip, aligned with the model.</returns>
    public static GameModelAnimationClip GrowingClip(
        GameModel model, int frameCount = 4, float maximumScale = 2f, string name = "grow")
    {
        List<GameModelAnimationFrame> frames = new(frameCount);

        for (int f = 0; f < frameCount; f++)
        {
            float scale = frameCount == 1
                ? maximumScale
                : 1f + ((maximumScale - 1f) * f / (frameCount - 1));

            List<GameModelFrameMesh> meshes = new(model.Meshes.Count);

            foreach (GameModelMesh mesh in model.Meshes)
            {
                float[] positions = new float[mesh.Positions.Length];
                for (int i = 0; i < positions.Length; i++) { positions[i] = mesh.Positions[i] * scale; }

                meshes.Add(new GameModelFrameMesh { Positions = positions, Normals = mesh.Normals });
            }

            frames.Add(new GameModelAnimationFrame { Meshes = meshes });
        }

        return new GameModelAnimationClip
        {
            Name = name,
            Duration = frameCount / 12f,
            FrameRate = 12,
            Frames = frames,
        };
    }

    /// <summary>
    /// Builds a glTF binary holding a skinned, animated mesh, which no fixture bundle provides: the
    /// animated Kenney models are all rigid-part animation.
    /// </summary>
    /// <param name="animationName">The name to give the animation.</param>
    /// <returns>The <c>.glb</c> bytes.</returns>
    public static byte[] BuildSkinnedGlb(string animationName = "bend")
    {
        NodeBuilder root = new("root");
        NodeBuilder joint0 = root.CreateNode("joint0");
        NodeBuilder joint1 = joint0.CreateNode("joint1").WithLocalTranslation(new Vector3(0f, 2f, 0f));

        joint1.UseRotation(animationName)
            .WithPoint(0f, Quaternion.Identity)
            .WithPoint(1f, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.8f));

        MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4> mesh = new("arm");
        mesh.UsePrimitive(MaterialBuilder.CreateDefault()).AddQuadrangle(
            SkinnedVertex(-0.5f, 0f, 0f, 0),
            SkinnedVertex(0.5f, 0f, 0f, 0),
            SkinnedVertex(0.5f, 4f, 0f, 1),
            SkinnedVertex(-0.5f, 4f, 0f, 1));

        SceneBuilder scene = new();
        scene.AddSkinnedMesh(mesh, Matrix4x4.Identity, joint0, joint1);

        return scene.ToGltf2().WriteGLB().ToArray();
    }

    /// <summary>
    /// Builds a glTF document in its JSON form, with its geometry in a satellite buffer beside it,
    /// which is the shape of a <c>.gltf</c> model as opposed to a self-contained <c>.glb</c>.
    /// </summary>
    /// <returns>Each written file's name mapped to its bytes: the document and its buffer.</returns>
    public static IReadOnlyDictionary<string, byte[]> BuildSatelliteGltf()
    {
        MeshBuilder<VertexPosition> mesh = new("triangle");
        mesh.UsePrimitive(MaterialBuilder.CreateDefault()).AddTriangle(
            new VertexPosition(-1f, 0f, 0f),
            new VertexPosition(1f, 0f, 0f),
            new VertexPosition(0f, 2f, 0f));

        SceneBuilder scene = new();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);

        Dictionary<string, ArraySegment<byte>> written = [];
        scene.ToGltf2().Save("triangle.gltf", WriteContext.CreateFromDictionary(written));

        Dictionary<string, byte[]> files = [];
        foreach ((string name, ArraySegment<byte> bytes) in written) { files[name] = bytes.ToArray(); }

        return files;
    }

    /// <summary>
    /// Measures a rendered frame the way the renderer tests assert on it: coverage, the bounding box
    /// of what was drawn, and the average colour of the pixels that were.
    /// </summary>
    /// <param name="bitmap">The rendered bitmap.</param>
    /// <param name="alphaThreshold">The alpha at or above which a pixel counts as drawn.</param>
    /// <returns>The measurements.</returns>
    public static RenderedFrameStats Measure(SKBitmap bitmap, byte alphaThreshold = 16)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int covered = 0;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        long red = 0;
        long green = 0;
        long blue = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                if (color.Alpha < alphaThreshold) { continue; }

                covered++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                red += color.Red;
                green += color.Green;
                blue += color.Blue;
            }
        }

        return covered == 0
            ? new RenderedFrameStats(0, 0, 0, 0, 0, 0, 0, 0, bitmap.Width * bitmap.Height)
            : new RenderedFrameStats(
                covered,
                minX,
                minY,
                maxX,
                maxY,
                (int)(red / covered),
                (int)(green / covered),
                (int)(blue / covered),
                bitmap.Width * bitmap.Height);
    }

    /// <summary>
    /// Compares a bitmap's coverage with another bitmap's coverage mirrored left to right, which is
    /// how two opposite camera directions of a symmetric model relate.
    /// </summary>
    /// <param name="left">The first bitmap.</param>
    /// <param name="right">The bitmap to mirror before comparing.</param>
    /// <param name="alphaThreshold">The alpha at or above which a pixel counts as drawn.</param>
    /// <returns>The fraction of pixels whose drawn-or-not state agrees, from <c>0</c> to <c>1</c>.</returns>
    public static double MirroredCoverageAgreement(
        SKBitmap left, SKBitmap right, byte alphaThreshold = 16)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int agreed = 0;
        int total = left.Width * left.Height;

        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                bool drawnLeft = left.GetPixel(x, y).Alpha >= alphaThreshold;
                bool drawnRight = right.GetPixel(right.Width - 1 - x, y).Alpha >= alphaThreshold;

                if (drawnLeft == drawnRight) { agreed++; }
            }
        }

        return total == 0 ? 1d : (double)agreed / total;
    }

    private static VertexBuilder<VertexPosition, VertexEmpty, VertexJoints4> SkinnedVertex(
        float x, float y, float z, int joint) =>
        new(new VertexPosition(x, y, z), new VertexJoints4(joint));

    private static GameModelMesh FacingQuad(
        float left, float bottom, float right, float top, float z, int materialIndex) =>
        new()
        {
            Positions =
            [
                left, bottom, z,
                right, bottom, z,
                right, top, z,
                left, top, z,
            ],
            Normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f],
            TexCoords = new float[8],
            Indices = [0u, 1u, 2u, 0u, 2u, 3u],
            MaterialIndex = materialIndex,
        };

    private static void AddBox(
        List<float> positions, List<float> normals, List<uint> indices, Vector3 min, Vector3 max)
    {
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z),
        ];

        //Each face is wound counter-clockwise seen from outside, so every face is a front face
        int[][] faces =
        [
            [4, 5, 6, 7],   //+Z
            [1, 0, 3, 2],   //-Z
            [5, 1, 2, 6],   //+X
            [0, 4, 7, 3],   //-X
            [3, 7, 6, 2],   //+Y
            [0, 1, 5, 4],   //-Y
        ];

        Vector3[] faceNormals =
        [
            Vector3.UnitZ,
            -Vector3.UnitZ,
            Vector3.UnitX,
            -Vector3.UnitX,
            Vector3.UnitY,
            -Vector3.UnitY,
        ];

        for (int f = 0; f < faces.Length; f++)
        {
            uint first = (uint)(positions.Count / 3);

            foreach (int corner in faces[f])
            {
                positions.Add(corners[corner].X);
                positions.Add(corners[corner].Y);
                positions.Add(corners[corner].Z);
                normals.Add(faceNormals[f].X);
                normals.Add(faceNormals[f].Y);
                normals.Add(faceNormals[f].Z);
            }

            indices.AddRange([first, first + 1u, first + 2u, first, first + 2u, first + 3u]);
        }
    }
}

/// <summary>
/// What a rendered frame looks like in the terms the renderer tests assert on, so that no test ever
/// pins a pixel value.
/// </summary>
/// <param name="CoveredPixels">How many pixels were drawn at all.</param>
/// <param name="MinX">The left edge of what was drawn.</param>
/// <param name="MinY">The top edge of what was drawn.</param>
/// <param name="MaxX">The right edge of what was drawn.</param>
/// <param name="MaxY">The bottom edge of what was drawn.</param>
/// <param name="AverageRed">The average red channel of the drawn pixels.</param>
/// <param name="AverageGreen">The average green channel of the drawn pixels.</param>
/// <param name="AverageBlue">The average blue channel of the drawn pixels.</param>
/// <param name="TotalPixels">How many pixels the frame has in all.</param>
internal readonly record struct RenderedFrameStats(
    int CoveredPixels,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int AverageRed,
    int AverageGreen,
    int AverageBlue,
    int TotalPixels)
{
    /// <summary>
    /// Gets the fraction of the frame that was drawn at all.
    /// </summary>
    public double Coverage => TotalPixels == 0 ? 0d : (double)CoveredPixels / TotalPixels;

    /// <summary>
    /// Gets the width of what was drawn, in pixels.
    /// </summary>
    public int DrawnWidth => CoveredPixels == 0 ? 0 : MaxX - MinX + 1;

    /// <summary>
    /// Gets the height of what was drawn, in pixels.
    /// </summary>
    public int DrawnHeight => CoveredPixels == 0 ? 0 : MaxY - MinY + 1;
}
