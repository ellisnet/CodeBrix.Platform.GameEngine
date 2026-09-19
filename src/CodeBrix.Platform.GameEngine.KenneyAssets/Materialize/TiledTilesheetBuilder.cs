using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Turns the tile sets a Tiled map references into registered engine tilesheets: it resolves each
/// tile set document and image, bakes the flipped variants the map asks for into the sheet's bitmap,
/// and adds one uniform-grid region per variant.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves one map import, because it keeps the tile set names it has already handed out
/// so that two tile sets with the same name inside one map still get separate registry keys.
/// </para>
/// <para>
/// A sheet is keyed <c>&lt;map key&gt;#&lt;tile set name&gt;</c>. When that key is already registered
/// the existing sheet is adopted as it stands: nothing is decoded and no region is added, because a
/// tilesheet's regions are slices of one bitmap and that bitmap is fixed once it is registered.
/// </para>
/// </remarks>
internal sealed class TiledTilesheetBuilder
{
    private static readonly SKSamplingOptions NearestNeighbour = new(SKFilterMode.Nearest);

    private readonly IKenneyArchive _archive;
    private readonly TiledMapDocument _map;
    private readonly string _mapKey;
    private readonly List<string> _warnings;
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="TiledTilesheetBuilder"/> class.
    /// </summary>
    /// <param name="archive">The archive the map and its tile sets live in.</param>
    /// <param name="map">The parsed map document.</param>
    /// <param name="mapKey">The registry key of the map asset, which every sheet key is built from.</param>
    /// <param name="warnings">The import's warning list, added to as tile sets are prepared.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TiledTilesheetBuilder(
        IKenneyArchive archive, TiledMapDocument map, string mapKey, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(mapKey);
        ArgumentNullException.ThrowIfNull(warnings);

