using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS; //was previously: Gondwana.Drawing.Tilesheets.GTS;
/// <summary>
/// Defines collision metadata for one frame within a tilesheet region.
/// </summary>
/// <remarks>
/// A frame record is written for every frame coordinate in a region, but only frames carrying an
/// explicit override store a <see cref="CollisionAdjust"/> value. A missing (null) value means the
/// frame inherits <see cref="TilesheetRegionDefinition.CollisionAdjust"/>, which is also how
/// definition files written before per-frame metadata existed are read.
/// </remarks>
public sealed class TilesheetFrameDefinition
{
    /// <summary>
    /// Gets or sets the zero-based frame column within the region.
    /// </summary>
    public int XTile { get; set; }

    /// <summary>
    /// Gets or sets the zero-based frame row within the region.
    /// </summary>
    public int YTile { get; set; }

    /// <summary>
    /// Gets or sets the frame-specific collision adjustment.
    /// A missing value inherits the region adjustment for backward compatibility.
    /// </summary>
    public CollisionAdjust? CollisionAdjust { get; set; }

    /// <summary>
    /// Gets or sets the frame-specific collision type.
    /// A missing value inherits the region collision type; an explicit
    /// <see cref="TileCollisionType.None"/> disables collision for this frame.
    /// </summary>
    public TileCollisionType? CollisionType { get; set; }
}
