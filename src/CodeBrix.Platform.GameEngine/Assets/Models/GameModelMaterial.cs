using System.Numerics;

namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// The appearance of the triangles of a <see cref="GameModelMesh"/>: a base colour, an optional
/// base-colour texture, how alpha is treated, and the metallic-roughness factors a shading model
/// may use.
/// </summary>
/// <remarks>
/// A material is pure data and holds no graphics-device handles. The texture arrives as plain
/// bytes rather than a bitmap object so that a consumer can upload it directly, whether it draws
/// with a graphics device or rasterizes on the CPU.
/// </remarks>
public sealed class GameModelMaterial
{
    /// <summary>
    /// Gets the material name the source asset carried, or <see langword="null"/> when it had
    /// none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets how the alpha of the material is interpreted. Defaults to
    /// <see cref="GameModelAlphaMode.Opaque"/>.
    /// </summary>
    public GameModelAlphaMode AlphaMode { get; init; } = GameModelAlphaMode.Opaque;

    /// <summary>
    /// Gets the base colour of the material as linear RGBA factors, each normally in
    /// <c>[0, 1]</c>. Defaults to opaque white, so it multiplies a texture without changing it.
    /// </summary>
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    /// <summary>
    /// Gets the alpha value at and above which a sample is kept when
    /// <see cref="AlphaMode"/> is <see cref="GameModelAlphaMode.Mask"/>. Defaults to
    /// <c>0.5</c> and is ignored in the other modes.
    /// </summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>
    /// Gets the decoded base-colour texture, or <see langword="null"/> when the material is
    /// untextured.
    /// </summary>
    /// <remarks>
    /// The layout is RGBA8888: four bytes per pixel in red, green, blue, alpha order, row-major,
    /// with the TOP row of the image first, and
    /// <see cref="BaseColorTextureWidth"/> multiplied by <see cref="BaseColorTextureHeight"/>
    /// multiplied by four bytes in all. The colour channels are NOT premultiplied by alpha.
    /// </remarks>
    public byte[]? BaseColorTextureRgba { get; init; }

    /// <summary>
    /// Gets the width of the base-colour texture in pixels, or <c>0</c> when the material is
    /// untextured.
    /// </summary>
    public int BaseColorTextureWidth { get; init; }

    /// <summary>
    /// Gets the height of the base-colour texture in pixels, or <c>0</c> when the material is
    /// untextured.
    /// </summary>
    public int BaseColorTextureHeight { get; init; }

    /// <summary>
    /// Gets how metallic the surface is, from <c>0</c> (dielectric) to <c>1</c> (metal).
    /// Defaults to <c>1</c>, which is the glTF default; a simple shading model may ignore it.
    /// </summary>
    public float MetallicFactor { get; init; } = 1.0f;

    /// <summary>
    /// Gets how rough the surface is, from <c>0</c> (mirror-smooth) to <c>1</c> (fully diffuse).
    /// Defaults to <c>1</c>, which is the glTF default; a simple shading model may ignore it.
    /// </summary>
    public float RoughnessFactor { get; init; } = 1.0f;

    /// <summary>
    /// Gets a value indicating whether both faces of a triangle are drawn. When
    /// <see langword="false"/> a renderer is free to cull back faces.
    /// </summary>
    public bool DoubleSided { get; init; }
}
