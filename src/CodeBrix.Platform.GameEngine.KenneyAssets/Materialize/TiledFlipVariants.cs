using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// The flipped tile variants a Tiled map can ask for, and the two things the importer needs for each
/// of them: the tilesheet region name it is baked into, and the transform that bakes it.
/// </summary>
/// <remarks>
/// <para>
/// A Tiled cell carries three flip bits; an engine tilesheet frame has no flip of its own. The
/// importer therefore bakes a flipped copy of a tile set's whole grid for every flip combination the
/// map actually uses, as an extra region of the same tilesheet, and assigns cells from the region
/// that matches their bits. The names are <c>tiles</c> for the unflipped grid and
/// <c>tiles-f</c> followed by <c>h</c>, <c>v</c> and <c>d</c> in that order for a flipped one, so a
/// horizontally and vertically flipped grid is <c>tiles-fhv</c>.
/// </para>
/// <para>
/// The diagonal bit is a transpose, applied before the horizontal and vertical flips, which is the
/// rule Tiled itself uses.
/// </para>
/// </remarks>
internal static class TiledFlipVariants
{
    /// <summary>
    /// The name of the region holding a tile set's grid as its image stores it.
    /// </summary>
    public const string BaseRegionName = "tiles";

    /// <summary>
    /// The prefix a flipped variant's region name carries after <see cref="BaseRegionName"/>.
    /// </summary>
    private const string FlipSuffixPrefix = "-f";

    //(x, y) -> (y, x): the transpose the diagonal flip bit means. Written as an explicit matrix
    //  rather than a 90 degree rotation so every value is exact and a baked tile cannot land half a
    //  pixel off its slot.
    private static readonly SKMatrix Transpose = new(0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);

    /// <summary>
    /// Gets the name of the tilesheet region a tile with the given flip flags is assigned from.
    /// </summary>
    /// <param name="flipFlags">The Tiled flip flags, as <see cref="TiledGid.GetFlipFlags"/> returns them.</param>
    /// <returns><see cref="BaseRegionName"/> when no flag is set; otherwise the variant's region name.</returns>
    public static string GetRegionName(uint flipFlags)
    {
        if (flipFlags == 0u) { return BaseRegionName; }

        StringBuilder name = new(BaseRegionName);
        name.Append(FlipSuffixPrefix);

        if ((flipFlags & TiledGid.FlipHorizontallyFlag) != 0u) { name.Append('h'); }
        if ((flipFlags & TiledGid.FlipVerticallyFlag) != 0u) { name.Append('v'); }
        if ((flipFlags & TiledGid.FlipDiagonallyFlag) != 0u) { name.Append('d'); }

        return name.ToString();
    }

    /// <summary>
    /// Orders a set of flip combinations so that the same map always produces the same regions in the
    /// same places of the tilesheet bitmap.
    /// </summary>
    /// <param name="flipFlags">The flip combinations used by a map, in any order.</param>
    /// <returns>The combinations, ascending by flag value, with the unflipped combination removed.</returns>
    public static IReadOnlyList<uint> Order(IEnumerable<uint> flipFlags) =>
        [.. flipFlags.Where(f => f != 0u).Distinct().OrderBy(f => f)];

    /// <summary>
    /// Builds the transform that draws one source tile into a baked variant slot.
    /// </summary>
    /// <param name="flipFlags">The flip combination being baked.</param>
    /// <param name="centerX">The horizontal centre of the destination slot, in bitmap pixels.</param>
    /// <param name="centerY">The vertical centre of the destination slot, in bitmap pixels.</param>
    /// <returns>
    /// The matrix to set on the canvas before drawing the tile into a rectangle centred on the origin.
    /// </returns>
    public static SKMatrix GetTileMatrix(uint flipFlags, float centerX, float centerY)
    {
        SKMatrix matrix = SKMatrix.CreateTranslation(centerX, centerY);

        float scaleX = (flipFlags & TiledGid.FlipHorizontallyFlag) != 0u ? -1f : 1f;
        float scaleY = (flipFlags & TiledGid.FlipVerticallyFlag) != 0u ? -1f : 1f;
        matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(scaleX, scaleY));

        if ((flipFlags & TiledGid.FlipDiagonallyFlag) != 0u)
        {
            matrix = SKMatrix.Concat(matrix, Transpose);
        }

        return matrix;
    }
}
