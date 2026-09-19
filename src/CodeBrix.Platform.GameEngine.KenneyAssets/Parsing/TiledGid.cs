namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Takes a Tiled global tile id apart. Tiled packs three flip flags into the high bits of every cell
/// value, so the raw number in a layer's data is not the tile id on its own.
/// </summary>
internal static class TiledGid
{
    /// <summary>
    /// The bit Tiled sets when a tile is mirrored left to right.
    /// </summary>
    public const uint FlipHorizontallyFlag = 0x80000000u;

    /// <summary>
    /// The bit Tiled sets when a tile is mirrored top to bottom.
    /// </summary>
    public const uint FlipVerticallyFlag = 0x40000000u;

    /// <summary>
    /// The bit Tiled sets when a tile is mirrored along its main diagonal. Combined with the other two
    /// flags this expresses every 90-degree rotation.
    /// </summary>
    public const uint FlipDiagonallyFlag = 0x20000000u;

    /// <summary>
    /// The bits that hold the tile id itself.
    /// </summary>
    public const uint TileIdMask = 0x1FFFFFFFu;

    /// <summary>
    /// Gets the tile id a cell value refers to, with every flag removed.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns>The global tile id, where <c>0</c> means an empty cell.</returns>
    public static uint GetTileId(uint gid) => gid & TileIdMask;

    /// <summary>
    /// Gets just the flip flags of a cell value.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns>The flag bits, which is <c>0</c> for an unflipped tile.</returns>
    public static uint GetFlipFlags(uint gid) => gid & ~TileIdMask;

    /// <summary>
    /// Determines whether a cell is empty.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns><see langword="true"/> when the cell holds no tile; otherwise <see langword="false"/>.</returns>
    public static bool IsEmpty(uint gid) => GetTileId(gid) == 0u;

    /// <summary>
    /// Determines whether a cell's tile is mirrored left to right.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns><see langword="true"/> when the horizontal flip flag is set; otherwise <see langword="false"/>.</returns>
    public static bool IsFlippedHorizontally(uint gid) => (gid & FlipHorizontallyFlag) != 0u;

    /// <summary>
    /// Determines whether a cell's tile is mirrored top to bottom.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns><see langword="true"/> when the vertical flip flag is set; otherwise <see langword="false"/>.</returns>
    public static bool IsFlippedVertically(uint gid) => (gid & FlipVerticallyFlag) != 0u;

    /// <summary>
    /// Determines whether a cell's tile is mirrored along its main diagonal.
    /// </summary>
    /// <param name="gid">The raw cell value from a layer's data.</param>
    /// <returns><see langword="true"/> when the diagonal flip flag is set; otherwise <see langword="false"/>.</returns>
    public static bool IsFlippedDiagonally(uint gid) => (gid & FlipDiagonallyFlag) != 0u;
}
