using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Imports a Tiled map asset of a Kenney bundle into a <see cref="Scene"/>: one
/// <see cref="SceneLayer"/> per tile layer, one registered <see cref="Tilesheet"/> per tile set, and
/// the map's object layers handed back as data.
/// </summary>
/// <remarks>
/// <para>
/// What is imported: orthogonal, finite maps whose layer data is CSV or base64 (uncompressed, zlib or
/// gzip); external and inline tile sets that cut their tiles from one grid image. Everything else is
/// refused with a message naming the feature - an isometric, hexagonal, staggered or infinite map, a
/// tile set that holds one image per tile, and a tile set whose tiles are smaller than the map's grid
/// cell.
/// </para>
/// <para>
/// Flip bits are pre-baked. An engine tilesheet frame has no flip of its own, so for every flip
/// combination a map uses the tile set's whole grid is baked again into the same tilesheet bitmap as an
/// extra region (see <see cref="TiledFlipVariants"/>) and the flipped cells are assigned from it. A
/// tile set with tiles larger than the grid cell - Kenney's 24 pixel characters on an 18 pixel map -
/// keeps Tiled's bottom-left anchoring through its regions' overhang, shifted by
/// <c>&lt;tileoffset&gt;</c>.
/// </para>
/// <para>
/// Collision comes from the tilesheet rather than from each tile: the collision type a tile takes is
/// recorded on the frame it is assigned, and a tile inherits its first frame's collision type. A
/// <see cref="TiledMapImportOptions.CollisionSelector"/> that answers differently for two cells sharing
/// one tile is still honoured - the second cell gets an explicit tile-level type.
/// </para>
/// <para>
/// Repeating an import is only half idempotent, by design: a tilesheet already registered under its
/// key is adopted rather than rebuilt, but layers are always added to the scene the caller passed, so
/// importing the same map into the same scene twice gives that scene two sets of layers.
/// </para>
/// </remarks>
internal sealed class TiledMapImporter
{
    /// <summary>
    /// The character between a map's key and a tile set's name in the key of a tilesheet the import
    /// registers. It is not a legal character in a Kenney asset key, so a tilesheet key can never
    /// collide with an asset key.
    /// </summary>
    public const char TilesheetKeySeparator = '#';

    //One import at a time. Two threads importing maps that share a tile set key would otherwise both
    //  find the key unregistered, both bake a sheet, and the second registration would dispose the
    //  first sheet while the first map's tiles were still pointing at it.
    private readonly object _gate = new();

    /// <summary>
    /// Builds the registry key an imported map's tile set is registered as a tilesheet under.
    /// </summary>
    /// <param name="mapKey">The registry key of the map asset.</param>
    /// <param name="tilesetName">The tile set's name, as the import settled on it.</param>
    /// <returns>The tilesheet's key, of the form <c>&lt;map key&gt;#&lt;tile set name&gt;</c>.</returns>
    public static string GetTilesheetKey(string mapKey, string tilesetName) =>
        $"{mapKey}{TilesheetKeySeparator}{tilesetName}";

    /// <summary>
    /// Imports a tile map asset into a scene.
    /// </summary>
    /// <param name="entry">The catalogued map asset; its kind must be <see cref="GameAssetKind.TiledMap"/>.</param>
    /// <param name="key">
    /// The registry key the map answers to. Every tilesheet the import registers is keyed
    /// <c>&lt;key&gt;#&lt;tile set name&gt;</c>.
    /// </param>
    /// <param name="scene">The scene that receives one layer per tile layer of the map.</param>
    /// <param name="options">
    /// Z-order, parallax, collision and filtering options, or <see langword="null"/> for the defaults.
    /// </param>
    /// <returns>The layers, tilesheets, object data and warnings the import produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> or <paramref name="scene"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not a tile map, or when the map or one of its tile sets uses a feature
    /// the engine cannot represent.
    /// </exception>
    /// <exception cref="TiledMapParseException">
    /// Thrown when the map document cannot be read, or when a tile set's declared grid does not fit its
    /// image. The message names what was wrong.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when a tile set document or image cannot be resolved inside the bundle; the message names
    /// the path that could not be resolved.
    /// </exception>
    /// <exception cref="InvalidDataException">Thrown when a tile set image cannot be decoded.</exception>
    public TiledMapImport Import(
        KenneyAssetEntry entry, string key, Scene scene, TiledMapImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(scene);

        if (entry.Kind != GameAssetKind.TiledMap)
        {
            throw new UnsupportedGameAssetException(entry.Kind, key);
        }

        TiledMapDocument map = ReadMap(entry);

        lock (_gate)
        {
            return ImportCore(entry, key, scene, options ?? new TiledMapImportOptions(), map);
        }
    }

