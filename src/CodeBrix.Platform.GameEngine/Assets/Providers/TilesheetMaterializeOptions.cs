using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Options that steer how an <see cref="ITilesheetAssetSource"/> turns an image, sprite atlas or
/// vector asset into a tilesheet. Every member is optional; the defaults reproduce the provider's
/// own behaviour.
/// </summary>
public sealed record TilesheetMaterializeOptions
{
    /// <summary>
    /// Gets the tile size to lay a uniform grid over a loose image, or <see langword="null"/> to
    /// keep the whole image as a single tile. Ignored for sprite atlases, whose sub-rectangles
    /// define their own regions.
    /// </summary>
    public Size? TileSize { get; init; }

    /// <summary>
    /// Gets the spacing between tiles of the grid, in pixels.
    /// </summary>
    public Spacing Padding { get; init; } = Spacing.None;

    /// <summary>
    /// Gets the margin around the grid within the source image, in pixels.
    /// </summary>
    public Spacing Margin { get; init; } = Spacing.None;

    /// <summary>
    /// Gets the collision type every frame of the produced region inherits.
    /// </summary>
    public TileCollisionType CollisionType { get; init; } = TileCollisionType.None;

    /// <summary>
    /// Gets the collision adjustment every frame of the produced region inherits, or
    /// <see langword="null"/> for no adjustment.
    /// </summary>
    public CollisionAdjust? CollisionAdjust { get; init; }

    /// <summary>
    /// Gets a value indicating whether a file extension carried in an atlas frame name (for
    /// example <c>ballBlue.png</c>) is stripped from the region name. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool StripFrameExtension { get; init; } = true;

    /// <summary>
    /// Gets the pixel size a vector asset is rasterized to, or <see langword="null"/> to use
    /// <see cref="VectorScale"/> or the asset's intrinsic size.
    /// </summary>
    public Size? VectorRasterSize { get; init; }

    /// <summary>
    /// Gets the factor applied to a vector asset's intrinsic size when
    /// <see cref="VectorRasterSize"/> is not given, or <see langword="null"/> for the intrinsic
    /// size.
    /// </summary>
    public float? VectorScale { get; init; }

    /// <summary>
    /// Gets the registry key to register the tilesheet under, overriding the descriptor's key, or
    /// <see langword="null"/> to use the descriptor's key.
    /// </summary>
    public string? RegisterAs { get; init; }
}