        _archive = archive;
        _map = map;
        _mapKey = mapKey;
        _warnings = warnings;
    }

    /// <summary>
    /// Resolves and registers every tile set the map references, in document order.
    /// </summary>
    /// <returns>The prepared tile sets, ordered as the map declares them.</returns>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when a tile set uses something the engine's tilesheets cannot represent: one image per
    /// tile, or tiles smaller than the map's grid cell.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when a tile set document or a tile set image is not in the archive; the message names the
    /// path that could not be resolved.
    /// </exception>
    /// <exception cref="TiledMapParseException">
    /// Thrown when a tile set document's declared grid does not fit the image it names.
    /// </exception>
    /// <exception cref="InvalidDataException">Thrown when a tile set image cannot be decoded.</exception>
    public IReadOnlyList<TiledImportedTileset> BuildAll()
    {
        List<TilesetPlan> plans = [];

        for (int index = 0; index < _map.Tilesets.Count; index++)
        {
            plans.Add(Plan(_map.Tilesets[index], index));
        }

        IReadOnlyList<IReadOnlyList<uint>> flipCombinations = CollectFlipCombinations(plans);
        List<TiledImportedTileset> tilesets = [];

        for (int index = 0; index < plans.Count; index++)
        {
            tilesets.Add(Build(plans[index], flipCombinations[index]));
        }

        return tilesets;
    }

    private TilesetPlan Plan(TiledTilesetReference reference, int index)
    {
        bool isInline = reference.Inline is not null;
        TiledTilesetDocument document = reference.Inline ?? ReadExternal(reference.Source);
        string label = Label(document, isInline, index);

        if (document.IsImageCollection)
        {
            throw new UnsupportedGameAssetException(
                $"{label} holds one image per tile (an image-collection tile set), which cannot be " +
                "imported: an engine tilesheet cuts its tiles from one grid image.");
        }

        if (document.ImagePath.Length == 0 || document.Columns <= 0)
        {
            throw new UnsupportedGameAssetException(
                $"{label} names no grid image with a column count, so its tiles cannot be located.");
        }

        if (document.TileWidth < _map.TileWidth || document.TileHeight < _map.TileHeight)
        {
            throw new UnsupportedGameAssetException(
                $"{label} has {document.TileWidth} by {document.TileHeight} pixel tiles, which are " +
                $"smaller than the map's {_map.TileWidth} by {_map.TileHeight} pixel grid cell; a tile " +
                "set smaller than the grid cannot be imported.");
        }

        int shapes = document.Tiles.Count(tile => tile.HasCollisionShapes);

        if (shapes > 0)
        {
            AddWarning(
                $"{label} gives {shapes} of its tiles their own collision shapes; an engine tile " +
                "collides as a rectangle, so the shapes were not imported.");
        }

        //An inline tile set's image is written relative to the map, an external one's relative to its
        //  own document. Resolution is strict on purpose: a bundle can hold several same-named images
        //  and guessing one would put the wrong art on the map.
        string basePath = isInline ? _map.DocumentPath : document.DocumentPath;
        string imagePath = _archive.ResolveDependencyPath(basePath, document.ImagePath)
            ?? throw new FileNotFoundException(
                $"{label} names the image '{document.ImagePath}', which is not in " +
                $"'{_archive.SourcePath}' relative to '{basePath}' or any folder above it.",
                document.ImagePath);

        return new TilesetPlan(reference.FirstGid, document, label, imagePath, TakeName(document, isInline, index));
    }

    private TiledTilesetDocument ReadExternal(string? source)
    {
        string path = _archive.ResolveDependencyPath(_map.DocumentPath, source)
            ?? throw new FileNotFoundException(
                $"Tiled map '{_map.DocumentPath}' references the tile set '{source}', which is not in " +
                $"'{_archive.SourcePath}' relative to the map or any folder above it.",
                source ?? string.Empty);

        return TiledMapParser.ParseTileset(_archive.ReadText(path), path);
    }

    private string TakeName(TiledTilesetDocument document, bool isInline, int index)
    {
        string candidate = document.Name;

        if (candidate.Length == 0 && !isInline)
        {
            candidate = KenneyArchivePath.GetFileStem(document.DocumentPath);
        }

        if (candidate.Length == 0)
        {
            candidate = $"tileset-{index}";
        }

        string name = candidate;

        for (int suffix = 2; !_usedNames.Add(name); suffix++)
        {
            name = $"{candidate}-{suffix}";
        }

        return name;
    }

    private static string Label(TiledTilesetDocument document, bool isInline, int index)
    {
        if (isInline)
        {
            return document.Name.Length > 0
                ? $"The inline Tiled tile set '{document.Name}'"
                : $"The inline Tiled tile set at position {index}";
        }

        return $"Tiled tile set '{document.DocumentPath}'";
    }

    //The flip combinations a map uses are read from EVERY tile layer, not only the layers being
    //  imported, so that the regions a sheet carries depend on the map alone. A later import of the
    //  same map through a different layer filter then finds the variants it needs already baked.
    private IReadOnlyList<IReadOnlyList<uint>> CollectFlipCombinations(IReadOnlyList<TilesetPlan> plans)
    {
        HashSet<uint>[] used = [.. plans.Select(_ => new HashSet<uint>())];

        foreach (TiledTileLayer layer in _map.TileLayers)
        {
            foreach (uint gid in layer.Gids)
            {
                if (TiledGid.IsEmpty(gid)) { continue; }

                uint flipFlags = TiledGid.GetFlipFlags(gid);
                if (flipFlags == 0u) { continue; }

                int owner = FindOwner(plans, TiledGid.GetTileId(gid));
                if (owner < 0) { continue; }

                if (!IsSquare(plans[owner].Document)
                    && (flipFlags & TiledGid.FlipDiagonallyFlag) != 0u)
                {
                    AddWarning(
                        $"{plans[owner].Label} has tiles that are not square, so the diagonal flip of " +
                        "one or more of its cells was dropped; the tile is placed without it.");
                    flipFlags &= ~TiledGid.FlipDiagonallyFlag;
                }

                if (flipFlags != 0u) { used[owner].Add(flipFlags); }
            }
        }

        return [.. used.Select(TiledFlipVariants.Order)];
    }

    private static int FindOwner(IReadOnlyList<TilesetPlan> plans, uint tileId)
    {
        int owner = -1;

        for (int index = 0; index < plans.Count; index++)
        {
            if (plans[index].FirstGid <= tileId && (owner < 0 || plans[index].FirstGid > plans[owner].FirstGid))
            {
                owner = index;
            }
        }

        return owner;
    }

    private TiledImportedTileset Build(TilesetPlan plan, IReadOnlyList<uint> flipCombinations)
    {
        return TilesheetRegistry.Instance.TryGet(plan.SheetKey(_mapKey), out Tilesheet? existing)
            && existing is not null
                ? Adopt(plan, flipCombinations, existing)
                : Create(plan, flipCombinations);
    }

    private TiledImportedTileset Adopt(
        TilesetPlan plan, IReadOnlyList<uint> flipCombinations, Tilesheet sheet)
    {
        string sheetKey = plan.SheetKey(_mapKey);
        Dictionary<uint, string> regionNames = [];

        foreach (uint flipFlags in Combinations(flipCombinations))
        {
            string regionName = TiledFlipVariants.GetRegionName(flipFlags);

            if (sheet.GetRegion(regionName) is null)
            {
                AddWarning(
                    $"The tilesheet already registered as '{sheetKey}' carries no region " +
                    $"'{regionName}', so the map's cells that need it were left empty.");
                continue;
            }

            regionNames[flipFlags] = regionName;
        }

        TilesheetRegion? baseRegion = sheet.GetRegion(TiledFlipVariants.BaseRegionName);
        int columns = baseRegion is { Columns: > 0 } ? baseRegion.Columns : plan.Document.Columns;

        if (baseRegion is not null && columns != plan.Document.Columns)
        {
            AddWarning(
                $"The tilesheet already registered as '{sheetKey}' lays its tiles out in {columns} " +
                $"columns, while {plan.Label} declares {plan.Document.Columns}; the registered sheet " +
                "decides where a tile is.");
        }

        int rows = baseRegion is { Rows: > 0 } ? baseRegion.Rows : 1;
        int tileCount = plan.Document.TileCount > 0
            ? Math.Min(plan.Document.TileCount, columns * rows)
            : columns * rows;

        return new TiledImportedTileset(
            plan.FirstGid,
            plan.Name,
            sheetKey,
            plan.Document,
            sheet,
            columns,
            tileCount,
            baseRegion?.Overhang ?? Spacing.None,
            IsSquare(plan.Document),
            regionNames,
            TilesById(plan.Document));
    }

    private TiledImportedTileset Create(TilesetPlan plan, IReadOnlyList<uint> flipCombinations)
    {
        TiledTilesetDocument document = plan.Document;
        string sheetKey = plan.SheetKey(_mapKey);
        SKBitmap source = Decode(plan);
        SKBitmap sheetBitmap;
        TilesetGrid grid;

        try
        {
            grid = MeasureGrid(plan, source);
            sheetBitmap = flipCombinations.Count == 0
                ? source
                : BakeVariants(plan, source, grid, flipCombinations);
        }
        catch
        {
            source.Dispose();
            throw;
        }

        if (!ReferenceEquals(sheetBitmap, source)) { source.Dispose(); }

        Tilesheet sheet = TilesheetRegistry.Instance.LoadFromBitmap(sheetKey, sheetBitmap);
        Size tileSize = new(document.TileWidth, document.TileHeight);
        Spacing padding = new(0, 0, document.Spacing, document.Spacing);
        Spacing margin = new(document.Margin, document.Margin, 0, 0);
        Spacing overhang = MeasureOverhang(plan);
        Dictionary<uint, string> regionNames = [];
        int block = 0;

        foreach (uint flipFlags in Combinations(flipCombinations))
        {
            string regionName = TiledFlipVariants.GetRegionName(flipFlags);

            sheet.AddRegion(
                regionName,
                new Rectangle(0, block * grid.BlockStride, grid.BlockWidth, grid.BlockHeight),
                tileSize,
                padding,
                margin,
                overhang);

            regionNames[flipFlags] = regionName;
            block++;
        }

        return new TiledImportedTileset(
            plan.FirstGid,
            plan.Name,
            sheetKey,
            document,
            sheet,
            grid.Columns,
            grid.TileCount,
            overhang,
            IsSquare(document),
            regionNames,
            TilesById(document));
    }

    private SKBitmap Decode(TilesetPlan plan)
    {
        using Stream stream = _archive.Open(plan.ImagePath);

        return SKBitmap.Decode(stream)
            ?? throw new InvalidDataException(
                $"{plan.Label} names the image '{plan.ImagePath}', which could not be decoded.");
    }

    //The engine slices a region as margin, then one tile plus its padding at a time, so Tiled's
    //  margin becomes the region's left and top margin and Tiled's spacing becomes each tile's right
    //  and bottom padding. The region area is one spacing wider and taller than the tile grid itself,
    //  because the engine divides the area by the padded tile size and the last tile of a row has no
    //  trailing gap in the image; the area may therefore reach past the image, which costs nothing
    //  since every tile rectangle is still inside it.
    private TilesetGrid MeasureGrid(TilesetPlan plan, SKBitmap source)
    {
        TiledTilesetDocument document = plan.Document;
        int columns = document.Columns;
        int pitchX = document.TileWidth + document.Spacing;
        int pitchY = document.TileHeight + document.Spacing;
        int rows;
        int tileCount;

        if (document.TileCount > 0)
        {
            tileCount = document.TileCount;
            rows = ((tileCount - 1) / columns) + 1;
        }
        else
        {
            rows = Math.Max(1, (source.Height - document.Margin + document.Spacing) / pitchY);
            tileCount = columns * rows;
        }

        int neededWidth = document.Margin + (columns * document.TileWidth)
            + ((columns - 1) * document.Spacing);
        int neededHeight = document.Margin + (rows * document.TileHeight)
            + ((rows - 1) * document.Spacing);

        if (source.Width < neededWidth || source.Height < neededHeight)
        {
            throw new TiledMapParseException(
                $"{plan.Label} declares {tileCount} tiles of {document.TileWidth} by " +
                $"{document.TileHeight} pixels in {columns} columns, which needs at least " +
                $"{neededWidth} by {neededHeight} pixels, but its image '{plan.ImagePath}' is " +
                $"{source.Width} by {source.Height} pixels.",
                document.DocumentPath);
        }

        if ((document.ImageWidth > 0 && document.ImageWidth != source.Width)
            || (document.ImageHeight > 0 && document.ImageHeight != source.Height))
        {
            AddWarning(
                $"{plan.Label} declares its image as {document.ImageWidth} by {document.ImageHeight} " +
                $"pixels, but '{plan.ImagePath}' is {source.Width} by {source.Height} pixels; the " +
                "file decides.");
        }

        int blockWidth = document.Margin + (columns * pitchX);
        int blockHeight = document.Margin + (rows * pitchY);

        return new TilesetGrid(
            columns,
            rows,
            tileCount,
            blockWidth,
            blockHeight,
            Math.Max(source.Height, blockHeight));
    }

    //Tiled anchors a tile that is larger than the grid cell to the cell's bottom left corner, and
    //  <tileoffset> shifts it from there. The engine expresses that as region overhang, which grows
    //  the drawn rectangle outwards from the cell on each side.
    private Spacing MeasureOverhang(TilesetPlan plan)
    {
        TiledTilesetDocument document = plan.Document;
        int extraWidth = document.TileWidth - _map.TileWidth;
        int extraHeight = document.TileHeight - _map.TileHeight;
        int left = -document.TileOffsetX;
        int right = extraWidth + document.TileOffsetX;
        int top = extraHeight - document.TileOffsetY;
        int bottom = document.TileOffsetY;

        if (left < 0 || right < 0 || top < 0 || bottom < 0)
        {
            AddWarning(
                $"{plan.Label} has a tile offset of {document.TileOffsetX}, {document.TileOffsetY} " +
                "pixels that would place its tiles outside the cell on the opposite side; the offset " +
                "was clamped, so those tiles sit at the edge of their cell instead.");

            left = Math.Max(0, left);
            right = Math.Max(0, right);
            top = Math.Max(0, top);
            bottom = Math.Max(0, bottom);
        }

        return new Spacing(left, top, right, bottom);
    }

    private static SKBitmap BakeVariants(
        TilesetPlan plan, SKBitmap source, TilesetGrid grid, IReadOnlyList<uint> flipCombinations)
    {
        TiledTilesetDocument document = plan.Document;
        SKImageInfo info = new(
            Math.Max(source.Width, grid.BlockWidth),
            grid.BlockStride * (flipCombinations.Count + 1),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        SKBitmap composite = new(info);

        try
        {
            using SKCanvas canvas = new(composite);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0f, 0f, NearestNeighbour);

            int pitchX = document.TileWidth + document.Spacing;
            int pitchY = document.TileHeight + document.Spacing;

            for (int block = 0; block < flipCombinations.Count; block++)
            {
                int blockTop = (block + 1) * grid.BlockStride;
                uint flipFlags = flipCombinations[block];

                for (int tile = 0; tile < grid.TileCount; tile++)
                {
                    int column = tile % grid.Columns;
                    int row = tile / grid.Columns;
                    int sourceX = document.Margin + (column * pitchX);
                    int sourceY = document.Margin + (row * pitchY);
                    SKRect sourceRect = SKRect.Create(
                        sourceX, sourceY, document.TileWidth, document.TileHeight);
                    SKRect destination = SKRect.Create(
                        document.TileWidth / -2f,
                        document.TileHeight / -2f,
                        document.TileWidth,
                        document.TileHeight);

                    canvas.Save();
                    canvas.SetMatrix(TiledFlipVariants.GetTileMatrix(
                        flipFlags,
                        sourceX + (document.TileWidth / 2f),
                        blockTop + sourceY + (document.TileHeight / 2f)));
                    canvas.DrawBitmap(source, sourceRect, destination, NearestNeighbour, null);
                    canvas.Restore();
                }
            }

            return composite;
        }
        catch
        {
            composite.Dispose();
            throw;
        }
    }

    private static IEnumerable<uint> Combinations(IReadOnlyList<uint> flipCombinations) =>
        Enumerable.Repeat(0u, 1).Concat(flipCombinations);

    private static bool IsSquare(TiledTilesetDocument document) =>
        document.TileWidth == document.TileHeight;

    private static IReadOnlyDictionary<int, TiledTilesetTile> TilesById(TiledTilesetDocument document)
    {
        Dictionary<int, TiledTilesetTile> tiles = [];

        foreach (TiledTilesetTile tile in document.Tiles)
        {
            tiles[tile.Id] = tile;
        }

        return tiles;
    }

    private void AddWarning(string message)
    {
        if (!_warnings.Contains(message, StringComparer.Ordinal))
        {
            _warnings.Add(message);
        }
    }

    private sealed record TilesetPlan(
        int FirstGid, TiledTilesetDocument Document, string Label, string ImagePath, string Name)
    {
        public string SheetKey(string mapKey) => TiledMapImporter.GetTilesheetKey(mapKey, Name);
    }

    private readonly record struct TilesetGrid(
        int Columns, int Rows, int TileCount, int BlockWidth, int BlockHeight, int BlockStride);
}
