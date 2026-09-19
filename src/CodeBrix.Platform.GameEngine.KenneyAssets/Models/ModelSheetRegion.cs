using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Models;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// One uniform-grid region of a model sprite sheet: the rest pose, or one animation, laid out with
/// animation frames across the columns and camera directions down the rows.
/// </summary>
internal sealed class ModelSheetRegion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSheetRegion"/> class.
    /// </summary>
    /// <param name="name">The region's name, which is the animation's name or the rest-pose name.</param>
    /// <param name="clip">The animation the region holds, or <see langword="null"/> for the rest pose.</param>
    /// <param name="columns">The number of frame columns.</param>
    /// <param name="rows">The number of direction rows.</param>
    /// <param name="area">The region's rectangle within the sheet, in pixels.</param>
    internal ModelSheetRegion(
        string name, GameModelAnimationClip? clip, int columns, int rows, Rectangle area)
    {
        Name = name;
        Clip = clip;
        Columns = columns;
        Rows = rows;
        Area = area;
    }

    /// <summary>
    /// Gets the region's name: the animation's own name, or
    /// <see cref="Assets.Providers.ModelRenderOptions.RestPoseRegionName"/> for the rest pose.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// Gets the animation the region's columns are the frames of, or <see langword="null"/> when the
    /// region holds the un-animated model.
    /// </summary>
    internal GameModelAnimationClip? Clip { get; }

    /// <summary>
    /// Gets the number of columns, which is the clip's frame count, or <c>1</c> for the rest pose.
    /// </summary>
    internal int Columns { get; }

    /// <summary>
    /// Gets the number of rows, which is the number of camera directions.
    /// </summary>
    internal int Rows { get; }

    /// <summary>
    /// Gets the region's rectangle within the sheet, in pixels.
    /// </summary>
    internal Rectangle Area { get; }
}
