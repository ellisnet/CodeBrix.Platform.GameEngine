using System;

namespace KenneyAssetsDemo.Game;

/// <summary>
/// Turns a direction of travel on the screen into the row of a pre-rendered model sprite sheet that
/// shows the character facing that way.
/// </summary>
/// <remarks>
/// <para>
/// A model sheet is laid out one uniform-grid region per animation, COLUMNS = animation frames and
/// ROWS = camera directions, so a frame is addressed <c>sheet["walk", frame, direction]</c>. Row
/// <c>d</c> is rendered at camera yaw <c>StartYawDegrees + d * 360 / Directions</c>, and the camera
/// convention is: yaw 0 puts the camera on +Z looking at the origin, so a model that faces +Z faces the
/// viewer; increasing yaw swings the camera toward +X, which makes the model appear to turn toward the
/// viewer's left.
/// </para>
/// <para>
/// Read that back as screen directions, with <c>StartYawDegrees</c> at 0 and eight directions: row 0 is
/// the character walking towards the viewer (down the screen), row 2 is walking left, row 4 is walking
/// away (up the screen) and row 6 is walking right, with the diagonals in between. So the row index is
/// the angle of travel measured from "down the screen", turning towards "left", divided by the angle one
/// row covers. That is what <see cref="DirectionFromMovement"/> computes, for any number of directions
/// the sheet was rendered with.
/// </para>
/// <para>
/// This assumes the model itself faces +Z, which Kenney's character models do. A model authored facing
/// some other way is corrected once, by rendering its sheet with a <c>StartYawDegrees</c> that cancels
/// the difference, rather than by biasing this calculation.
/// </para>
/// </remarks>
internal static class ModelSheetFacing
{
    /// <summary>
    /// Gets the sheet row that faces the way something moving by the given screen-space delta is going.
    /// </summary>
    /// <param name="deltaX">The movement along the screen's X axis; positive is to the right.</param>
    /// <param name="deltaY">The movement along the screen's Y axis; positive is DOWN the screen.</param>
    /// <param name="directions">The number of direction rows the sheet was rendered with.</param>
    /// <param name="startYawDegrees">
    /// The <c>StartYawDegrees</c> the sheet was rendered with, which rotates every row by the same
    /// amount.
    /// </param>
    /// <returns>
    /// A row index from 0 to <paramref name="directions"/> - 1, or 0 when the delta is zero (nothing is
    /// moving, so no direction is implied) or <paramref name="directions"/> is not positive.
    /// </returns>
    internal static int DirectionFromMovement(
        float deltaX, float deltaY, int directions, float startYawDegrees = 0.0f)
    {
        if (directions < 1 || (deltaX == 0.0f && deltaY == 0.0f)) { return 0; }

        //The angle of travel, in degrees, measured from "down the screen" and turning towards "left" -
        //  which is exactly how the sheet's rows advance
        float degrees = (MathF.Atan2(-deltaX, deltaY) * 180.0f / MathF.PI) - startYawDegrees;
        float degreesPerRow = 360.0f / directions;
        int row = (int)MathF.Round(degrees / degreesPerRow);

        //Round can land on -directions..directions, and a negative remainder is not a row
        return ((row % directions) + directions) % directions;
    }
}
