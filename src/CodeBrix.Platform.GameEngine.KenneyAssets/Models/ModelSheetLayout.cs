using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// Works out where every rendered cell of a model sprite sheet goes, following the output layout
/// contract of <see cref="ModelRenderOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The sheet carries one uniform-grid region per rendered animation, named after that animation,
/// plus a region named <see cref="ModelRenderOptions.RestPoseRegionName"/> when
/// <see cref="ModelRenderOptions.IncludeRestPose"/> is set. Within a region, COLUMNS are animation
/// frames in playback order - the rest-pose region has exactly one - and ROWS are camera directions.
/// Every cell is exactly <see cref="ModelRenderOptions.FrameSize"/>, so a frame is addressed
/// <c>sheet["walk", frame, direction]</c>.
/// </para>
/// <para>
/// Regions are stacked vertically in the order they are rendered, rest pose first, so the sheet is
/// as wide as its widest region and as tall as its regions put together.
/// </para>
/// </remarks>
internal sealed class ModelSheetLayout
{
    /// <summary>
    /// The largest sheet dimension allowed, in pixels. It is the texture size every graphics device
    /// the engine runs on can hold, so a sheet that would exceed it is refused rather than produced
    /// and silently unusable.
    /// </summary>
    internal const int MaxDimension = 8192;

    private ModelSheetLayout(Size frameSize, int directions, int width, int height,
        IReadOnlyList<ModelSheetRegion> regions)
    {
        FrameSize = frameSize;
        Directions = directions;
        Width = width;
        Height = height;
        Regions = regions;
    }

    /// <summary>
    /// Gets the size of one cell, in pixels.
    /// </summary>
    internal Size FrameSize { get; }

    /// <summary>
    /// Gets the number of camera directions, which is the row count of every region.
    /// </summary>
    internal int Directions { get; }

    /// <summary>
    /// Gets the sheet's width in pixels.
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// Gets the sheet's height in pixels.
    /// </summary>
    internal int Height { get; }

    /// <summary>
    /// Gets the regions, in the order they are laid out from the top of the sheet down.
    /// </summary>
    internal IReadOnlyList<ModelSheetRegion> Regions { get; }

    /// <summary>
    /// Lays out a sheet for a set of clips.
    /// </summary>
    /// <param name="options">The render options, which decide the cell size, the direction count and
    /// whether a rest-pose region is included.</param>
    /// <param name="clips">The clips to lay out, in the order they were asked for.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or
    /// <paramref name="clips"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the cell size or direction count is not positive, when there is nothing to render
    /// at all, when two regions would take the same name, or when the sheet would exceed
    /// <see cref="MaxDimension"/> in either dimension.
    /// </exception>
    internal static ModelSheetLayout Create(
        ModelRenderOptions options, IReadOnlyList<GameModelAnimationClip> clips)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clips);

        Size frameSize = options.FrameSize;

        if (frameSize.Width < 1 || frameSize.Height < 1)
        {
            throw new ArgumentException(
                "ModelRenderOptions.FrameSize must be at least one pixel wide and one pixel high.",
                nameof(options));
        }

        if (options.Directions < 1)
        {
            throw new ArgumentException(
                "ModelRenderOptions.Directions must be at least one.", nameof(options));
        }

        int directions = options.Directions;
        List<ModelSheetRegion> regions = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        //Long arithmetic, so that a wild frame size or frame count is caught by the dimension guard
        //  rather than wrapping round into a plausible-looking sheet
        long width = 0L;
        long top = 0L;

        if (options.IncludeRestPose)
        {
            AddRegion(ModelRenderOptions.RestPoseRegionName, clip: null, columns: 1);
        }

        foreach (GameModelAnimationClip clip in clips)
        {
            if (clip.FrameCount < 1)
            {
                throw new ArgumentException(
                    $"The animation '{clip.Name}' has no frames, so it cannot become a sheet region.",
                    nameof(clips));
            }

            AddRegion(clip.Name, clip, clip.FrameCount);
        }

        if (width > MaxDimension || top > MaxDimension)
        {
            throw new ArgumentException(
                $"The sprite sheet for this model would be {width} by {top} pixels, which exceeds the "
                + $"{MaxDimension} pixel limit. Render fewer animations, lower "
                + "ModelRenderOptions.AnimationFramesPerSecond, use a smaller "
                + "ModelRenderOptions.FrameSize, or split the animations across separate sheets by "
                + "materializing each one under its own key with TilesheetMaterializeOptions.RegisterAs.",
                nameof(options));
        }

        if (regions.Count == 0)
        {
            throw new ArgumentException(
                "There is nothing to render: no animation was asked for and "
                + "ModelRenderOptions.IncludeRestPose is false.",
                nameof(options));
        }

        return new ModelSheetLayout(frameSize, directions, (int)width, (int)top, regions);

        void AddRegion(string name, GameModelAnimationClip? clip, int columns)
        {
            if (!names.Add(name))
            {
                throw new ArgumentException(
                    $"Two regions of the sheet would be named '{name}'. An animation whose name "
                    + "collides with the rest-pose region name needs "
                    + "ModelRenderOptions.IncludeRestPose set to false, or a single request per sheet.",
                    nameof(clips));
            }

            long regionWidth = (long)columns * frameSize.Width;
            long regionHeight = (long)directions * frameSize.Height;

            width = Math.Max(width, regionWidth);
            long regionTop = top;
            top += regionHeight;

            if (width > MaxDimension || top > MaxDimension) { return; }

            regions.Add(new ModelSheetRegion(
                name, clip, columns, directions,
                new Rectangle(0, (int)regionTop, (int)regionWidth, (int)regionHeight)));
        }
    }
}
