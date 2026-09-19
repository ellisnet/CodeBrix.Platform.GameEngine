using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// Draws a <see cref="GameModel"/> into sprite frames with a pure-managed, z-buffered software
/// triangle rasterizer, so that a game that draws in two dimensions can use three-dimensional
/// Kenney art without any graphics device.
/// </summary>
/// <remarks>
/// <para>
/// CAMERA. Yaw is the camera's azimuth around the model's up axis (+Y). At yaw 0 the camera sits on
/// the +Z side and looks toward <see cref="ModelCameraFit.Center"/>, so a model authored facing +Z -
/// the glTF convention - faces the viewer. Increasing yaw swings the camera toward +X, which makes
/// the model appear to turn toward the viewer's left. <see cref="ModelRenderOptions.PitchDegrees"/>
/// lifts the camera above the model's horizontal plane.
/// </para>
/// <para>
/// SHADING is Lambert diffuse plus a constant ambient term, evaluated per vertex from the model's
/// own normals and interpolated across each triangle.
/// <see cref="ModelRenderOptions.LightDirection"/> is in MODEL space and points from the model
/// toward the light, so the lit side belongs to the model and stays with it as the camera turns.
/// </para>
/// <para>
/// TEXTURING samples the material's base-colour map with NEAREST filtering and repeat wrapping.
/// Nearest is deliberate: a Kenney colormap is an atlas of flat colour patches whose texels a model
/// points at individually, and a bilinear tap at a patch edge would blend in the neighbouring
/// colour. Repeat wrapping is the glTF default and is needed in practice - the blocky-character
/// models address their texture with coordinates outside the unit square. Antialiasing comes from
/// supersampling instead, not from texture filtering.
/// </para>
/// <para>
/// TRANSPARENCY. Opaque and alpha-masked triangles are drawn first with depth writes; blended
/// triangles are drawn afterwards, sorted back to front, testing depth but not writing it. The
/// colour buffer is composited premultiplied over a transparent background, which is exactly what
/// a Skia bitmap wants and what makes the supersampled downscale correct.
/// </para>
/// <para>
/// The renderer holds no state between calls beyond its options and is safe to use from several
/// threads.
/// </para>
/// </remarks>
internal sealed class ModelSpriteRenderer
{
    /// <summary>
    /// The largest supersampling factor honoured. A larger value is clamped to this, because the
    /// cost grows with its square while the visible gain stops well before here.
    /// </summary>
    internal const int MaxSupersample = 8;

    private const float Epsilon = 1e-7f;

    private readonly float _ambient;
    private readonly float _fitPadding;
    private readonly float _halfFieldOfView;
    private readonly Vector3 _light;
    private readonly float _pitchRadians;
    private readonly SKSamplingOptions _sampling;
    private readonly float _startYawRadians;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSpriteRenderer"/> class.
    /// </summary>
    /// <param name="options">The camera, animation and shading settings to render with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <see cref="ModelRenderOptions.FrameSize"/> is not at least one pixel in each dimension, or
    /// <see cref="ModelRenderOptions.Directions"/> is less than one.</exception>
    internal ModelSpriteRenderer(ModelRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.FrameSize.Width < 1 || options.FrameSize.Height < 1)
        {
            throw new ArgumentException(
                "ModelRenderOptions.FrameSize must be at least one pixel wide and one pixel high.",
                nameof(options));
        }

        if (options.Directions < 1)
        {
            throw new ArgumentException(
                "ModelRenderOptions.Directions must be at least one.",
                nameof(options));
        }

        Options = options;
        FrameSize = options.FrameSize;
        Directions = options.Directions;
        Supersample = Math.Clamp(options.Supersample, 1, MaxSupersample);

        _ambient = Math.Clamp(options.AmbientLight, 0f, 1f);
        _fitPadding = Math.Clamp(options.FitPadding, 0f, 0.45f);
        _startYawRadians = float.DegreesToRadians(options.StartYawDegrees);

        //A camera exactly above the model has no well-defined right vector, so the pitch stops just
        //  short of straight down
        _pitchRadians = float.DegreesToRadians(Math.Clamp(options.PitchDegrees, -89.5f, 89.5f));
        _halfFieldOfView = float.DegreesToRadians(Math.Clamp(options.FieldOfViewDegrees, 1f, 120f)) * 0.5f;