    private static TiledMapDocument ReadMap(KenneyAssetEntry entry)
    {
        if (entry.TiledMap is { } parsed) { return parsed; }

        //The pack index parses every .tmx while it catalogs, so a map that could not be read carries
        //  the reason. That message names the feature that was refused, which is what a caller needs.
        if (!string.IsNullOrEmpty(entry.TiledMapError))
        {
            throw new TiledMapParseException(entry.TiledMapError, entry.Path);
        }

        return TiledMapParser.ParseMap(entry.Pack.Archive.ReadText(entry.Path), entry.Path);
    }

    private static TiledMapImport ImportCore(
        KenneyAssetEntry entry,
        string key,
        Scene scene,
        TiledMapImportOptions options,
        TiledMapDocument map)
    {
        List<string> warnings = [];

        //Whatever the parser could not reproduce - an image layer, a layer group - is already a
        //  warning on the document, and it is the import's business too.
        foreach (string warning in map.Warnings)
        {
            AddWarning(warnings, warning);
        }

        TiledTilesheetBuilder builder = new(entry.Pack.Archive, map, key, warnings);
        IReadOnlyList<TiledImportedTileset> tilesets = builder.BuildAll();
        Dictionary<FrameIdentity, TileCollisionType> frameCollisionTypes = [];
        List<SceneLayer> layers = [];

        foreach (TiledTileLayer layer in map.TileLayers)
        {
            if (options.LayerFilter is not null && !options.LayerFilter(layer.Name)) { continue; }

            layers.Add(ImportLayer(scene, map, layer, tilesets, options, warnings, frameCollisionTypes));
        }

        return new TiledMapImport
        {
            Scene = scene,
            Layers = layers,
            Tilesheets = [.. tilesets.Select(tileset => tileset.Sheet)],
            ObjectGroups = options.ImportObjectLayers
                ? BuildObjectGroups(map, tilesets, warnings)
                : [],
            MapSizePx = new Size(map.Width * map.TileWidth, map.Height * map.TileHeight),
            TileSize = new Size(map.TileWidth, map.TileHeight),
            Warnings = warnings,
        };
    }

    private static SceneLayer ImportLayer(
        Scene scene,
        TiledMapDocument map,
        TiledTileLayer layer,
        IReadOnlyList<TiledImportedTileset> tilesets,
        TiledMapImportOptions options,
        List<string> warnings,
        Dictionary<FrameIdentity, TileCollisionType> frameCollisionTypes)
    {
        SceneLayer sceneLayer = scene.AddLayer(
            columnCount: map.Width,
            rowCount: map.Height,
            width: map.TileWidth,
            height: map.TileHeight,
            zOrder: options.ZOrderBase + layer.DocumentIndex,
            parallax: ResolveParallax(layer, options, warnings),
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        sceneLayer.Visible = layer.Visible && layer.Opacity > 0f;

        //A layer offset moves the layer's content; the engine's origin is the pixel the content is
        //  drawn back from, so it is the offset negated.
        if (layer.OffsetX != 0f || layer.OffsetY != 0f)
        {
            sceneLayer.OriginPx = new Point(
                -(int)MathF.Round(layer.OffsetX), -(int)MathF.Round(layer.OffsetY));
        }

        if (!string.IsNullOrWhiteSpace(options.CollisionProfileName))
        {
            sceneLayer.DefaultTileCollisionProfile = options.CollisionProfileName;
        }

        if (layer.Opacity <= 0f)
        {
            AddWarning(
                warnings,
                $"Layer '{layer.Name}' has an opacity of {layer.Opacity} in the map; the engine has no " +
                "per-layer opacity, so the layer was imported hidden.");
        }
        else if (layer.Opacity < 1f)
        {
            AddWarning(
                warnings,
                $"Layer '{layer.Name}' has an opacity of {layer.Opacity} in the map; the engine has no " +
                "per-layer opacity, so the layer was imported fully opaque.");
        }

        if (layer.Width != map.Width || layer.Height != map.Height)
        {
            AddWarning(
                warnings,
                $"Layer '{layer.Name}' is {layer.Width} by {layer.Height} tiles while the map's grid is " +
                $"{map.Width} by {map.Height}; only the cells inside the map's grid were imported.");
        }

        int columns = Math.Min(layer.Width, map.Width);
        int rows = Math.Min(layer.Height, map.Height);

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                AssignTile(
                    sceneLayer,
                    layer,
                    column,
                    row,
                    tilesets,
                    options,
                    warnings,
                    frameCollisionTypes);
            }
        }

