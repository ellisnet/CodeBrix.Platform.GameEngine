using System.Numerics;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// The one camera framing a whole sprite sheet is rendered with: where the camera orbits, how far
/// away it sits, and how model units become cell pixels.
/// </summary>
/// <remarks>
/// A fit is computed ONCE per sheet, over the union of the rest pose and every frame of every
/// animation the sheet carries, and then used for every cell. That is what the output layout
/// contract means by one common scale: an animated model never appears to breathe or jump in size
/// from one cell to the next, and no pose can clip against a cell edge.
/// </remarks>
internal readonly struct ModelCameraFit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCameraFit"/> struct.
    /// </summary>
    /// <param name="center">The point in model space the camera orbits and looks at.</param>
    /// <param name="radius">The distance from <paramref name="center"/> to the furthest vertex.</param>
    /// <param name="scale">Cell pixels per model unit, for an orthographic camera.</param>
    /// <param name="offsetX">The horizontal view-space offset that centres the model in the cell.</param>
    /// <param name="offsetY">The vertical view-space offset that centres the model in the cell.</param>
    /// <param name="distance">The camera's distance from <paramref name="center"/>.</param>
    /// <param name="focalLength">The focal length in cell pixels, for a perspective camera.</param>
    /// <param name="isPerspective">Whether the fit is for a perspective camera.</param>
    internal ModelCameraFit(
        Vector3 center,
        float radius,
        float scale,
        float offsetX,
        float offsetY,
        float distance,
        float focalLength,
        bool isPerspective)
    {
        Center = center;
        Radius = radius;
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Distance = distance;
        FocalLength = focalLength;
        IsPerspective = isPerspective;
    }

    /// <summary>
    /// Gets the point in model space the camera orbits and looks at, which is the model's pivot
    /// when it has one and the centre of its bounds otherwise.
    /// </summary>
    internal Vector3 Center { get; }

    /// <summary>
    /// Gets the distance from <see cref="Center"/> to the furthest vertex of any pose, which is
    /// independent of the camera direction and is what a perspective camera is placed by.
    /// </summary>
    internal float Radius { get; }

    /// <summary>
    /// Gets how many cell pixels one model unit covers under an orthographic camera. Unused for a
    /// perspective camera, which scales by <see cref="FocalLength"/> divided by depth.
    /// </summary>
    internal float Scale { get; }

    /// <summary>
    /// Gets the horizontal view-space offset subtracted before projection, which centres the widest
    /// pose of the sheet in the cell. Shared by every direction, so the model does not slide about
    /// as it turns.
    /// </summary>
    internal float OffsetX { get; }

    /// <summary>
    /// Gets the vertical view-space offset subtracted before projection.
    /// </summary>
    internal float OffsetY { get; }

    /// <summary>
    /// Gets the camera's distance from <see cref="Center"/> along the view axis. It always exceeds
    /// <see cref="Radius"/>, so no vertex of any pose can fall behind the camera and no near plane
    /// clipping is needed.
    /// </summary>
    internal float Distance { get; }

    /// <summary>
    /// Gets the focal length in cell pixels for a perspective camera: a point at unit depth is
    /// projected this many pixels from the cell's centre per model unit.
    /// </summary>
    internal float FocalLength { get; }

    /// <summary>
    /// Gets a value indicating whether the fit is for a perspective camera rather than an
    /// orthographic one.
    /// </summary>
    internal bool IsPerspective { get; }
}