        _light = options.LightDirection.LengthSquared() > Epsilon
            ? Vector3.Normalize(options.LightDirection)
            : Vector3.UnitY;

        //Mitchell is the engine's own "high quality" resampler; nearest is exact when there is
        //  nothing to downscale
        _sampling = Supersample > 1
            ? new SKSamplingOptions(SKCubicResampler.Mitchell)
            : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
    }

    /// <summary>
    /// Gets the options the renderer was built with.
    /// </summary>
    internal ModelRenderOptions Options { get; }

    /// <summary>
    /// Gets the size in pixels of one rendered cell.
    /// </summary>
    internal Size FrameSize { get; }

    /// <summary>
    /// Gets the number of camera directions a sheet's rows cover.
    /// </summary>
    internal int Directions { get; }

    /// <summary>
    /// Gets the supersampling factor actually used, which is the requested one clamped to
    /// <c>1</c>..<see cref="MaxSupersample"/>.
    /// </summary>
    internal int Supersample { get; }

    /// <summary>
    /// Gets the camera yaw of one direction row, in degrees.
    /// </summary>
    /// <param name="direction">The zero-based direction index.</param>
    /// <returns>
    /// <see cref="ModelRenderOptions.StartYawDegrees"/> plus the direction's share of a full turn.
    /// </returns>
    internal float DirectionYawDegrees(int direction) =>
        Options.StartYawDegrees + (direction * 360f / Directions);

    /// <summary>
    /// Computes the single camera framing every cell of a sheet shares.
    /// </summary>
    /// <param name="model">The model to frame.</param>
    /// <param name="clips">
    /// Every clip the sheet will carry, so that no pose of any of them can clip a cell edge, or
    /// <see langword="null"/> when the sheet carries the rest pose only.
    /// </param>
    /// <returns>The fit.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is null.</exception>
    internal ModelCameraFit ComputeFit(GameModel model, IReadOnlyList<GameModelAnimationClip>? clips)
    {
        ArgumentNullException.ThrowIfNull(model);

        Vector3 center = model.Pivot ?? model.BoundsCenter;
        List<float[]> poses = CollectPoses(model, clips);

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        float radiusSquared = 0f;
        bool any = false;

        for (int direction = 0; direction < Directions; direction++)
        {
            (Vector3 right, Vector3 up, _) = BuildBasis(direction);

            foreach (float[] positions in poses)
            {
                for (int i = 0; i + 2 < positions.Length; i += 3)
                {
                    Vector3 local = new Vector3(positions[i], positions[i + 1], positions[i + 2]) - center;

                    float x = Vector3.Dot(local, right);
                    float y = Vector3.Dot(local, up);

                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }

                    //The distance from the orbit centre does not depend on the direction, so the
                    //  first pass over the poses settles the bounding radius
                    float lengthSquared = local.LengthSquared();
                    if (lengthSquared > radiusSquared) { radiusSquared = lengthSquared; }

                    any = true;
                }
            }
        }

        if (!any)
        {
            return new ModelCameraFit(center, 0f, 1f, 0f, 0f, 1f, 1f, IsPerspective());
        }

        float radius = MathF.Sqrt(radiusSquared);
        float extentX = MathF.Max(maxX - minX, Epsilon);
        float extentY = MathF.Max(maxY - minY, Epsilon);
        float usableWidth = FrameSize.Width * (1f - (2f * _fitPadding));
        float usableHeight = FrameSize.Height * (1f - (2f * _fitPadding));

        if (IsPerspective())
        {
            //The vertical field of view maps to the cell height; the model is fitted as a sphere
            //  about the orbit centre, which makes the framing identical for every direction.
            float focalLength = FrameSize.Height * 0.5f / MathF.Tan(_halfFieldOfView);
            float target = MathF.Max(MathF.Min(usableWidth, usableHeight) * 0.5f, Epsilon);
            float ratio = focalLength / target;
            float safeRadius = MathF.Max(radius, Epsilon);
            float distance = safeRadius * MathF.Sqrt(1f + (ratio * ratio));

            return new ModelCameraFit(center, radius, 1f, 0f, 0f, distance, focalLength, true);
        }

        float scale = MathF.Min(usableWidth / extentX, usableHeight / extentY);

        return new ModelCameraFit(
            center,
            radius,
            scale,
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f,
            radius + 1f,
            0f,
            false);
    }

    /// <summary>
    /// Renders one cell of a sheet into a rectangle of a canvas.
    /// </summary>
    /// <param name="canvas">The canvas to draw into, whose bitmap is premultiplied RGBA.</param>
    /// <param name="destination">The cell rectangle to fill.</param>
    /// <param name="model">The model to draw.</param>
    /// <param name="frame">The animation frame to draw, or <see langword="null"/> for the rest pose.</param>
    /// <param name="direction">The zero-based direction index, which chooses the camera yaw.</param>
    /// <param name="fit">The framing shared by the whole sheet.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> or
    /// <paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="frame"/> does not carry one
    /// mesh per mesh of <paramref name="model"/>.</exception>
    internal void RenderInto(
        SKCanvas canvas,
        SKRect destination,
        GameModel model,
        GameModelAnimationFrame? frame,
        int direction,
        in ModelCameraFit fit)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(model);

        int width = FrameSize.Width * Supersample;
        int height = FrameSize.Height * Supersample;
        byte[] pixels = Rasterize(model, frame, direction, fit, width, height);

        SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKImage image = SKImage.FromPixelCopy(info, pixels);

        canvas.DrawImage(image, destination, _sampling, null);
    }

    /// <summary>
    /// Renders one frame on its own, at <see cref="FrameSize"/>.
    /// </summary>
    /// <param name="model">The model to draw.</param>
    /// <param name="frame">The animation frame to draw, or <see langword="null"/> for the rest pose.</param>
    /// <param name="direction">The zero-based direction index, which chooses the camera yaw.</param>
    /// <param name="fit">The framing to draw with.</param>
    /// <returns>A premultiplied RGBA bitmap with a transparent background. The caller disposes it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is null.</exception>
    internal SKBitmap RenderFrame(
        GameModel model, GameModelAnimationFrame? frame, int direction, in ModelCameraFit fit)
    {
        SKBitmap bitmap = new(
            new SKImageInfo(FrameSize.Width, FrameSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

        try
        {
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Transparent);
            RenderInto(
                canvas,
                new SKRect(0f, 0f, FrameSize.Width, FrameSize.Height),
                model,
                frame,
                direction,
                fit);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    private bool IsPerspective() => Options.Projection == ModelProjection.Perspective;

    private static List<float[]> CollectPoses(
        GameModel model, IReadOnlyList<GameModelAnimationClip>? clips)
    {
        List<float[]> poses = [];

        foreach (GameModelMesh mesh in model.Meshes)
        {
            poses.Add(mesh.Positions);
        }

        if (clips is null) { return poses; }

        foreach (GameModelAnimationClip clip in clips)
        {
            foreach (GameModelAnimationFrame frame in clip.Frames)
            {
                foreach (GameModelFrameMesh frameMesh in frame.Meshes)
                {
                    poses.Add(frameMesh.Positions);
                }
            }
        }

        return poses;
    }

    private (Vector3 Right, Vector3 Up, Vector3 Back) BuildBasis(int direction)
    {
        float yaw = _startYawRadians + (direction * MathF.Tau / Directions);
        float cosPitch = MathF.Cos(_pitchRadians);

        //The camera's position relative to the orbit centre: yaw 0 puts it on +Z, and increasing
        //  yaw swings it toward +X
        Vector3 back = Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(_pitchRadians),
            MathF.Cos(yaw) * cosPitch));

        Vector3 right = Vector3.Cross(Vector3.UnitY, back);
        right = right.LengthSquared() > Epsilon ? Vector3.Normalize(right) : Vector3.UnitX;

        Vector3 up = Vector3.Normalize(Vector3.Cross(back, right));

        return (right, up, back);
    }

    private byte[] Rasterize(
        GameModel model,
        GameModelAnimationFrame? frame,
        int direction,
        in ModelCameraFit fit,
        int width,
        int height)
    {
        if (frame is not null && frame.Meshes.Count != model.Meshes.Count)
        {
            throw new ArgumentException(
                "An animation frame must carry one mesh per mesh of the model.", nameof(frame));
        }

        bool perspective = fit.IsPerspective;
        (Vector3 right, Vector3 up, Vector3 back) = BuildBasis(direction);

        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        float scale = fit.Scale * Supersample;
        float focalLength = fit.FocalLength * Supersample;

        float[] color = new float[width * height * 4];
        float[] depths = new float[width * height];
        Array.Fill(depths, float.MaxValue);

        int meshCount = model.Meshes.Count;
        MeshBatch[] batches = new MeshBatch[meshCount];
        List<(int Mesh, int Triangle, float Depth)> blended = [];

        for (int m = 0; m < meshCount; m++)
        {
            GameModelMesh mesh = model.Meshes[m];
            float[] positions = frame?.Meshes[m].Positions ?? mesh.Positions;
            float[] normals = frame?.Meshes[m].Normals ?? mesh.Normals;

            if (positions.Length != mesh.Positions.Length)
            {
                throw new ArgumentException(
                    "An animation frame's mesh must carry the same vertex count as the model's mesh.",
                    nameof(frame));
            }

            int vertexCount = positions.Length / 3;
            MeshBatch batch = new(vertexCount, mesh.Indices, ResolveMaterial(model, mesh.MaterialIndex));

            for (int v = 0; v < vertexCount; v++)
            {
                Vector3 local = new Vector3(positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2])
                    - fit.Center;

                float viewX = Vector3.Dot(local, right);
                float viewY = Vector3.Dot(local, up);
                float depth = MathF.Max(fit.Distance - Vector3.Dot(local, back), Epsilon);

                if (perspective)
                {
                    float invDepth = 1f / depth;
                    batch.ScreenX[v] = halfWidth + (focalLength * viewX * invDepth);
                    batch.ScreenY[v] = halfHeight - (focalLength * viewY * invDepth);
                    batch.InvDepth[v] = invDepth;
                }
                else
                {
                    batch.ScreenX[v] = halfWidth + ((viewX - fit.OffsetX) * scale);
                    batch.ScreenY[v] = halfHeight - ((viewY - fit.OffsetY) * scale);
                    batch.InvDepth[v] = 1f;
                }

                batch.Depth[v] = depth;

                Vector3 normal = new(normals[v * 3], normals[(v * 3) + 1], normals[(v * 3) + 2]);
                normal = normal.LengthSquared() > Epsilon ? Vector3.Normalize(normal) : up;
                float lambert = MathF.Max(Vector3.Dot(normal, _light), 0f);
                batch.Shade[v] = _ambient + ((1f - _ambient) * lambert);

                float u = 0f;
                float w = 0f;
                if ((v * 2) + 1 < mesh.TexCoords.Length)
                {
                    u = mesh.TexCoords[v * 2];
                    w = mesh.TexCoords[(v * 2) + 1];
                }

                batch.U[v] = u * batch.InvDepth[v];
                batch.V[v] = w * batch.InvDepth[v];
            }

            batches[m] = batch;

            if (batch.Material.AlphaMode != GameModelAlphaMode.Blend) { continue; }

            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                float triangleDepth = MathF.Max(
                    MathF.Max(batch.Depth[mesh.Indices[t]], batch.Depth[mesh.Indices[t + 1]]),
                    batch.Depth[mesh.Indices[t + 2]]);

                blended.Add((m, t, triangleDepth));
            }
        }

        //Opaque and masked geometry first, with depth writes
        for (int m = 0; m < meshCount; m++)
        {
            MeshBatch batch = batches[m];
            if (batch.Material.AlphaMode == GameModelAlphaMode.Blend) { continue; }

            for (int t = 0; t + 2 < batch.Indices.Length; t += 3)
            {
                RasterizeTriangle(color, depths, width, height, batch, t, perspective, writeDepth: true);
            }
        }

        //Then translucent geometry, back to front, testing depth without writing it
        if (blended.Count > 0)
        {
            blended.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));

            foreach ((int mesh, int triangle, float _) in blended)
            {
                RasterizeTriangle(
                    color, depths, width, height, batches[mesh], triangle, perspective, writeDepth: false);
            }
        }

        return ToPremultipliedBytes(color);
    }

    private void RasterizeTriangle(
        float[] color,
        float[] depths,
        int width,
        int height,
        MeshBatch batch,
        int triangleOffset,
        bool perspective,
        bool writeDepth)
    {
        int ia = (int)batch.Indices[triangleOffset];
        int ib = (int)batch.Indices[triangleOffset + 1];
        int ic = (int)batch.Indices[triangleOffset + 2];

        int vertexCount = batch.Depth.Length;
        if (ia >= vertexCount || ib >= vertexCount || ic >= vertexCount) { return; }

        float x0 = batch.ScreenX[ia];
        float y0 = batch.ScreenY[ia];
        float x1 = batch.ScreenX[ib];
        float y1 = batch.ScreenY[ib];
        float x2 = batch.ScreenX[ic];
        float y2 = batch.ScreenY[ic];

        float area = ((x1 - x0) * (y2 - y0)) - ((x2 - x0) * (y1 - y0));
        if (MathF.Abs(area) < Epsilon) { return; }

        //Screen y grows downward, so a triangle wound counter-clockwise in the source - a front face
        //  in glTF - comes out with a negative signed area here
        bool backFacing = area > 0f;
        if (backFacing && !batch.Material.DoubleSided) { return; }

        //The edge functions carry the sign of the signed area inside the triangle, so testing them
        //  against that sign is the coverage test for either winding
        float sign = backFacing ? 1f : -1f;
        float invArea = 1f / area;

        int minX = Math.Max((int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))), 0);
        int maxX = Math.Min((int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))), width - 1);
        int minY = Math.Max((int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))), 0);
        int maxY = Math.Min((int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))), height - 1);

        if (minX > maxX || minY > maxY) { return; }

        MaterialView material = batch.Material;
        float shade0 = batch.Shade[ia];
        float shade1 = batch.Shade[ib];
        float shade2 = batch.Shade[ic];

        for (int y = minY; y <= maxY; y++)
        {
            float pixelY = y + 0.5f;
            int row = y * width;

            for (int x = minX; x <= maxX; x++)
            {
                float pixelX = x + 0.5f;

                float e0 = ((x2 - x1) * (pixelY - y1)) - ((y2 - y1) * (pixelX - x1));
                float e1 = ((x0 - x2) * (pixelY - y2)) - ((y0 - y2) * (pixelX - x2));
                float e2 = ((x1 - x0) * (pixelY - y0)) - ((y1 - y0) * (pixelX - x0));

                if (e0 * sign < 0f || e1 * sign < 0f || e2 * sign < 0f) { continue; }

                float l0 = e0 * invArea;
                float l1 = e1 * invArea;
                float l2 = e2 * invArea;

                float invDepth = (l0 * batch.InvDepth[ia])
                    + (l1 * batch.InvDepth[ib])
                    + (l2 * batch.InvDepth[ic]);

                float depth = perspective
                    ? (invDepth > Epsilon ? 1f / invDepth : float.MaxValue)
                    : (l0 * batch.Depth[ia]) + (l1 * batch.Depth[ib]) + (l2 * batch.Depth[ic]);

                int pixel = row + x;
                if (depth >= depths[pixel]) { continue; }

                Vector4 sampled = material.BaseColor;

                if (material.HasTexture)
                {
                    float invW = perspective && invDepth > Epsilon ? 1f / invDepth : 1f;
                    float u = ((l0 * batch.U[ia]) + (l1 * batch.U[ib]) + (l2 * batch.U[ic])) * invW;
                    float v = ((l0 * batch.V[ia]) + (l1 * batch.V[ib]) + (l2 * batch.V[ic])) * invW;

                    sampled *= material.SampleNearest(u, v);
                }

                float alpha = sampled.W;

                switch (material.AlphaMode)
                {
                    case GameModelAlphaMode.Mask:
                        if (alpha < material.AlphaCutoff) { continue; }

                        alpha = 1f;
                        break;

                    case GameModelAlphaMode.Blend:
                        if (alpha <= 0f) { continue; }

                        break;

                    default:
                        alpha = 1f;
                        break;
                }

                float shade = Math.Clamp((l0 * shade0) + (l1 * shade1) + (l2 * shade2), 0f, 1f);
                float red = Math.Clamp(sampled.X, 0f, 1f) * shade;
                float green = Math.Clamp(sampled.Y, 0f, 1f) * shade;
                float blue = Math.Clamp(sampled.Z, 0f, 1f) * shade;
                int offset = pixel * 4;

                if (alpha >= 1f)
                {
                    color[offset] = red;
                    color[offset + 1] = green;
                    color[offset + 2] = blue;
                    color[offset + 3] = 1f;
                }
                else
                {
                    //Source-over in premultiplied space, which is what the buffer holds
                    float inverse = 1f - alpha;
                    color[offset] = (red * alpha) + (color[offset] * inverse);
                    color[offset + 1] = (green * alpha) + (color[offset + 1] * inverse);
                    color[offset + 2] = (blue * alpha) + (color[offset + 2] * inverse);
                    color[offset + 3] = alpha + (color[offset + 3] * inverse);
                }

                if (writeDepth) { depths[pixel] = depth; }
            }
        }
    }

    private static byte[] ToPremultipliedBytes(float[] color)
    {
        byte[] pixels = new byte[color.Length];

        for (int i = 0; i < color.Length; i += 4)
        {
            float alpha = Math.Clamp(color[i + 3], 0f, 1f);
            pixels[i] = ToByte(MathF.Min(color[i], alpha));
            pixels[i + 1] = ToByte(MathF.Min(color[i + 1], alpha));
            pixels[i + 2] = ToByte(MathF.Min(color[i + 2], alpha));
            pixels[i + 3] = ToByte(alpha);
        }

        return pixels;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f), 0, 255);

    private static MaterialView ResolveMaterial(GameModel model, int materialIndex)
    {
        if (materialIndex < 0 || materialIndex >= model.Materials.Count)
        {
            return new MaterialView(null);
        }

        return new MaterialView(model.Materials[materialIndex]);
    }

    //Everything the rasterizer needs about a material, resolved once per mesh
    private readonly struct MaterialView
    {
        private readonly byte[]? _texture;
        private readonly int _textureWidth;
        private readonly int _textureHeight;

        internal MaterialView(GameModelMaterial? material)
        {
            BaseColor = material?.BaseColorFactor ?? Vector4.One;
            AlphaMode = material?.AlphaMode ?? GameModelAlphaMode.Opaque;
            AlphaCutoff = material?.AlphaCutoff ?? 0.5f;
            DoubleSided = material?.DoubleSided ?? false;

            byte[]? texture = material?.BaseColorTextureRgba;
            int width = material?.BaseColorTextureWidth ?? 0;
            int height = material?.BaseColorTextureHeight ?? 0;

            if (texture is not null && width > 0 && height > 0 && texture.Length >= width * height * 4)
            {
                _texture = texture;
                _textureWidth = width;
                _textureHeight = height;
            }
            else
            {
                _texture = null;
                _textureWidth = 0;
                _textureHeight = 0;
            }
        }

        internal Vector4 BaseColor { get; }

        internal GameModelAlphaMode AlphaMode { get; }

        internal float AlphaCutoff { get; }

        internal bool DoubleSided { get; }

        internal bool HasTexture => _texture is not null;

        internal Vector4 SampleNearest(float u, float v)
        {
            if (_texture is null) { return Vector4.One; }

            int x = Wrap((int)MathF.Floor(u * _textureWidth), _textureWidth);

            //A glTF texture's v axis runs downward from the top row, which is the row order the
            //  decoded payload is in, so no flip is needed
            int y = Wrap((int)MathF.Floor(v * _textureHeight), _textureHeight);
            int offset = ((y * _textureWidth) + x) * 4;

            return new Vector4(
                _texture[offset] / 255f,
                _texture[offset + 1] / 255f,
                _texture[offset + 2] / 255f,
                _texture[offset + 3] / 255f);
        }

        private static int Wrap(int value, int size)
        {
            int wrapped = value % size;

            return wrapped < 0 ? wrapped + size : wrapped;
        }
    }

    //One mesh's vertices, projected and shaded once for the direction being drawn
    private sealed class MeshBatch
    {
        internal MeshBatch(int vertexCount, uint[] indices, MaterialView material)
        {
            ScreenX = new float[vertexCount];
            ScreenY = new float[vertexCount];
            Depth = new float[vertexCount];
            InvDepth = new float[vertexCount];
            U = new float[vertexCount];
            V = new float[vertexCount];
            Shade = new float[vertexCount];
            Indices = indices;
            Material = material;
        }

        internal float[] ScreenX { get; }

        internal float[] ScreenY { get; }

        internal float[] Depth { get; }

        internal float[] InvDepth { get; }

        internal float[] U { get; }

        internal float[] V { get; }

        internal float[] Shade { get; }

        internal uint[] Indices { get; }

        internal MaterialView Material { get; }
    }
}