        return sceneLayer;
    }

    private static void AssignTile(
        SceneLayer sceneLayer,
        TiledTileLayer layer,
        int column,
        int row,
        IReadOnlyList<TiledImportedTileset> tilesets,
        TiledMapImportOptions options,
        List<string> warnings,
        Dictionary<FrameIdentity, TileCollisionType> frameCollisionTypes)
    {
        uint cell = layer.Gids[(row * layer.Width) + column];

        if (TiledGid.IsEmpty(cell)) { return; }

        uint globalTileId = TiledGid.GetTileId(cell);
        TiledImportedTileset? tileset = FindTileset(tilesets, globalTileId);

        if (tileset is null)
        {
            AddWarning(
                warnings,
                $"Global tile id {globalTileId} belongs to none of the map's tile sets; every cell " +
                "holding it was left empty.");
            return;
        }

        int localTileId = (int)globalTileId - tileset.FirstGid;

        if (!tileset.ContainsTile(localTileId))
        {
            AddWarning(
                warnings,
                $"Global tile id {globalTileId} is past the end of tile set '{tileset.Name}', which " +
                $"holds {tileset.TileCount} tiles; every cell holding it was left empty.");
            return;
        }

        uint flipFlags = tileset.MapFlipFlags(TiledGid.GetFlipFlags(cell));

        if (!tileset.TryGetRegionName(flipFlags, out string? regionName))
        {
            //Adopt() has already said which region the registered sheet is missing.
            return;
        }

        SceneLayerTile? tile = sceneLayer[column, row];

        if (tile is null) { return; }

        int frameColumn = tileset.GetColumn(localTileId);
        int frameRow = tileset.GetRow(localTileId);
        FrameIdentity identity = new(tileset.SheetKey, flipFlags, localTileId);
        bool firstUse = !frameCollisionTypes.TryGetValue(
            identity, out TileCollisionType frameCollisionType);
        TileCollisionType cellCollisionType = frameCollisionType;

        if (firstUse || options.CollisionSelector is not null)
        {
            TiledTilesetTile? declared = tileset.GetTile(localTileId);
            TiledTileInfo info = new()
            {
                LayerName = layer.Name,
                Column = column,
                Row = row,
                GlobalTileId = (int)globalTileId,
                LocalTileId = localTileId,
                TilesetName = tileset.Name,
                TileType = declared?.Type,
                TileProperties = declared?.Properties ?? TiledProperties.Empty,
                TilesetProperties = tileset.Document.Properties,
                FlippedHorizontally = TiledGid.IsFlippedHorizontally(cell),
                FlippedVertically = TiledGid.IsFlippedVertically(cell),
                FlippedDiagonally = TiledGid.IsFlippedDiagonally(cell),
            };

            cellCollisionType = options.CollisionSelector is { } selector
                ? selector(info)
                : TiledCollisionDefaults.Resolve(info);
        }

        if (firstUse)
        {
            //The first cell to use a frame decides what the frame itself collides as, so every other
            //  cell using it inherits that with no per-tile work at all.
            frameCollisionType = cellCollisionType;
            frameCollisionTypes[identity] = frameCollisionType;

            if (frameCollisionType != TileCollisionType.None)
            {
                tileset.Sheet.GetRegion(regionName)
                    ?.SetFrameCollisionType(frameColumn, frameRow, frameCollisionType);
            }
        }

        tile.CurrentFrame = new Frame(tileset.Sheet, regionName, frameColumn, frameRow);

        //The frame has already given the tile its collision type; only a cell whose selector answered
        //  differently from its own frame needs an explicit tile-level value.
        if (cellCollisionType != frameCollisionType)
        {
            tile.CollisionType = cellCollisionType;
        }
    }

    private static TiledImportedTileset? FindTileset(
        IReadOnlyList<TiledImportedTileset> tilesets, uint globalTileId)
    {
        TiledImportedTileset? owner = null;

        foreach (TiledImportedTileset tileset in tilesets)
        {
            if (tileset.FirstGid <= globalTileId && (owner is null || tileset.FirstGid > owner.FirstGid))
            {
                owner = tileset;
            }
        }

        return owner;
    }

    private static float ResolveParallax(
        TiledTileLayer layer, TiledMapImportOptions options, List<string> warnings)
    {
        if (layer.ParallaxX != layer.ParallaxY)
        {
            AddWarning(
                warnings,
                $"Layer '{layer.Name}' scrolls at {layer.ParallaxX} horizontally and " +
                $"{layer.ParallaxY} vertically; the engine has one parallax factor per layer, so the " +
                "horizontal one was used.");
        }

        //A layer that says nothing about parallax takes the caller's value; one that does keeps its own.
        return layer.ParallaxX == 1f && layer.ParallaxY == 1f ? options.Parallax : layer.ParallaxX;
    }

    private static IReadOnlyList<TiledObjectGroup> BuildObjectGroups(
        TiledMapDocument map, IReadOnlyList<TiledImportedTileset> tilesets, List<string> warnings)
    {
        List<TiledObjectGroup> groups = [];

        foreach (TiledObjectLayer layer in map.ObjectLayers)
        {
            groups.Add(new TiledObjectGroup
            {
                Name = layer.Name,
                Objects =
                [
                    .. layer.Objects.Select(mapObject => new TiledObject
                    {
                        Id = mapObject.Id,
                        Name = mapObject.Name,
                        Type = mapObject.Type,
                        Bounds = new RectangleF(
                            mapObject.X, mapObject.Y, mapObject.Width, mapObject.Height),
                        Rotation = mapObject.Rotation,
                        Visible = mapObject.Visible,
                        Tile = BuildObjectTileInfo(layer, mapObject, tilesets, warnings),
                        Properties = mapObject.Properties,
                    }),
                ],
                Offset = new PointF(layer.OffsetX, layer.OffsetY),
                Visible = layer.Visible,
                Opacity = layer.Opacity,
                DocumentIndex = layer.DocumentIndex,
                Properties = layer.Properties,
            });
        }

        return groups;
    }

    //A tile object names a tile of one of the map's tile sets; a shape, point or text object does not
    private static TiledTileInfo? BuildObjectTileInfo(
        TiledObjectLayer layer,
        TiledMapObject mapObject,
        IReadOnlyList<TiledImportedTileset> tilesets,
        List<string> warnings)
    {
        if (TiledGid.IsEmpty(mapObject.Gid)) { return null; }

        uint globalTileId = TiledGid.GetTileId(mapObject.Gid);
        TiledImportedTileset? tileset = FindTileset(tilesets, globalTileId);
        int localTileId = tileset is null ? -1 : (int)globalTileId - tileset.FirstGid;

        if (tileset is null || !tileset.ContainsTile(localTileId))
        {
            AddWarning(
                warnings,
                $"Object {mapObject.Id} of object layer '{layer.Name}' draws global tile id " +
                $"{globalTileId}, which none of the map's tile sets holds; the object is reported " +
                "without its tile.");
            return null;
        }

        TiledTilesetTile? declared = tileset.GetTile(localTileId);

        //Column and Row are zero by contract: an object sits at pixel coordinates, not in a grid cell
        return new TiledTileInfo
        {
            LayerName = layer.Name,
            GlobalTileId = (int)globalTileId,
            LocalTileId = localTileId,
            TilesetName = tileset.Name,
            TileType = declared?.Type,
            TileProperties = declared?.Properties ?? TiledProperties.Empty,
            TilesetProperties = tileset.Document.Properties,
            FlippedHorizontally = TiledGid.IsFlippedHorizontally(mapObject.Gid),
            FlippedVertically = TiledGid.IsFlippedVertically(mapObject.Gid),
            FlippedDiagonally = TiledGid.IsFlippedDiagonally(mapObject.Gid),
        };
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        if (!warnings.Contains(message, StringComparer.Ordinal))
        {
            warnings.Add(message);
        }
    }

    //A frame of one imported map: which sheet, which flipped variant of it, and which tile.
    private readonly record struct FrameIdentity(string SheetKey, uint FlipFlags, int LocalTileId);
}
