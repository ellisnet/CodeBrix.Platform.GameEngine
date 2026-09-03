using SkiaSharp;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets; //was previously: Gondwana.Drawing.Tilesheets;
/// <summary>
/// Represents a cached slice of a tilesheet region containing both bitmap and image representations of a single tile.
/// </summary>
internal readonly struct TilesheetRegionSlice
{
    /// <summary>
    /// The bitmap representation of the tile slice.
    /// </summary>
    public readonly SKBitmap Bitmap;

    /// <summary>
    /// The image representation of the tile slice.
    /// </summary>
    public readonly SKImage Image;

    /// <summary>
    /// The collision adjustment associated with this cached frame.
    /// </summary>
    public readonly CollisionAdjust CollisionAdjust;

    /// <summary>
    /// Gets the frame-local collision rectangle derived from the slice bitmap and
    /// <see cref="CollisionAdjust"/>.
    /// </summary>
    public readonly Rectangle CollisionArea =>
        CollisionAdjust.ApplyTo(new Rectangle(0, 0, Bitmap.Width, Bitmap.Height));

    /// <summary>
    /// Initializes a new instance of the <see cref="TilesheetRegionSlice"/> struct with the specified bitmap, image and collision adjustment.
    /// </summary>
    /// <param name="bmp">The SKBitmap representation of the tile.</param>
    /// <param name="img">The SKImage representation of the tile.</param>
    /// <param name="collisionAdjust">The collision adjustment in effect for this frame.</param>
    public TilesheetRegionSlice(SKBitmap bmp, SKImage img, CollisionAdjust collisionAdjust)
    {
        Bitmap = bmp;
        Image = img;
        CollisionAdjust = collisionAdjust;
    }

    /// <summary>
    /// Returns a cache entry that reuses this slice's image resources with updated collision metadata.
    /// </summary>
    /// <param name="collisionAdjust">The collision adjustment the returned slice carries.</param>
    /// <returns>A slice sharing this slice's bitmap and image, carrying <paramref name="collisionAdjust"/>.</returns>
    public readonly TilesheetRegionSlice WithCollisionAdjust(CollisionAdjust collisionAdjust) =>
        new(Bitmap, Image, collisionAdjust);
}
