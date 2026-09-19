using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates importing a Tiled map into a scene: the layers and tilesheets it produces, where each
/// cell's art comes from, the baked flipped variants, the overhang that anchors an oversized tile set,
/// collision, object data, and the message given for every map or tile set the importer refuses.
/// </summary>
public class TiledMapImporterTests : IDisposable
{
    private const string FixtureMapPath = "Tiled/tilemap-example-a.tmx";
    private const string TilesSheetSuffix = "#tileset-tiles";
    private const string CharactersSheetSuffix = "#tileset-characters";

    private static readonly SKColor TopLeftColor = SKColors.Red;
    private static readonly SKColor TopRightColor = SKColors.Lime;
    private static readonly SKColor BottomLeftColor = SKColors.Blue;
    private static readonly SKColor BottomRightColor = SKColors.White;

    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _scratchFolders = [];
    private readonly List<Scene> _scenes = [];
    private readonly TiledMapImporter _importer = new();

    /// <summary>
    /// Releases the scenes, the process-wide tilesheet registry, the bundles and the scratch folders
    /// this fixture touched, so that one test cannot see another's tilesheets.
    /// </summary>
    public void Dispose()
    {
        foreach (Scene scene in _scenes) { scene.Dispose(); }

        Scene.ClearAllScenes();
        TilesheetRegistry.Instance.Clear();

        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Import_adds_one_layer_per_tile_layer_in_map_order()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(
            entry, "map:fixture-layers", scene, new TiledMapImportOptions { ZOrderBase = 10 });

        //Assert
        import.Scene.Should().BeSameAs(scene);
        import.Layers.Count.Should().Be(4);
        int[] zOrders = [.. import.Layers.Select(layer => layer.ZOrder)];
        int[] expectedZOrders = [10, 11, 12, 13];
        zOrders.Should().Equal(expectedZOrders);
        import.Layers.Should().AllSatisfy(layer =>
        {
            layer.GridColumnCount.Should().Be(26);
            layer.GridRowCount.Should().Be(15);
            layer.TileWidth.Should().Be(18);
            layer.TileHeight.Should().Be(18);
            layer.Visible.Should().BeTrue();
            layer.Parallax.Should().Be(1f);
        });
        import.MapSizePx.Should().Be(new Size(468, 270));
        import.TileSize.Should().Be(new Size(18, 18));
        import.Warnings.Should().BeEmpty();
        import.ObjectGroups.Should().BeEmpty();
    }

    [Fact]
    public void Import_registers_one_tilesheet_per_tile_set_under_the_map_key()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();
        const string key = "map:fixture-sheets";

        //Act
        TiledMapImport import = _importer.Import(entry, key, scene);

