using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Drawing.Coordinates; //was previously: Gondwana.Drawing.Coordinates;

/// <summary>
/// The world-space repetition lattice of a wrapped <see cref="SceneLayer"/>: one translation
/// vector per grid axis, together with the axes that actually repeat.
/// </summary>
/// <remarks>A lattice of translations, independent of canonical object identity.</remarks>
/// <param name="Columns">World-space translation produced by advancing a whole grid width.</param>
/// <param name="Rows">World-space translation produced by advancing a whole grid height.</param>
/// <param name="WrapColumns">Whether the column axis repeats.</param>
/// <param name="WrapRows">Whether the row axis repeats.</param>
internal readonly record struct LayerPeriod(PointF Columns, PointF Rows, bool WrapColumns, bool WrapRows)
{
    /// <summary>
    /// Derives and validates the period vectors of a layer from its coordinate strategy.
    /// </summary>
    /// <param name="coordinates">The coordinate strategy that projects grid cells to world pixels.</param>
    /// <param name="layer">The layer whose grid and tile dimensions define the lattice.</param>
    /// <returns>The validated <see cref="LayerPeriod"/> for the layer.</returns>
    /// <exception cref="InvalidOperationException">
    /// The grid or tile dimensions are not positive, the projection cannot repeat cleanly with the
    /// configured dimensions, or the two period vectors are not independent and nonzero.
    /// </exception>
    internal static LayerPeriod Create(ISceneLayerCoordinates coordinates, SceneLayer layer)
    {
        if (layer.GridColumnCount <= 0 || layer.GridRowCount <= 0 || layer.TileWidth <= 0 || layer.TileHeight <= 0)
            throw new InvalidOperationException("Wrapping requires positive grid and tile dimensions.");

        PointF Period(int columns, int rows, bool enabled)
        {
            var origin = coordinates.GetAnchorPixelAtSceneLayerCoordinates(layer, PointF.Empty);
            var end = coordinates.GetAnchorPixelAtSceneLayerCoordinates(layer, new(columns, rows));
            var delta = new PointF(end.X - origin.X, end.Y - origin.Y);
            if (enabled)
            {
                // Includes both stagger parities and pixel-rounding phases.
                for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                {
                    var a = coordinates.GetAnchorPixelAtSceneLayerCoordinates(layer, new(x, y));
                    var b = coordinates.GetAnchorPixelAtSceneLayerCoordinates(layer, new(x + columns, y + rows));
                    if (b.X - a.X != delta.X || b.Y - a.Y != delta.Y)
                        throw new InvalidOperationException($"{layer.CoordinateSystemType} cannot repeat cleanly with these dimensions. Flat-top hex column wrapping requires even columns; pointed-top hex row wrapping requires even rows. Pixel-rounded projections also require an integral world-space period.");
                }
            }
            return delta;
        }

        var result = new LayerPeriod(Period(layer.GridColumnCount, 0, layer.WrapHorizontally),
            Period(0, layer.GridRowCount, layer.WrapVertically), layer.WrapHorizontally, layer.WrapVertically);
        if (Math.Abs(result.Columns.X * result.Rows.Y - result.Columns.Y * result.Rows.X) < 0.001)
            throw new InvalidOperationException("Wrapping requires independent, nonzero world-space period vectors.");
        return result;
    }

    /// <summary>
    /// Expresses a world-space delta in the period-vector basis.
    /// </summary>
    /// <param name="delta">The world-space delta, in pixels.</param>
    /// <returns>The column and row coefficients that reproduce <paramref name="delta"/>.</returns>
    internal PointF Coefficients(PointF delta)
    {
        double determinant = (double)Columns.X * Rows.Y - (double)Columns.Y * Rows.X;
        return new((float)((delta.X * (double)Rows.Y - delta.Y * (double)Rows.X) / determinant),
            (float)((Columns.X * (double)delta.Y - Columns.Y * (double)delta.X) / determinant));
    }

    /// <summary>
    /// Builds the world-space translation for a whole number (or fraction) of periods.
    /// </summary>
    /// <param name="column">Number of column periods to advance.</param>
    /// <param name="row">Number of row periods to advance.</param>
    /// <returns>The combined world-space translation, in pixels.</returns>
    internal PointF Offset(double column, double row) =>
        new((float)(column * Columns.X + row * Rows.X), (float)(column * Columns.Y + row * Rows.Y));

    /// <summary>
    /// Returns the image of <paramref name="point"/> that lies closest to <paramref name="reference"/>.
    /// </summary>
    /// <param name="point">The canonical world-space point.</param>
    /// <param name="reference">The world-space point to be closest to.</param>
    /// <returns>The nearest equivalent world-space point.</returns>
    internal PointF Nearest(PointF point, PointF reference)
    {
        var delta = new PointF(reference.X - point.X, reference.Y - point.Y);
        var coefficients = Coefficients(delta);
        // For a single axis the Euclidean projection, not the inverse lattice,
        // gives the closest equivalent point.
        double c = WrapColumns ? Math.Round(coefficients.X) : 0;
        double r = WrapRows ? Math.Round(coefficients.Y) : 0;
        if (WrapColumns && !WrapRows)
            c = Math.Round(((double)delta.X * Columns.X + (double)delta.Y * Columns.Y) / ((double)Columns.X * Columns.X + (double)Columns.Y * Columns.Y));
        if (WrapRows && !WrapColumns)
            r = Math.Round(((double)delta.X * Rows.X + (double)delta.Y * Rows.Y) / ((double)Rows.X * Rows.X + (double)Rows.Y * Rows.Y));
        var initial = Offset(c, r);
        var best = new PointF(point.X + initial.X, point.Y + initial.Y);
        if (!WrapColumns || !WrapRows) return best;
        double Distance(PointF p) => Math.Pow(p.X - reference.X, 2) + Math.Pow(p.Y - reference.Y, 2);
        double distance = Distance(best);
        float radius = (float)Math.Sqrt(distance) + 1;
        // Exact bounded search also handles strongly skewed and unequal periods.
        foreach (var offset in Offsets(new(point.X, point.Y, 0.001f, 0.001f),
            new(reference.X - radius, reference.Y - radius, radius * 2, radius * 2)))
        {
            var candidate = new PointF(point.X + offset.X, point.Y + offset.Y);
            double next = Distance(candidate);
            if (next < distance) { best = candidate; distance = next; }
        }
        return best;
    }

    /// <summary>
    /// Enumerates every lattice translation whose translated content rectangle intersects a
    /// query rectangle.
    /// </summary>
    /// <param name="content">The canonical world-space bounds of the content.</param>
    /// <param name="query">The world-space region being queried.</param>
    /// <returns>The intersecting world-space translations, in pixels.</returns>
    /// <exception cref="InvalidOperationException">
    /// The requested region would produce more than one million candidate instances, or a
    /// non-finite candidate range.
    /// </exception>
    internal IEnumerable<PointF> Offsets(RectangleF content, RectangleF query)
    {
        // Minkowski difference bounds all translations whose content could intersect query.
        var corners = new[] {
            Coefficients(new(query.Left - content.Right, query.Top - content.Bottom)),
            Coefficients(new(query.Right - content.Left, query.Top - content.Bottom)),
            Coefficients(new(query.Left - content.Right, query.Bottom - content.Top)),
            Coefficients(new(query.Right - content.Left, query.Bottom - content.Top)) };
        double minX = WrapColumns ? Math.Ceiling(corners.Min(p => p.X)) : 0;
        double maxX = WrapColumns ? Math.Floor(corners.Max(p => p.X)) : 0;
        double minY = WrapRows ? Math.Ceiling(corners.Min(p => p.Y)) : 0;
        double maxY = WrapRows ? Math.Floor(corners.Max(p => p.Y)) : 0;
        double count = Math.Max(0, maxX - minX + 1) * Math.Max(0, maxY - minY + 1);
        if (!double.IsFinite(count) || count > 1_000_000 || Math.Abs(minX) > int.MaxValue || Math.Abs(maxX) > int.MaxValue || Math.Abs(minY) > int.MaxValue || Math.Abs(maxY) > int.MaxValue)
            throw new InvalidOperationException("The requested wrapped region exceeds the supported instance range.");
        for (double x = minX; x <= maxX; x++)
        for (double y = minY; y <= maxY; y++)
        {
            var offset = Offset(x, y);
            var translated = content;
            translated.Offset(offset);
            if (translated.IntersectsWith(query))
                yield return offset;
        }
    }
}
