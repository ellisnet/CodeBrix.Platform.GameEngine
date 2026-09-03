using System;
using System.Drawing;
using System.Text.Json.Serialization;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Per-edge inset amounts applied to visual bounds to produce a collision rectangle.
/// </summary>
/// <remarks>
/// <para>
/// Positive values move the corresponding edge inward toward the centre of the rectangle,
/// shrinking the collision box. Negative values move the edge outward, expanding it. The
/// convention is the same on all four edges: a positive <see cref="Bottom"/> raises the bottom
/// edge just as a positive <see cref="Top"/> lowers the top edge.
/// </para>
/// </remarks>
public struct CollisionAdjust : IEquatable<CollisionAdjust>
{
    /// <summary>
    /// Gets or sets the number of pixels by which to inset the top edge.
    /// A positive value moves the top edge down; a negative value moves it up.
    /// </summary>
    [JsonInclude]
    public int Top { get; set; }

    /// <summary>
    /// Gets or sets the number of pixels by which to inset the bottom edge.
    /// A positive value moves the bottom edge up; a negative value moves it down.
    /// </summary>
    [JsonInclude]
    public int Bottom { get; set; }

    /// <summary>
    /// Gets or sets the number of pixels by which to inset the left edge.
    /// A positive value moves the left edge right; a negative value moves it left.
    /// </summary>
    [JsonInclude]
    public int Left { get; set; }

    /// <summary>
    /// Gets or sets the number of pixels by which to inset the right edge.
    /// A positive value moves the right edge left; a negative value moves it right.
    /// </summary>
    [JsonInclude]
    public int Right { get; set; }

    /// <summary>
    /// Represents an adjustment with no pixel insets (all values are zero).
    /// </summary>
    public static readonly CollisionAdjust None = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CollisionAdjust"/> struct.
    /// </summary>
    /// <param name="top">The signed inset amount for the top edge.</param>
    /// <param name="bottom">The signed inset amount for the bottom edge.</param>
    /// <param name="left">The signed inset amount for the left edge.</param>
    /// <param name="right">The signed inset amount for the right edge.</param>
    public CollisionAdjust(int top, int bottom, int left, int right)
    {
        Top = top;
        Bottom = bottom;
        Left = left;
        Right = right;
    }

    /// <summary>
    /// Applies this adjustment to the supplied rectangle.
    /// </summary>
    /// <param name="rectangle">The unadjusted visual rectangle.</param>
    /// <returns>The derived collision rectangle.</returns>
    public readonly Rectangle ApplyTo(Rectangle rectangle)
    {
        return Rectangle.FromLTRB(
            rectangle.Left + Left,
            rectangle.Top + Top,
            rectangle.Right - Right,
            rectangle.Bottom - Bottom);
    }

    /// <summary>
    /// Determines whether this adjustment has the same edge values as another adjustment.
    /// </summary>
    /// <param name="other">The adjustment to compare with this instance.</param>
    /// <returns><see langword="true"/> when all four edge values match; otherwise, <see langword="false"/>.</returns>
    public readonly bool Equals(CollisionAdjust other) =>
        Top == other.Top &&
        Bottom == other.Bottom &&
        Left == other.Left &&
        Right == other.Right;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) =>
        obj is CollisionAdjust other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() =>
        HashCode.Combine(Top, Bottom, Left, Right);

    /// <summary>
    /// Determines whether two adjustments have the same edge values.
    /// </summary>
    /// <param name="left">The first adjustment to compare.</param>
    /// <param name="right">The second adjustment to compare.</param>
    /// <returns><see langword="true"/> when the adjustments are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(CollisionAdjust left, CollisionAdjust right) =>
        left.Equals(right);

    /// <summary>
    /// Determines whether two adjustments have different edge values.
    /// </summary>
    /// <param name="left">The first adjustment to compare.</param>
    /// <param name="right">The second adjustment to compare.</param>
    /// <returns><see langword="true"/> when the adjustments differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(CollisionAdjust left, CollisionAdjust right) =>
        !left.Equals(right);
}