        //Assert
        string[] names = [.. import.Tilesheets.Select(sheet => sheet.Name)];
        string[] expected = [key + CharactersSheetSuffix, key + TilesSheetSuffix];
        names.Should().Equal(expected);
        TilesheetRegistry.Instance.TryGet(key + TilesSheetSuffix, out Tilesheet? tiles).Should().BeTrue();
        tiles!.GetRegion(TiledFlipVariants.BaseRegionName).Should().NotBeNull();
        tiles.GetRegion(TiledFlipVariants.BaseRegionName)!.Columns.Should().Be(20);
        tiles.GetRegion(TiledFlipVariants.BaseRegionName)!.Rows.Should().Be(9);
        tiles.GetRegion(TiledFlipVariants.BaseRegionName)!.TileSize.Should().Be(new Size(18, 18));
    }

    [Fact]
    public void Import_maps_a_cell_to_the_frame_of_the_tile_set_that_owns_its_global_id()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();
        const string key = "map:fixture-cells";

        //Act
        TiledMapImport import = _importer.Import(entry, key, scene);

        //Assert - global id 132 is the tiles set (first gid 28), local 104, so column 4 of row 5.
        Frame ground = FrameAt(import.Layers[0], 0, 0);
        ground.Tilesheet.Name.Should().Be(key + TilesSheetSuffix);
        ground.RegionName.Should().Be(TiledFlipVariants.BaseRegionName);
        ground.XTile.Should().Be(4);
        ground.YTile.Should().Be(5);

        //Assert - global id 22 is the characters set (first gid 1), local 21, so column 3 of row 2.
        Frame character = FrameAt(import.Layers[3], 21, 2);
        character.Tilesheet.Name.Should().Be(key + CharactersSheetSuffix);
        character.RegionName.Should().Be(TiledFlipVariants.BaseRegionName);
        character.XTile.Should().Be(3);
        character.YTile.Should().Be(2);

        //Assert - an empty cell keeps no frame at all.
        import.Layers[0][7, 0]!.CurrentFrame.Tilesheet.Should().BeNull();
    }

    [Fact]
    public void Import_assigns_a_flipped_cell_from_the_baked_variant_region()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(entry, "map:fixture-flips", scene);

        //Assert - 3221225625 is global id 153 flipped horizontally and vertically, so local 125.
        Frame flippedBoth = FrameAt(import.Layers[0], 1, 6);
        flippedBoth.RegionName.Should().Be("tiles-fhv");
        flippedBoth.XTile.Should().Be(5);
        flippedBoth.YTile.Should().Be(6);

        //Assert - 2147483775 is global id 127 flipped horizontally.
        Frame flippedOnce = FrameAt(import.Layers[0], 14, 8);
        flippedOnce.RegionName.Should().Be("tiles-fh");
        flippedOnce.XTile.Should().Be(19);
        flippedOnce.YTile.Should().Be(4);

        //Assert - 2147483649 is the first character tile, flipped horizontally.
        Frame flippedCharacter = FrameAt(import.Layers[3], 5, 7);
        flippedCharacter.RegionName.Should().Be("tiles-fh");
        flippedCharacter.XTile.Should().Be(0);
        flippedCharacter.YTile.Should().Be(0);
        flippedCharacter.SkBitmap.Should().NotBeNull();
        flippedCharacter.TileSize.Should().Be(new Size(24, 24));
    }

    [Fact]
    public void Import_bakes_only_the_flip_combinations_the_map_uses()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();
        const string key = "map:fixture-variants";

        //Act
        _importer.Import(entry, key, scene);

        //Assert - the tiles set is flipped horizontally and both ways, the characters set only
        //  horizontally; "default" is the whole-bitmap region every registry loader adds.
        RegionNames(key + TilesSheetSuffix).Should().Equal(
            ["default", "tiles", "tiles-fh", "tiles-fhv"]);
        RegionNames(key + CharactersSheetSuffix).Should().Equal(["default", "tiles", "tiles-fh"]);
    }

    [Fact]
    public void Import_anchors_a_tile_set_larger_than_the_grid_with_region_overhang()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();
        const string key = "map:fixture-overhang";

        //Act
        TiledMapImport import = _importer.Import(entry, key, scene);

        //Assert - 24 pixel tiles with a tile offset of -3 on an 18 pixel grid: 6 pixels above the
        //  cell, and the 6 extra pixels of width split by the offset.
        Sheet(key + CharactersSheetSuffix).Regions
            .Where(region => region.Name != "default")
            .Should().AllSatisfy(region => region.Overhang.Should().Be(new Spacing(3, 6, 3, 0)));
        Sheet(key + TilesSheetSuffix).Regions
            .Where(region => region.Name != "default")
            .Should().AllSatisfy(region => region.Overhang.Should().Be(Spacing.None));

        //Assert - the drawn rectangle of a character cell is the whole 24 by 24 tile.
        Rectangle drawn = import.Layers[3][21, 2]!.DrawLocationWorld;
        drawn.Width.Should().Be(24);
        drawn.Height.Should().Be(24);
        drawn.Right.Should().Be((22 * 18) + 3);
        drawn.Bottom.Should().Be(3 * 18);
    }

    [Fact]
    public void Import_honours_a_layer_filter()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(
            entry,
            "map:fixture-filter",
            scene,
            new TiledMapImportOptions
            {
                LayerFilter = name => name.Equals("Characters", StringComparison.Ordinal),
            });

        //Assert - the layer keeps the z-order its position in the map earns it.
        import.Layers.Count.Should().Be(1);
        import.Layers[0].ZOrder.Should().Be(3);
        import.Tilesheets.Count.Should().Be(2);
    }

    [Fact]
    public void Import_reuses_a_tilesheet_that_is_already_registered_under_its_key()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene first = NewScene();
        Scene second = NewScene();
        const string key = "map:fixture-twice";

        //Act
        TiledMapImport once = _importer.Import(entry, key, first);
        int registered = TilesheetRegistry.Instance.Count;
        TiledMapImport twice = _importer.Import(entry, key, second);

        //Assert - the sheets are the same objects, and the layers were added again.
        twice.Tilesheets[0].Should().BeSameAs(once.Tilesheets[0]);
        twice.Tilesheets[1].Should().BeSameAs(once.Tilesheets[1]);
        TilesheetRegistry.Instance.Count.Should().Be(registered);
        twice.Layers.Count.Should().Be(4);
        twice.Warnings.Should().BeEmpty();
        FrameAt(twice.Layers[0], 1, 6).RegionName.Should().Be("tiles-fhv");
    }

    [Fact]
    public void Import_rejects_an_asset_that_is_not_a_tile_map()
    {
        //Arrange
        KenneyAssetSource source = Fixture(TestFixtures.SimulatedBundleFileName);
        KenneyAssetEntry image = source.Packs[0].Entries
            .First(candidate => candidate.Kind == GameAssetKind.Image);
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(image, "map:not-a-map", scene);

        //Assert
        act.Should().Throw<UnsupportedGameAssetException>();
    }

    [Fact]
    public void Import_bakes_each_flip_the_way_the_map_draws_it()
    {
        //Arrange - one 16 pixel tile whose four quadrants are four colours, placed four times: as
        //  written, flipped horizontally, flipped vertically, and flipped diagonally.
        uint[] gids =
        [
            1u,
            1u | TiledGid.FlipHorizontallyFlag,
            1u | TiledGid.FlipVerticallyFlag,
            1u | TiledGid.FlipDiagonallyFlag,
        ];
        KenneyAssetSource source = SyntheticBundle(
            "flips",
            new Dictionary<string, byte[]>
            {
                ["art/tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 16),
                ["maps/tiles.tsx"] = Bytes(Tsx(
                    "quadrants", 16, 16, tileCount: 1, columns: 1, image: "../art/tiles.png",
                    imageWidth: 16, imageHeight: 16)),
                ["maps/flips.tmx"] = Bytes(Map(
                    4, 1, 16, gids, """<tileset firstgid="1" source="tiles.tsx"/>""")),
            });
        Scene scene = NewScene();
        const string key = "map:flip-bake";

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "maps/flips.tmx"), key, scene);

        //Assert
        RegionNames(key + "#quadrants").Should().Equal(
            ["default", "tiles", "tiles-fd", "tiles-fv", "tiles-fh"]);
        import.Warnings.Should().BeEmpty();

        //Assert - as written.
        AssertQuadrants(
            import.Layers[0], 0, TopLeftColor, TopRightColor, BottomLeftColor, BottomRightColor);

        //Assert - flipped horizontally: the columns swap.
        AssertQuadrants(
            import.Layers[0], 1, TopRightColor, TopLeftColor, BottomRightColor, BottomLeftColor);

        //Assert - flipped vertically: the rows swap.
        AssertQuadrants(
            import.Layers[0], 2, BottomLeftColor, BottomRightColor, TopLeftColor, TopRightColor);

        //Assert - flipped diagonally: the tile is transposed.
        AssertQuadrants(
            import.Layers[0], 3, TopLeftColor, BottomLeftColor, TopRightColor, BottomRightColor);
    }

    [Fact]
    public void Import_cuts_tiles_out_of_a_tile_set_that_has_spacing_and_a_margin()
    {
        //Arrange - six solid-coloured tiles in three columns, one pixel apart, two pixels in from the
        //  image edge.
        SKColor[] colors =
        [
            SKColors.Red, SKColors.Lime, SKColors.Blue,
            SKColors.Yellow, SKColors.Magenta, SKColors.Cyan,
        ];
        KenneyAssetSource source = SyntheticBundle(
            "spacing",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = SolidTilesImage(colors, columns: 3, tileSize: 8, spacing: 1, margin: 2),
                ["packed.tsx"] = Bytes(Tsx(
                    "packed", 8, 8, tileCount: 6, columns: 3, image: "tiles.png",
                    imageWidth: 30, imageHeight: 21, spacing: 1, margin: 2)),
                ["packed.tmx"] = Bytes(Map(
                    2, 1, 8, [5u, 1u], """<tileset firstgid="1" source="packed.tsx"/>""")),
            });
        Scene scene = NewScene();
        const string key = "map:spacing";

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "packed.tmx"), key, scene);

        //Assert
        TilesheetRegion region = Sheet(key + "#packed").GetRegion(TiledFlipVariants.BaseRegionName)!;
        region.Columns.Should().Be(3);
        region.Rows.Should().Be(2);
        region.TilePadding.Should().Be(new Spacing(0, 0, 1, 1));
        region.RegionMargin.Should().Be(new Spacing(2, 2, 0, 0));
        import.Warnings.Should().BeEmpty();

        //Assert - global id 5 is local tile 4, the middle tile of the second row.
        Frame middle = FrameAt(import.Layers[0], 0, 0);
        middle.XTile.Should().Be(1);
        middle.YTile.Should().Be(1);
        CenterColor(middle).Should().Be(colors[4]);
        CenterColor(FrameAt(import.Layers[0], 1, 0)).Should().Be(colors[0]);
    }

    [Fact]
    public void Import_reads_base64_zlib_layer_data_the_same_as_its_csv_twin()
    {
        //Arrange
        uint[] gids = [1u, 2u | TiledGid.FlipHorizontallyFlag, 0u, 3u];
        KenneyAssetSource source = SyntheticBundle(
            "encodings",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 2, rows: 2, tileSize: 8),
                ["grid.tsx"] = Bytes(Tsx(
                    "grid", 8, 8, tileCount: 4, columns: 2, image: "tiles.png",
                    imageWidth: 16, imageHeight: 16)),
                ["csv.tmx"] = Bytes(Map(
                    4, 1, 8, gids, """<tileset firstgid="1" source="grid.tsx"/>""")),
                ["zlib.tmx"] = Bytes(Map(
                    4,
                    1,
                    8,
                    gids,
                    """<tileset firstgid="1" source="grid.tsx"/>""",
                    data: Base64Data(gids, compress: true))),
            });
        Scene csvScene = NewScene();
        Scene zlibScene = NewScene();

        //Act
        TiledMapImport csv = _importer.Import(MapEntry(source, "csv.tmx"), "map:csv", csvScene);
        TiledMapImport zlib = _importer.Import(MapEntry(source, "zlib.tmx"), "map:zlib", zlibScene);

        //Assert
        csv.Warnings.Should().BeEmpty();
        zlib.Warnings.Should().BeEmpty();

        for (int column = 0; column < 4; column++)
        {
            Frame fromCsv = csv.Layers[0][column, 0]!.CurrentFrame;
            Frame fromZlib = zlib.Layers[0][column, 0]!.CurrentFrame;
            fromZlib.RegionName.Should().Be(fromCsv.RegionName);
            fromZlib.XTile.Should().Be(fromCsv.XTile);
            fromZlib.YTile.Should().Be(fromCsv.YTile);
            (fromZlib.Tilesheet is null).Should().Be(fromCsv.Tilesheet is null);
        }
    }

    [Fact]
    public void Import_gives_a_tile_the_collision_type_its_properties_ask_for()
    {
        //Arrange - the tile set opts in as a trigger, tile 0 overrides to blocking and tile 2 opts out.
        const string tiles = """
             <tile id="0"><properties><property name="collision" value="blocking"/></properties></tile>
             <tile id="2"><properties><property name="collision" value="false"/></properties></tile>
            """;
        KenneyAssetSource source = SyntheticBundle(
            "collision",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 2, rows: 2, tileSize: 8),
                ["grid.tsx"] = Bytes(Tsx(
                    "grid",
                    8,
                    8,
                    tileCount: 4,
                    columns: 2,
                    image: "tiles.png",
                    imageWidth: 16,
                    imageHeight: 16,
                    properties: """<properties><property name="collision" value="trigger"/></properties>""",
                    tiles: tiles)),
                ["grid.tmx"] = Bytes(Map(
                    3, 1, 8, [1u, 2u, 3u], """<tileset firstgid="1" source="grid.tsx"/>""")),
            });
        Scene scene = NewScene();
        const string key = "map:collision";

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "grid.tmx"), key, scene);

        //Assert
        SceneLayer layer = import.Layers[0];
        layer[0, 0]!.CollisionType.Should().Be(TileCollisionType.Blocking);
        layer[0, 0]!.CollisionsEnabled.Should().BeTrue();
        layer[1, 0]!.CollisionType.Should().Be(TileCollisionType.Trigger);
        layer[2, 0]!.CollisionType.Should().Be(TileCollisionType.None);
        layer[2, 0]!.CollisionsEnabled.Should().BeFalse();

        //Assert - the type is carried by the frame, so any other tile using it inherits the same.
        TilesheetRegion region = Sheet(key + "#grid").GetRegion(TiledFlipVariants.BaseRegionName)!;
        region.GetFrameCollisionType(0, 0).Should().Be(TileCollisionType.Blocking);
        region.GetFrameCollisionType(1, 0).Should().Be(TileCollisionType.Trigger);
        region.TryGetFrameCollisionTypeOverride(0, 1, out _).Should().BeFalse();
    }

    [Fact]
    public void Import_lets_a_collision_selector_answer_for_each_cell()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "selector",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["one.tmx"] = Bytes(Map(
                    3, 1, 8, [1u, 1u, 1u], """<tileset firstgid="1" source="one.tsx"/>""")),
            });
        Scene scene = NewScene();
        List<TiledTileInfo> seen = [];

        //Act - the same tile, asked for three times, collides differently in each column.
        TiledMapImport import = _importer.Import(
            MapEntry(source, "one.tmx"),
            "map:selector",
            scene,
            new TiledMapImportOptions
            {
                CollisionSelector = info =>
                {
                    seen.Add(info);

                    return info.Column switch
                    {
                        0 => TileCollisionType.Blocking,
                        1 => TileCollisionType.Trigger,
                        _ => TileCollisionType.None,
                    };
                },
                CollisionProfileName = CollisionProfileNames.Actor,
            });

        //Assert
        SceneLayer layer = import.Layers[0];
        layer[0, 0]!.CollisionType.Should().Be(TileCollisionType.Blocking);
        layer[1, 0]!.CollisionType.Should().Be(TileCollisionType.Trigger);
        layer[2, 0]!.CollisionType.Should().Be(TileCollisionType.None);

        //Assert - the selector was told which cell it was answering for, and about the tile set.
        seen.Count.Should().Be(3);
        seen.Should().AllSatisfy(info =>
        {
            info.LayerName.Should().Be("Ground");
            info.Row.Should().Be(0);
            info.GlobalTileId.Should().Be(1);
            info.LocalTileId.Should().Be(0);
            info.TilesetName.Should().Be("one");
            info.FlippedHorizontally.Should().BeFalse();
        });

        //Assert - the profile reached every tile of the layer.
        layer.DefaultTileCollisionProfile.Should().Be(CollisionProfileNames.Actor);
        layer[2, 0]!.CollisionProfileName.Should().Be(CollisionProfileNames.Actor);
    }

    [Fact]
    public void Import_keeps_object_layers_as_data_and_can_be_told_not_to()
    {
        //Arrange
        const string objects = """
         <objectgroup id="2" name="Spawns">
          <object id="7" name="start" type="spawn" x="18" y="36" width="16" height="32">
           <properties><property name="team" value="blue"/></properties>
          </object>
         </objectgroup>
        """;
        KenneyAssetSource source = SyntheticBundle(
            "objects",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["one.tmx"] = Bytes(Map(
                    1, 1, 8, [1u], """<tileset firstgid="1" source="one.tsx"/>""", extra: objects)),
            });
        KenneyAssetEntry entry = MapEntry(source, "one.tmx");

        //Act
        TiledMapImport kept = _importer.Import(entry, "map:objects", NewScene());
        TiledMapImport dropped = _importer.Import(
            entry,
            "map:objects",
            NewScene(),
            new TiledMapImportOptions { ImportObjectLayers = false });

        //Assert
        TiledObjectGroup group = Assert.Single(kept.ObjectGroups);
        group.Name.Should().Be("Spawns");
        group.Offset.Should().Be(new PointF(0f, 0f));
        group.Visible.Should().BeTrue();
        group.Opacity.Should().Be(1f);
        //The object layer follows the map's one tile layer
        group.DocumentIndex.Should().Be(1);
        TiledObject spawn = Assert.Single(group.Objects);
        spawn.Id.Should().Be(7);
        spawn.Name.Should().Be("start");
        spawn.Type.Should().Be("spawn");
        spawn.Bounds.Should().Be(new RectangleF(18f, 36f, 16f, 32f));
        spawn.Rotation.Should().Be(0f);
        spawn.Visible.Should().BeTrue();
        spawn.Tile.Should().BeNull();
        spawn.Properties["team"].Should().Be("blue");
        dropped.ObjectGroups.Should().BeEmpty();
    }

    [Fact]
    public void Import_reports_an_object_layers_offset_opacity_rotation_and_tile_objects()
    {
        //Arrange - a hidden, half-transparent, offset object layer holding a rotated invisible shape,
        //  a horizontally flipped tile object, and a tile object whose global id no tile set holds.
        uint flippedGid = 2u | TiledGid.FlipHorizontallyFlag;
        string objects = $"""
         <objectgroup id="2" name="Spawns" offsetx="16" offsety="-8" opacity="0.5" visible="0">
          <object id="7" name="start" type="spawn" x="18" y="36" width="16" height="32" rotation="45" visible="0">
           <properties><property name="team" value="blue"/></properties>
          </object>
          <object id="8" name="crate" gid="{flippedGid}" x="8" y="16" width="8" height="8"/>
          <object id="9" name="stray" gid="99" x="0" y="0" width="8" height="8"/>
         </objectgroup>
        """;
        KenneyAssetSource source = SyntheticBundle(
            "object-detail",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 2, rows: 2, tileSize: 8),
                ["grid.tsx"] = Bytes(Tsx(
                    "grid",
                    8,
                    8,
                    tileCount: 4,
                    columns: 2,
                    image: "tiles.png",
                    imageWidth: 16,
                    imageHeight: 16,
                    properties: """ <properties><property name="kind" value="terrain"/></properties>""",
                    tiles: """ <tile id="1" type="crate"><properties><property name="collision" value="blocking"/></properties></tile>""")),
                ["grid.tmx"] = Bytes(Map(
                    2, 1, 8, [1u, 2u], """<tileset firstgid="1" source="grid.tsx"/>""", extra: objects)),
            });

        //Act
        TiledMapImport import = _importer.Import(
            MapEntry(source, "grid.tmx"), "map:object-detail", NewScene());

        //Assert - the layer's own values
        TiledObjectGroup group = Assert.Single(import.ObjectGroups);
        group.Offset.Should().Be(new PointF(16f, -8f));
        group.Visible.Should().BeFalse();
        group.Opacity.Should().Be(0.5f);
        group.DocumentIndex.Should().Be(1);
        group.Objects.Count.Should().Be(3);

        //Assert - a rotated, hidden shape object keeps its unrotated rectangle and has no tile
        TiledObject shape = group.Objects[0];
        shape.Bounds.Should().Be(new RectangleF(18f, 36f, 16f, 32f));
        shape.Rotation.Should().Be(45f);
        shape.Visible.Should().BeFalse();
        shape.Tile.Should().BeNull();

        //Assert - a tile object carries the tile it draws, its properties and its flip flags
        TiledObject tileObject = group.Objects[1];
        tileObject.Visible.Should().BeTrue();
        tileObject.Tile.Should().NotBeNull();
        tileObject.Tile!.LayerName.Should().Be("Spawns");
        tileObject.Tile.Column.Should().Be(0);
        tileObject.Tile.Row.Should().Be(0);
        tileObject.Tile.GlobalTileId.Should().Be(2);
        tileObject.Tile.LocalTileId.Should().Be(1);
        tileObject.Tile.TilesetName.Should().Be("grid");
        tileObject.Tile.TileType.Should().Be("crate");
        tileObject.Tile.TileProperties["collision"].Should().Be("blocking");
        tileObject.Tile.TilesetProperties["kind"].Should().Be("terrain");
        tileObject.Tile.FlippedHorizontally.Should().BeTrue();
        tileObject.Tile.FlippedVertically.Should().BeFalse();
        tileObject.Tile.FlippedDiagonally.Should().BeFalse();

        //Assert - a tile object nothing owns is reported without its tile, and warned about
        group.Objects[2].Tile.Should().BeNull();
        import.Warnings.Should().Contain(warning => warning.Contains("global tile id 99"));

        //Assert - nothing is lost any more, so the old warnings are gone
        import.Warnings.Should().NotContain(warning => warning.Contains("rotated objects"));
        import.Warnings.Should().NotContain(warning => warning.Contains("drawn at an offset"));
    }

    [Fact]
    public void Import_warns_about_what_it_cannot_reproduce()
    {
        //Arrange - a half-transparent layer drawn at an offset, an image layer, and a tile set whose
        //  tiles carry their own collision shapes.
        const string imageLayer = """
         <imagelayer id="3" name="Backdrop"><image source="tiles.png"/></imagelayer>
        """;
        KenneyAssetSource source = SyntheticBundle(
            "warnings",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one",
                    8,
                    8,
                    tileCount: 1,
                    columns: 1,
                    image: "tiles.png",
                    imageWidth: 8,
                    imageHeight: 8,
                    tiles: """ <tile id="0"><objectgroup><object id="1" x="1" y="1" width="6" height="6"/></objectgroup></tile>""")),
                ["one.tmx"] = Bytes(Map(
                    1,
                    1,
                    8,
                    [1u],
                    """<tileset firstgid="1" source="one.tsx"/>""",
                    layerAttributes: """opacity="0.5" offsetx="16" offsety="-8" """,
                    extra: imageLayer)),
            });
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "one.tmx"), "map:warnings", scene);

        //Assert
        import.Warnings.Should().Contain(warning => warning.Contains("opacity"));
        import.Warnings.Should().Contain(warning => warning.Contains("Image layer 'Backdrop'"));
        import.Warnings.Should().Contain(warning => warning.Contains("collision shapes"));

        //Assert - the layer offset moves the layer's content, so the origin is its negation.
        import.Layers[0].OriginPx.Should().Be(new Point(-16, 8));
    }

    [Fact]
    public void Import_hides_a_layer_the_map_hides_or_makes_invisible()
    {
        //Arrange - a hidden layer, and a second layer the map draws at zero opacity.
        const string second = """
         <layer id="2" name="Ghost" width="2" height="1" opacity="0">
          <data encoding="csv">1,1</data>
         </layer>
        """;
        KenneyAssetSource source = SyntheticBundle(
            "hidden",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["one.tmx"] = Bytes(Map(
                    2,
                    1,
                    8,
                    [1u, 1u],
                    """<tileset firstgid="1" source="one.tsx"/>""",
                    layerAttributes: """visible="0" """,
                    extra: second)),
            });
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "one.tmx"), "map:hidden", scene);

        //Assert - both layers are imported, with their tiles, but neither is drawn.
        import.Layers.Count.Should().Be(2);
        import.Layers.Should().AllSatisfy(layer => layer.Visible.Should().BeFalse());
        FrameAt(import.Layers[1], 0, 0).Tilesheet.Should().NotBeNull();
        import.Warnings.Should().Contain(warning => warning.Contains("imported hidden"));
    }

    [Fact]
    public void Import_fails_when_a_tile_set_image_cannot_be_resolved()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "missing-image",
            new Dictionary<string, byte[]>
            {
                ["maps/one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "../art/absent.png",
                    imageWidth: 8, imageHeight: 8)),
                ["maps/one.tmx"] = Bytes(Map(
                    1, 1, 8, [1u], """<tileset firstgid="1" source="one.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "maps/one.tmx"), "map:missing", scene);

        //Assert
        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*../art/absent.png*");
    }

    [Fact]
    public void Import_fails_when_a_tile_set_document_cannot_be_resolved()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "missing-tsx",
            new Dictionary<string, byte[]>
            {
                ["one.tmx"] = Bytes(Map(
                    1, 1, 8, [1u], """<tileset firstgid="1" source="absent.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "one.tmx"), "map:missing-tsx", scene);

        //Assert
        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*absent.tsx*");
    }

    [Fact]
    public void Import_fails_when_a_tile_set_grid_does_not_fit_its_image()
    {
        //Arrange - the document claims 9 tiles in a 16 by 16 image that holds 4.
        KenneyAssetSource source = SyntheticBundle(
            "too-small",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 2, rows: 2, tileSize: 8),
                ["grid.tsx"] = Bytes(Tsx(
                    "grid", 8, 8, tileCount: 9, columns: 3, image: "tiles.png",
                    imageWidth: 24, imageHeight: 24)),
                ["grid.tmx"] = Bytes(Map(
                    1, 1, 8, [1u], """<tileset firstgid="1" source="grid.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "grid.tmx"), "map:too-small", scene);

        //Assert
        act.Should().Throw<TiledMapParseException>()
            .WithMessage("*16 by 16 pixels*");
    }

    [Fact]
    public void Import_refuses_an_isometric_map()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "isometric",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["one.tmx"] = Bytes(Map(
                    1,
                    1,
                    8,
                    [1u],
                    """<tileset firstgid="1" source="one.tsx"/>""",
                    orientation: "isometric")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "one.tmx"), "map:isometric", scene);

        //Assert
        act.Should().Throw<TiledMapParseException>()
            .WithMessage("*isometric*");
    }

    [Fact]
    public void Import_refuses_an_infinite_map()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "infinite",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["one.tsx"] = Bytes(Tsx(
                    "one", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["one.tmx"] = Bytes(Map(
                    1,
                    1,
                    8,
                    [1u],
                    """<tileset firstgid="1" source="one.tsx"/>""",
                    infinite: "1")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "one.tmx"), "map:infinite", scene);

        //Assert
        act.Should().Throw<TiledMapParseException>()
            .WithMessage("*infinite*");
    }

    [Fact]
    public void Import_refuses_a_tile_set_that_holds_one_image_per_tile()
    {
        //Arrange
        KenneyAssetSource source = SyntheticBundle(
            "collection",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["loose.tsx"] = Bytes("""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <tileset version="1.10" name="loose" tilewidth="8" tileheight="8" tilecount="1" columns="0">
                     <tile id="0"><image source="tiles.png" width="8" height="8"/></tile>
                    </tileset>
                    """),
                ["loose.tmx"] = Bytes(Map(
                    1, 1, 8, [1u], """<tileset firstgid="1" source="loose.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "loose.tmx"), "map:collection", scene);

        //Assert
        act.Should().Throw<UnsupportedGameAssetException>()
            .WithMessage("*image per tile*");
    }

    [Fact]
    public void Import_refuses_a_tile_set_smaller_than_the_map_grid()
    {
        //Arrange - 8 pixel tiles on a 16 pixel grid.
        KenneyAssetSource source = SyntheticBundle(
            "smaller",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 1, rows: 1, tileSize: 8),
                ["small.tsx"] = Bytes(Tsx(
                    "small", 8, 8, tileCount: 1, columns: 1, image: "tiles.png",
                    imageWidth: 8, imageHeight: 8)),
                ["small.tmx"] = Bytes(Map(
                    1, 1, 16, [1u], """<tileset firstgid="1" source="small.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        Action act = () => _importer.Import(MapEntry(source, "small.tmx"), "map:smaller", scene);

        //Assert
        act.Should().Throw<UnsupportedGameAssetException>()
            .WithMessage("*smaller than the map*");
    }

    [Fact]
    public void Import_leaves_a_cell_empty_when_no_tile_set_holds_its_global_id()
    {
        //Arrange - global id 9 is past the end of the only tile set.
        KenneyAssetSource source = SyntheticBundle(
            "stray-gid",
            new Dictionary<string, byte[]>
            {
                ["tiles.png"] = QuadrantImage(columns: 2, rows: 2, tileSize: 8),
                ["grid.tsx"] = Bytes(Tsx(
                    "grid", 8, 8, tileCount: 4, columns: 2, image: "tiles.png",
                    imageWidth: 16, imageHeight: 16)),
                ["grid.tmx"] = Bytes(Map(
                    2, 1, 8, [1u, 9u], """<tileset firstgid="1" source="grid.tsx"/>""")),
            });
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _importer.Import(MapEntry(source, "grid.tmx"), "map:stray", scene);

        //Assert
        import.Layers[0][0, 0]!.CurrentFrame.Tilesheet.Should().NotBeNull();
        import.Layers[0][1, 0]!.CurrentFrame.Tilesheet.Should().BeNull();
        import.Warnings.Should().Contain(warning => warning.Contains("Global tile id 9"));
    }

    [Fact]
    public void Import_validates_its_arguments()
    {
        //Arrange
        KenneyAssetEntry entry = FixtureMap();
        Scene scene = NewScene();

        //Act
        Action nullEntry = () => _importer.Import(null!, "map:key", scene);
        Action blankKey = () => _importer.Import(entry, "  ", scene);
        Action nullScene = () => _importer.Import(entry, "map:key", null!);

        //Assert
        nullEntry.Should().Throw<ArgumentNullException>();
        blankKey.Should().Throw<ArgumentException>();
        nullScene.Should().Throw<ArgumentNullException>();
    }

    private KenneyAssetEntry FixtureMap() =>
        MapEntry(Fixture(TestFixtures.SimulatedBundleFileName), FixtureMapPath);

    private KenneyAssetSource Fixture(string fileName)
    {
        KenneyAssetSource source = KenneyAssetSource.Open(TestFixtures.BundlePath(fileName));
        _sources.Add(source);

        return source;
    }

    private KenneyAssetSource SyntheticBundle(string purpose, Dictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);

        entries["License.txt"] = Bytes(TestFixtures.LicenseText(purpose, "1.0"));
        string zipPath = Path.Combine(folder, $"{purpose}.zip");
        TestFixtures.BuildZip(zipPath, entries);

        KenneyAssetSource source = KenneyAssetSource.Open(zipPath);
        _sources.Add(source);

        return source;
    }

    private static KenneyAssetEntry MapEntry(KenneyAssetSource source, string archivePath) =>
        source.Packs
            .SelectMany(pack => pack.Entries)
            .First(entry => entry.Path.Equals(archivePath, StringComparison.OrdinalIgnoreCase));

    private Scene NewScene()
    {
        Scene scene = new();
        _scenes.Add(scene);

        return scene;
    }

    private static Frame FrameAt(SceneLayer layer, int column, int row) =>
        layer[column, row]!.CurrentFrame;

    private static Tilesheet Sheet(string key) => TilesheetRegistry.Instance[key];

    private static IReadOnlyList<string> RegionNames(string key) =>
        [.. Sheet(key).Regions.Select(region => region.Name)];

    private static void AssertQuadrants(
        SceneLayer layer,
        int column,
        SKColor topLeft,
        SKColor topRight,
        SKColor bottomLeft,
        SKColor bottomRight)
    {
        SKBitmap tile = FrameAt(layer, column, 0).SkBitmap!;
        int low = tile.Width / 4;
        int high = tile.Width - low - 1;

        tile.GetPixel(low, low).Should().Be(topLeft);
        tile.GetPixel(high, low).Should().Be(topRight);
        tile.GetPixel(low, high).Should().Be(bottomLeft);
        tile.GetPixel(high, high).Should().Be(bottomRight);
    }

    private static SKColor CenterColor(Frame frame)
    {
        SKBitmap tile = frame.SkBitmap!;

        return tile.GetPixel(tile.Width / 2, tile.Height / 2);
    }

    //Every tile of this image carries the same four quadrant colours, so a baked variant shows which
    //  way it was flipped.
    private static byte[] QuadrantImage(int columns, int rows, int tileSize)
    {
        using SKBitmap bitmap = new(columns * tileSize, rows * tileSize);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        int half = tileSize / 2;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int left = column * tileSize;
                int top = row * tileSize;
                Fill(canvas, left, top, half, half, TopLeftColor);
                Fill(canvas, left + half, top, half, half, TopRightColor);
                Fill(canvas, left, top + half, half, half, BottomLeftColor);
                Fill(canvas, left + half, top + half, half, half, BottomRightColor);
            }
        }

        return Encode(bitmap);
    }

    //A tile grid of solid colours laid out the way Tiled lays one out: a margin, then tiles a spacing
    //  apart.
    private static byte[] SolidTilesImage(
        IReadOnlyList<SKColor> colors, int columns, int tileSize, int spacing, int margin)
    {
        //Tiled writes the margin on every side, so the image is wider and taller than the tiles alone.
        int rows = ((colors.Count - 1) / columns) + 1;
        int width = (2 * margin) + (columns * tileSize) + ((columns - 1) * spacing);
        int height = (2 * margin) + (rows * tileSize) + ((rows - 1) * spacing);

        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        for (int tile = 0; tile < colors.Count; tile++)
        {
            int left = margin + ((tile % columns) * (tileSize + spacing));
            int top = margin + ((tile / columns) * (tileSize + spacing));
            Fill(canvas, left, top, tileSize, tileSize, colors[tile]);
        }

        return Encode(bitmap);
    }

    private static void Fill(SKCanvas canvas, int x, int y, int width, int height, SKColor color)
    {
        using SKPaint paint = new() { Color = color, IsAntialias = false };
        canvas.DrawRect(SKRect.Create(x, y, width, height), paint);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Tsx(
        string name,
        int tileWidth,
        int tileHeight,
        int tileCount,
        int columns,
        string image,
        int imageWidth,
        int imageHeight,
        int spacing = 0,
        int margin = 0,
        string tileOffset = "",
        string properties = "",
        string tiles = "")
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <tileset version="1.10" name="{name}" tilewidth="{tileWidth}" tileheight="{tileHeight}" tilecount="{tileCount}" columns="{columns}" spacing="{spacing}" margin="{margin}">
            {properties}
            {tileOffset}
             <image source="{image}" width="{imageWidth}" height="{imageHeight}"/>
            {tiles}
            </tileset>
            """;
    }

    private static string Map(
        int width,
        int height,
        int tileSize,
        uint[] gids,
        string tilesets,
        string layerAttributes = "",
        string extra = "",
        string orientation = "orthogonal",
        string infinite = "0",
        string? data = null)
    {
        string layerData = data ?? $"""<data encoding="csv">{string.Join(",", gids)}</data>""";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <map version="1.10" orientation="{orientation}" renderorder="right-down" width="{width}" height="{height}" tilewidth="{tileSize}" tileheight="{tileSize}" infinite="{infinite}">
             {tilesets}
             <layer id="1" name="Ground" width="{width}" height="{height}" {layerAttributes}>
              {layerData}
             </layer>
            {extra}
            </map>
            """;
    }

    private static string Base64Data(uint[] gids, bool compress)
    {
        byte[] raw = new byte[gids.Length * 4];

        for (int index = 0; index < gids.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(index * 4, 4), gids[index]);
        }

        if (!compress)
        {
            return $"""<data encoding="base64">{Convert.ToBase64String(raw)}</data>""";
        }

        using MemoryStream output = new();

        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(raw, 0, raw.Length);
        }

        return $"""
            <data encoding="base64" compression="zlib">{Convert.ToBase64String(output.ToArray())}</data>
            """;
    }
}
