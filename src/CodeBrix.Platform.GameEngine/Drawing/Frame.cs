using System.Drawing;
using SkiaSharp;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing; //was previously: Gondwana.Drawing;
/// <summary>
/// Represents the source Tilesheet and its coordinates to render on a destination.
/// </summary>
public struct Frame
{
    /// <summary>
    /// The tilesheet that contains the source bitmap for this frame.
    /// </summary>
    public readonly Tilesheet Tilesheet;

    /// <summary>
    /// The tilesheet region that contains the source bitmap for this frame.
    /// </summary>
    public readonly string RegionName;

    /// <summary>
    /// The horizontal tile coordinate (column index) within the tilesheet.
    /// </summary>
    public readonly int XTile;

    /// <summary>
    /// The vertical tile coordinate (row index) within the tilesheet.
    /// </summary>
    public readonly int YTile;

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> struct with the specified tilesheet and tile coordinates.
    /// </summary>
    /// <param name="tilesheet">The tilesheet containing the source bitmap.</param>
    /// <param name="xTile">The horizontal tile coordinate (column index) within the tilesheet.</param>
    /// <param name="yTile">The vertical tile coordinate (row index) within the tilesheet.</param>
    public Frame(Tilesheet tilesheet, int xTile, int yTile)
    {
        Tilesheet = tilesheet;
        RegionName = TilesheetRegion.DefaultRegionName;
        XTile = xTile;
        YTile = yTile;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> struct with the specified tilesheet and tile coordinates.
    /// </summary>
    /// <param name="tilesheet">The tilesheet containing the source bitmap.</param>
    /// <param name="regionName">The tilesheet region containing the source bitmap.</param>
    /// <param name="xTile">The horizontal tile coordinate (column index) within the tilesheet.</param>
    /// <param name="yTile">The vertical tile coordinate (row index) within the tilesheet.</param>
    public Frame(Tilesheet tilesheet, string regionName, int xTile, int yTile)
    {
        Tilesheet = tilesheet;
        RegionName = regionName;
        XTile = xTile;
        YTile = yTile;
    }

    /// <summary>
    /// Gets the SkiaSharp bitmap for this frame at the specified tile coordinates.
    /// Returns <see langword="null"/> if the tilesheet is not available.
    /// </summary>
    /// <returns>The frame bitmap, or <see langword="null"/>.</returns>
    public readonly SKBitmap? SkBitmap => Tilesheet?.GetBitmap(RegionName, XTile, YTile);

    /// <summary>
    /// Gets the SkiaSharp image for this frame at the specified tile coordinates.
    /// Returns <see langword="null"/> if the tilesheet is not available.
    /// </summary>
    /// <returns>The frame image, or <see langword="null"/>.</returns>
    public readonly SKImage? SkImage => Tilesheet?.GetImage(RegionName, XTile, YTile);

    /// <summary>
    /// Gets the base tile size (without overhang) from the tilesheet.
    /// Returns <see cref="Size.Empty"/> if the tilesheet is not available.
    /// </summary>
    public readonly Size TileSize => Tilesheet?.GetRegion(RegionName)?.TileSize ?? Size.Empty;

    /// <summary>
    /// Gets the overhang dimensions (in pixels) that extend beyond the base tile boundaries.
    /// Returns <see cref="Spacing.None"/> if the tilesheet is not available.
    /// </summary>
    public readonly Spacing Overhang => Tilesheet?.GetRegion(RegionName)?.Overhang ?? Spacing.None;

    /// <summary>
    /// Gets or sets the collision adjustment associated with this frame's region coordinates.
    /// Returns <see cref="Physics.Collisions.CollisionAdjust.None"/> if the tilesheet is not available.
    /// </summary>
    /// <remarks>
    /// A frame is a lightweight tilesheet reference, so assigning this property updates the
    /// authoritative per-frame metadata owned by <see cref="TilesheetRegion"/> and its cache.
    /// Assigning a value always records an explicit override, even when the value equals the
    /// region default.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the setter when the frame's tilesheet region cannot be resolved.
    /// </exception>
    public CollisionAdjust CollisionAdjust
    {
        readonly get => Tilesheet?.GetRegion(RegionName)?.GetFrameCollisionAdjust(XTile, YTile)
            ?? Physics.Collisions.CollisionAdjust.None;
        set
        {
            var region = Tilesheet?.GetRegion(RegionName)
                ?? throw new InvalidOperationException(
                    $"Tilesheet region '{RegionName}' could not be resolved for this frame.");

            region.SetFrameCollisionAdjust(XTile, YTile, value);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this frame carries an explicit collision adjustment rather
    /// than inheriting its region's <see cref="TilesheetRegion.CollisionAdjust"/>.
    /// </summary>
    public readonly bool HasCollisionAdjustOverride =>
        Tilesheet?.GetRegion(RegionName)?.TryGetFrameCollisionAdjustOverride(XTile, YTile, out _) == true;

    /// <summary>
    /// Removes this frame's explicit collision adjustment so that it once again inherits its
    /// region's <see cref="TilesheetRegion.CollisionAdjust"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when an explicit override was removed; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the frame's tilesheet region cannot be resolved.
    /// </exception>
    public readonly bool ClearCollisionAdjustOverride()
    {
        var region = Tilesheet?.GetRegion(RegionName)
            ?? throw new InvalidOperationException(
                $"Tilesheet region '{RegionName}' could not be resolved for this frame.");

        return region.ClearFrameCollisionAdjustOverride(XTile, YTile);
    }

    /// <summary>
    /// Gets the frame-local collision rectangle derived from <see cref="TileSize"/> and
    /// <see cref="CollisionAdjust"/>.
    /// </summary>
    public readonly Rectangle CollisionArea =>
        CollisionAdjust.ApplyTo(new Rectangle(Point.Empty, TileSize));

    /// <summary>
    /// Gets or sets the effective collision type associated with this frame's region coordinates.
    /// Returns <see cref="TileCollisionType.None"/> if the tilesheet is not available.
    /// </summary>
    /// <remarks>
    /// A frame is a lightweight tilesheet reference, so assigning this property updates the
    /// authoritative per-frame metadata owned by <see cref="TilesheetRegion"/>. Assigning a value
    /// always records an explicit override, even when the value equals the region default.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the setter when the frame's tilesheet region cannot be resolved.
    /// </exception>
    public TileCollisionType CollisionType
    {
        readonly get => Tilesheet?.GetRegion(RegionName)?.GetFrameCollisionType(XTile, YTile)
            ?? TileCollisionType.None;
        set
        {
            var region = Tilesheet?.GetRegion(RegionName)
                ?? throw new InvalidOperationException(
                    $"Tilesheet region '{RegionName}' could not be resolved for this frame.");

            region.SetFrameCollisionType(XTile, YTile, value);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this frame carries an explicit collision type rather than
    /// inheriting its region's <see cref="TilesheetRegion.CollisionType"/>.
    /// </summary>
    public readonly bool HasCollisionTypeOverride =>
        Tilesheet?.GetRegion(RegionName)?.TryGetFrameCollisionTypeOverride(XTile, YTile, out _) == true;

    /// <summary>
    /// Removes this frame's explicit collision type so that it once again inherits its region's
    /// <see cref="TilesheetRegion.CollisionType"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when an explicit override was removed; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the frame's tilesheet region cannot be resolved.
    /// </exception>
    public readonly bool ClearCollisionTypeOverride()
    {
        var region = Tilesheet?.GetRegion(RegionName)
            ?? throw new InvalidOperationException(
                $"Tilesheet region '{RegionName}' could not be resolved for this frame.");

        return region.ClearFrameCollisionTypeOverride(XTile, YTile);
    }
}