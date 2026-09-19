using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// One tile set of an imported map after it has become an engine tilesheet: the document it came
/// from, the sheet it was registered as, and everything the importer needs to turn a global tile id
/// into a <see cref="Frame"/>.
/// </summary>
internal sealed class TiledImportedTileset
{
    private readonly IReadOnlyDictionary<uint, string> _regionNames;
    private readonly IReadOnlyDictionary<int, TiledTilesetTile> _tiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="TiledImportedTileset"/> class.
    /// </summary>
    /// <param name="firstGid">The global tile id of the tile set's first tile.</param>
    /// <param name="name">The tile set's name as the importer settled on it, unique within the map.</param>
    /// <param name="sheetKey">The registry key the tilesheet was registered under.</param>
    /// <param name="document">The parsed tile set document.</param>
    /// <param name="sheet">The registered tilesheet.</param>
    /// <param name="columns">The number of tile columns of the tile set grid.</param>
    /// <param name="tileCount">The number of tiles the tile set holds.</param>
    /// <param name="overhang">The overhang the importer gave the tile set's regions.</param>
    /// <param name="supportsDiagonalFlip">Whether a diagonal flip can be baked, which needs square tiles.</param>
    /// <param name="regionNames">The region name of each flip combination the sheet carries, keyed by flip flags.</param>
    /// <param name="tiles">What the document declares about individual tiles, keyed by local tile id.</param>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is null.</exception>
    public TiledImportedTileset(
        int firstGid,
        string name,
        string sheetKey,
        TiledTilesetDocument document,
        Tilesheet sheet,
        int columns,
        int tileCount,
        Spacing overhang,
        bool supportsDiagonalFlip,
        IReadOnlyDictionary<uint, string> regionNames,
        IReadOnlyDictionary<int, TiledTilesetTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sheetKey);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(regionNames);
        ArgumentNullException.ThrowIfNull(tiles);

        FirstGid = firstGid;
        Name = name;
        SheetKey = sheetKey;
        Document = document;
        Sheet = sheet;
        Columns = columns;
        TileCount = tileCount;
        Overhang = overhang;
        SupportsDiagonalFlip = supportsDiagonalFlip;
        _regionNames = regionNames;
        _tiles = tiles;
    }

    /// <summary>
    /// Gets the global tile id of the tile set's first tile.
    /// </summary>
    public int FirstGid { get; }

    /// <summary>
    /// Gets the tile set's name as the importer settled on it: the document's own name where it has
    /// one, made unique within the map.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the key the tilesheet is registered under in the engine's tilesheet registry.
    /// </summary>
    public string SheetKey { get; }

    /// <summary>
    /// Gets the parsed tile set document.
    /// </summary>
    public TiledTilesetDocument Document { get; }

    /// <summary>
    /// Gets the registered tilesheet holding the tile set's grid and its baked flipped variants.
    /// </summary>
    public Tilesheet Sheet { get; }

    /// <summary>
    /// Gets the number of tile columns of the tile set grid.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// Gets the number of tiles the tile set holds.
    /// </summary>
    public int TileCount { get; }

    /// <summary>
    /// Gets the overhang applied to every region of the sheet, which anchors a tile larger than the
    /// map's grid cell to the cell's bottom left corner the way Tiled draws it.
    /// </summary>
    public Spacing Overhang { get; }

    /// <summary>
    /// Gets a value indicating whether a diagonally flipped variant of this tile set can be baked. A
    /// diagonal flip transposes a tile, which only keeps its size when the tile is square.
    /// </summary>
    public bool SupportsDiagonalFlip { get; }

    /// <summary>
    /// Gets a value indicating whether a local tile id belongs to this tile set.
    /// </summary>
    /// <param name="localTileId">The tile's identifier within this tile set.</param>
    /// <returns><see langword="true"/> when the tile set holds that tile.</returns>
    public bool ContainsTile(int localTileId) => localTileId >= 0 && localTileId < TileCount;

    /// <summary>
    /// Gets the column of a tile within the tile set grid.
    /// </summary>
    /// <param name="localTileId">The tile's identifier within this tile set.</param>
    /// <returns>The zero-based column.</returns>
    public int GetColumn(int localTileId) => localTileId % Columns;

    /// <summary>
    /// Gets the row of a tile within the tile set grid.
    /// </summary>
    /// <param name="localTileId">The tile's identifier within this tile set.</param>
    /// <returns>The zero-based row.</returns>
    public int GetRow(int localTileId) => localTileId / Columns;

    /// <summary>
    /// Reduces a cell's flip flags to the ones this tile set can reproduce.
    /// </summary>
    /// <param name="flipFlags">The flip flags the map's cell carries.</param>
    /// <returns>
    /// The flags unchanged, or the flags without the diagonal bit when the tile set's tiles are not
    /// square. The importer reports the dropped bit once per tile set.
    /// </returns>
    public uint MapFlipFlags(uint flipFlags) =>
        SupportsDiagonalFlip ? flipFlags : flipFlags & ~TiledGid.FlipDiagonallyFlag;

    /// <summary>
    /// Gets the name of the tilesheet region a flip combination is baked into.
    /// </summary>
    /// <param name="flipFlags">The flip flags, already passed through <see cref="MapFlipFlags"/>.</param>
    /// <param name="regionName">The region's name when the method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the sheet carries that variant; <see langword="false"/> when it
    /// does not, which happens only when the sheet was already registered under
    /// <see cref="SheetKey"/> by something else.
    /// </returns>
    public bool TryGetRegionName(uint flipFlags, [NotNullWhen(true)] out string? regionName) =>
        _regionNames.TryGetValue(flipFlags, out regionName);

    /// <summary>
    /// Gets what the document declares about one tile.
    /// </summary>
    /// <param name="localTileId">The tile's identifier within this tile set.</param>
    /// <returns>The tile's declaration, or <see langword="null"/> when the document says nothing about it.</returns>
    public TiledTilesetTile? GetTile(int localTileId) =>
        _tiles.TryGetValue(localTileId, out TiledTilesetTile? tile) ? tile : null;
}
