using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS; //was previously: Gondwana.Drawing.Tilesheets.GTS;
/// <summary>
/// Produces deterministic authoring diagnostics for a <see cref="TilesheetDefinition"/> without
/// creating runtime tiles, decoding the source image, or registering any resource.
/// </summary>
/// <remarks>
/// The validator is intended for authoring and tooling scenarios (for example a definition-file
/// linter) where a definition must be checked before it is turned into a live
/// <see cref="Tilesheet"/>. It is side-effect free and never throws for bad content: every problem
/// it finds is returned as a human-readable message. Loading a definition remains permissive, so a
/// definition that produces messages here may still load at run time.
/// </remarks>
public static class TilesheetDefinitionValidator
{
    /// <summary>
    /// Checks region layouts and frame metadata, optionally against known source-image dimensions.
    /// </summary>
    /// <param name="definition">The tilesheet definition to inspect.</param>
    /// <param name="imageWidth">
    /// The width, in pixels, of the source image, or <see langword="null"/> when the image dimensions
    /// are unknown. When supplied, region areas that extend past the right edge are reported.
    /// </param>
    /// <param name="imageHeight">
    /// The height, in pixels, of the source image, or <see langword="null"/> when the image dimensions
    /// are unknown. When supplied, region areas that extend past the bottom edge are reported.
    /// </param>
    /// <returns>
    /// A read-only list of diagnostic messages, in the order the problems were found. An empty list
    /// means no problem was detected.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
    public static IReadOnlyList<string> Validate(TilesheetDefinition definition, int? imageWidth = null, int? imageHeight = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in definition.Regions ?? [])
        {
            if (region is null)
            {
                errors.Add("Regions contains a null entry.");
                continue;
            }

            var label = $"Region '{region.Name}'";

            if (string.IsNullOrWhiteSpace(region.Name))
                errors.Add($"{label}: name is empty.");

            if (!names.Add(region.Name))
                errors.Add($"{label}: duplicate name.");

            if (region.Area.Width <= 0 || region.Area.Height <= 0 ||
                region.TileSize.Width <= 0 || region.TileSize.Height <= 0)
            {
                errors.Add($"{label}: area and tile dimensions must be positive.");
                continue;
            }

            if (region.Area.X < 0 || region.Area.Y < 0 ||
                (imageWidth.HasValue && (long)region.Area.X + region.Area.Width > imageWidth) ||
                (imageHeight.HasValue && (long)region.Area.Y + region.Area.Height > imageHeight))
            {
                errors.Add($"{label}: area is outside the source image.");
            }

            if (Negative(region.TilePadding) || Negative(region.RegionMargin))
                errors.Add($"{label}: tile padding and region margins cannot be negative.");

            if (Negative(region.Overhang))
                errors.Add($"{label}: overhang cannot be negative.");

            var (columns, rows) = GridSize(region);

            if (columns <= 0 || rows <= 0)
                errors.Add($"{label}: layout contains no complete frames.");

            CheckCollision(region.CollisionAdjust, region, label, errors);

            if (!Enum.IsDefined(region.CollisionType))
                errors.Add($"{label}: unknown collision type.");

            var coordinates = new HashSet<(int, int)>();

            foreach (var frame in region.Frames ?? [])
            {
                if (frame is null)
                {
                    errors.Add($"{label}: null frame metadata.");
                    continue;
                }

                var frameLabel = $"{label}, frame ({frame.XTile}, {frame.YTile})";

                if (!coordinates.Add((frame.XTile, frame.YTile)))
                    errors.Add($"{frameLabel}: duplicate frame metadata.");

                if (frame.XTile < 0 || frame.YTile < 0 || frame.XTile >= columns || frame.YTile >= rows)
                    errors.Add($"{frameLabel}: coordinates are outside the frame grid.");

                if (frame.CollisionAdjust is { } adjust)
                    CheckCollision(adjust, region, frameLabel, errors);

                if (frame.CollisionType is { } type && !Enum.IsDefined(type))
                    errors.Add($"{frameLabel}: unknown collision type.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Computes the frame-grid dimensions of a region using the run-time padding and margin convention.
    /// </summary>
    /// <param name="region">The region whose grid dimensions are computed.</param>
    /// <returns>
    /// The number of whole frame columns and rows the region's area can hold. Either value is zero
    /// when the region's geometry leaves no room for a complete frame.
    /// </returns>
    /// <remarks>
    /// Each frame occupies its tile size plus the region's tile padding on all four sides, and the
    /// region margin is removed from the available area before the division.
    /// </remarks>
    public static (long Columns, long Rows) GridSize(TilesheetRegionDefinition region)
    {
        long width = (long)region.TileSize.Width + region.TilePadding.Left + region.TilePadding.Right;
        long height = (long)region.TileSize.Height + region.TilePadding.Top + region.TilePadding.Bottom;

        return (width <= 0
                ? 0
                : Math.Max(0, ((long)region.Area.Width - region.RegionMargin.Left - region.RegionMargin.Right) / width),
            height <= 0
                ? 0
                : Math.Max(0, ((long)region.Area.Height - region.RegionMargin.Top - region.RegionMargin.Bottom) / height));
    }

    private static bool Negative(Spacing spacing) =>
        spacing.Left < 0 || spacing.Right < 0 || spacing.Top < 0 || spacing.Bottom < 0;

    private static void CheckCollision(CollisionAdjust adjust, TilesheetRegionDefinition region, string label, List<string> errors)
    {
        // Negative adjustments intentionally expand collision bounds. Only inverted
        // geometry is impossible; zero-sized collision areas may disable collisions.
        if ((long)adjust.Left + adjust.Right > region.TileSize.Width ||
            (long)adjust.Top + adjust.Bottom > region.TileSize.Height)
        {
            errors.Add($"{label}: collision adjustments invert the frame's collision rectangle.");
        }
    }
}
