using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates reading Tiled map and tile set documents: the real Kenney map in the simulated bundle,
/// every layer encoding the parser accepts, the features it keeps as data, and the message it gives for
/// each feature it refuses.
/// </summary>
public class TiledMapParserTests
{
    private const string MapPath = "Tiled/tilemap-example-a.tmx";

    [Fact]
    public void ParseMap_reads_the_kenney_map_in_the_simulated_bundle()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(archive.ReadText(MapPath), MapPath);

        //Assert
        map.Width.Should().Be(26);
        map.Height.Should().Be(15);
        map.TileWidth.Should().Be(18);
        map.TileHeight.Should().Be(18);
        map.Orientation.Should().Be("orthogonal");
        map.RenderOrder.Should().Be("right-down");
        map.DocumentPath.Should().Be(MapPath);
        map.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ParseMap_reads_every_tile_layer_in_document_order()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(archive.ReadText(MapPath), MapPath);

        //Assert
        string[] names = [.. map.TileLayers.Select(l => l.Name)];
        string[] expected = ["Tiles", "Tiles (layer A)", "Tiles (layer B)", "Characters"];
        names.Should().Equal(expected);
        map.TileLayers.Should().AllSatisfy(layer => layer.Gids.Length.Should().Be(26 * 15));
        int[] indexes = [.. map.TileLayers.Select(l => l.DocumentIndex)];
        int[] expectedIndexes = [0, 1, 2, 3];
        indexes.Should().Equal(expectedIndexes);
    }

    [Fact]
    public void ParseMap_keeps_the_flip_bits_of_a_cell_value()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(archive.ReadText(MapPath), MapPath);

        //Assert
        uint[] flipped = [.. map.TileLayers
            .SelectMany(l => l.Gids)
            .Where(gid => TiledGid.GetFlipFlags(gid) != 0u)];
        flipped.Should().NotBeEmpty();
        flipped.Should().Contain(3221225625u);
    }

    [Fact]
    public void ParseMap_reads_both_external_tile_set_references()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(archive.ReadText(MapPath), MapPath);

        //Assert
        map.Tilesets.Count.Should().Be(2);
        map.Tilesets[0].FirstGid.Should().Be(1);
        map.Tilesets[0].Source.Should().Be("tileset-characters.tsx");
        map.Tilesets[0].Inline.Should().BeNull();
        map.Tilesets[1].FirstGid.Should().Be(28);
        map.Tilesets[1].Source.Should().Be("tileset-tiles.tsx");
    }

    [Fact]
    public void ParseTileset_reads_a_tile_offset_and_a_tile_size_larger_than_the_map_grid()
    {
        //Arrange
        // The characters tile set is 24 pixels on an 18-pixel map and shifts itself with a tileoffset.
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));
        const string path = "Tiled/tileset-characters.tsx";

        //Act
        TiledTilesetDocument tileset = TiledMapParser.ParseTileset(archive.ReadText(path), path);

        //Assert
        tileset.Name.Should().Be("tileset-characters");
        tileset.TileWidth.Should().Be(24);
        tileset.TileHeight.Should().Be(24);
        tileset.TileCount.Should().Be(27);
        tileset.Columns.Should().Be(9);
        tileset.TileOffsetX.Should().Be(-3);
        tileset.TileOffsetY.Should().Be(0);
        tileset.ImagePath.Should().Be("../Tilemap/tilemap-characters_packed.png");
        tileset.ImageWidth.Should().Be(216);
        tileset.ImageHeight.Should().Be(72);
        tileset.IsImageCollection.Should().BeFalse();
    }

    [Fact]
    public void ParseTileset_reads_spacing_and_margin()
    {
        //Act
        TiledTilesetDocument tileset = TiledMapParser.ParseTileset(
            "<tileset name=\"legacy\" tilewidth=\"16\" tileheight=\"16\" spacing=\"1\" margin=\"2\" " +
            "tilecount=\"1024\" columns=\"32\">" +
            "<image source=\"tileset_legacy.png\" width=\"543\" height=\"543\"/></tileset>",
            "Tilemap/tileset_colored.tsx");

        //Assert
        tileset.Spacing.Should().Be(1);
        tileset.Margin.Should().Be(2);
        tileset.Columns.Should().Be(32);
    }

    [Fact]
    public void ParseTileset_keeps_tile_set_and_per_tile_properties()
    {
        //Act
        TiledTilesetDocument tileset = TiledMapParser.ParseTileset(
            "<tileset name=\"props\" tilewidth=\"16\" tileheight=\"16\" tilecount=\"4\" columns=\"2\">" +
            "<properties><property name=\"theme\" value=\"dungeon\"/></properties>" +
            "<image source=\"tiles.png\"/>" +
            "<tile id=\"2\" type=\"wall\">" +
            "<properties><property name=\"collision\" value=\"blocking\"/></properties>" +
            "<objectgroup><object id=\"1\" x=\"0\" y=\"0\" width=\"16\" height=\"8\"/></objectgroup>" +
            "</tile></tileset>",
            "Tiled/props.tsx");

        //Assert
        tileset.Properties["THEME"].Should().Be("dungeon");
        TiledTilesetTile tile = tileset.Tiles.Single();
        tile.Id.Should().Be(2);
        tile.Type.Should().Be("wall");
        tile.Properties["collision"].Should().Be("blocking");
        tile.HasCollisionShapes.Should().BeTrue();
    }

    [Fact]
    public void ParseTileset_recognizes_a_tile_set_that_holds_one_image_per_tile()
    {
        //Act
        TiledTilesetDocument tileset = TiledMapParser.ParseTileset(
            "<tileset name=\"collection\" tilewidth=\"16\" tileheight=\"16\" tilecount=\"2\" columns=\"0\">" +
            "<tile id=\"0\"><image source=\"a.png\" width=\"16\" height=\"16\"/></tile>" +
            "<tile id=\"1\"><image source=\"b.png\" width=\"16\" height=\"16\"/></tile></tileset>",
            "Tiled/collection.tsx");

        //Assert
        tileset.IsImageCollection.Should().BeTrue();
        tileset.ImagePath.Should().BeEmpty();
        string?[] tileImages = [.. tileset.Tiles.Select(t => t.ImagePath)];
        string?[] expectedImages = ["a.png", "b.png"];
        tileImages.Should().Equal(expectedImages);
    }

    [Fact]
    public void ParseMap_keeps_object_layers_as_data()
    {
        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(
                layerXml: CsvLayer("Tiles", "1,1,1,1"),
                extraXml:
                    "<objectgroup id=\"3\" name=\"Spawns\" offsetx=\"4\" offsety=\"6\">" +
                    "<properties><property name=\"purpose\" value=\"logic\"/></properties>" +
                    "<object id=\"7\" name=\"player\" type=\"spawn\" x=\"32\" y=\"48\" width=\"16\" " +
                    "height=\"24\" rotation=\"90\" gid=\"5\">" +
                    "<properties><property name=\"lives\" value=\"3\"/></properties></object>" +
                    "</objectgroup>"),
            "Tiled/objects.tmx");

        //Assert
        TiledObjectLayer layer = map.ObjectLayers.Single();
        layer.Name.Should().Be("Spawns");
        layer.Id.Should().Be(3);
        layer.OffsetX.Should().Be(4.0f);
        layer.OffsetY.Should().Be(6.0f);
        layer.Properties["purpose"].Should().Be("logic");
        layer.DocumentIndex.Should().Be(1);

        TiledMapObject mapObject = layer.Objects.Single();
        mapObject.Id.Should().Be(7);
        mapObject.Name.Should().Be("player");
        mapObject.Type.Should().Be("spawn");
        mapObject.X.Should().Be(32.0f);
        mapObject.Y.Should().Be(48.0f);
        mapObject.Width.Should().Be(16.0f);
        mapObject.Height.Should().Be(24.0f);
        mapObject.Rotation.Should().Be(90.0f);
        mapObject.Gid.Should().Be(5u);
        mapObject.Properties["lives"].Should().Be("3");
    }

    [Fact]
    public void ParseMap_notes_an_image_layer_it_cannot_reproduce()
    {
        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(
                layerXml: CsvLayer("Tiles", "1,1,1,1"),
                extraXml:
                    "<imagelayer id=\"9\" name=\"Sky\" offsetx=\"2\" offsety=\"3\">" +
                    "<image source=\"../Backgrounds/sky.png\"/></imagelayer>"),
            "Tiled/images.tmx");

        //Assert
        TiledImageLayer layer = map.ImageLayers.Single();
        layer.Name.Should().Be("Sky");
        layer.ImagePath.Should().Be("../Backgrounds/sky.png");
        layer.OffsetX.Should().Be(2.0f);
        map.Warnings.Should().Contain(w => w.Contains("Sky"));
    }

    [Fact]
    public void ParseMap_notes_a_layer_group_it_does_not_descend_into()
    {
        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(
                layerXml: CsvLayer("Tiles", "1,1,1,1"),
                extraXml: "<group name=\"Decor\">" + CsvLayer("Inside", "1,1,1,1") + "</group>"),
            "Tiled/groups.tmx");

        //Assert
        map.TileLayers.Should().ContainSingle();
        map.Warnings.Should().Contain(w => w.Contains("Decor"));
    }

    [Fact]
    public void ParseMap_keeps_layer_visibility_opacity_offset_and_parallax()
    {
        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(
                "<layer id=\"4\" name=\"Back\" width=\"2\" height=\"2\" visible=\"0\" opacity=\"0.4\" " +
                "offsetx=\"-8\" offsety=\"12\" parallaxx=\"0.5\" parallaxy=\"0.75\">" +
                "<properties><property name=\"mood\">calm</property></properties>" +
                "<data encoding=\"csv\">1,1,1,1</data></layer>"),
            "Tiled/layer.tmx");

        //Assert
        TiledTileLayer layer = map.TileLayers.Single();
        layer.Id.Should().Be(4);
        layer.Visible.Should().BeFalse();
        layer.Opacity.Should().Be(0.4f);
        layer.OffsetX.Should().Be(-8.0f);
        layer.OffsetY.Should().Be(12.0f);
        layer.ParallaxX.Should().Be(0.5f);
        layer.ParallaxY.Should().Be(0.75f);
        layer.Properties["mood"].Should().Be("calm");
    }

    [Fact]
    public void ParseMap_reads_base64_layer_data_without_compression()
    {
        //Arrange
        uint[] gids = [1u, 2u, 3u, 2147483775u];

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(Base64Layer("Tiles", gids, compression: null)), "Tiled/base64.tmx");

        //Assert
        map.TileLayers.Single().Gids.Should().Equal(gids);
    }

    [Fact]
    public void ParseMap_reads_base64_layer_data_compressed_with_zlib()
    {
        //Arrange
        uint[] gids = [5u, 6u, 7u, 8u];

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(Base64Layer("Tiles", gids, "zlib")), "Tiled/zlib.tmx");

        //Assert
        map.TileLayers.Single().Gids.Should().Equal(gids);
    }

    [Fact]
    public void ParseMap_reads_base64_layer_data_compressed_with_gzip()
    {
        //Arrange
        uint[] gids = [9u, 10u, 11u, 12u];

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(Base64Layer("Tiles", gids, "gzip")), "Tiled/gzip.tmx");

        //Assert
        map.TileLayers.Single().Gids.Should().Equal(gids);
    }

    [Fact]
    public void ParseMap_refuses_an_infinite_map_and_says_so()
    {
        //Arrange
        string xml = BuildMap(CsvLayer("Tiles", "1,1,1,1"), infinite: true);

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/infinite.tmx"));

        //Assert
        exception.Message.Should().Contain("infinite map");
        exception.Message.Should().Contain("finite");
        exception.DocumentPath.Should().Be("Tiled/infinite.tmx");
    }

    [Theory]
    [InlineData("isometric")]
    [InlineData("hexagonal")]
    [InlineData("staggered")]
    public void ParseMap_refuses_an_orientation_it_cannot_lay_out_and_names_it(string orientation)
    {
        //Arrange
        string xml = BuildMap(CsvLayer("Tiles", "1,1,1,1"), orientation: orientation);

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/other.tmx"));

        //Assert
        exception.Message.Should().Contain(orientation);
        exception.Message.Should().Contain("orthogonal");
    }

    [Fact]
    public void ParseMap_refuses_an_encoding_it_cannot_read_and_names_the_ones_it_can()
    {
        //Arrange
        string xml = BuildMap(
            "<layer name=\"Tiles\" width=\"2\" height=\"2\">" +
            "<data encoding=\"json\">[1,1,1,1]</data></layer>");

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/json.tmx"));

        //Assert
        exception.Message.Should().Contain("json");
        exception.Message.Should().Contain("CSV and base64");
    }

    [Fact]
    public void ParseMap_refuses_unencoded_tile_elements_and_names_the_encodings_it_can_read()
    {
        //Arrange
        string xml = BuildMap(
            "<layer name=\"Tiles\" width=\"2\" height=\"2\"><data>" +
            "<tile gid=\"1\"/><tile gid=\"1\"/><tile gid=\"1\"/><tile gid=\"1\"/></data></layer>");

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/xmltiles.tmx"));

        //Assert
        exception.Message.Should().Contain("unencoded XML elements");
        exception.Message.Should().Contain("CSV and base64");
    }

    [Fact]
    public void ParseMap_refuses_a_compression_it_cannot_decompress_and_names_it()
    {
        //Arrange
        string xml = BuildMap(
            "<layer name=\"Tiles\" width=\"2\" height=\"2\">" +
            "<data encoding=\"base64\" compression=\"zstd\">AAAA</data></layer>");

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/zstd.tmx"));

        //Assert
        exception.Message.Should().Contain("zstd");
        exception.Message.Should().Contain("CSV and base64");
    }

    [Fact]
    public void ParseMap_refuses_layer_data_that_does_not_fill_the_layer()
    {
        //Arrange
        string xml = BuildMap(CsvLayer("Tiles", "1,1,1"));

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/short.tmx"));

        //Assert
        exception.Message.Should().Contain("3 tile values");
        exception.Message.Should().Contain("4");
    }

    [Fact]
    public void ParseMap_refuses_a_map_with_no_tile_layers()
    {
        //Arrange
        string xml = BuildMap(layerXml: string.Empty);

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/empty.tmx"));

        //Assert
        exception.Message.Should().Contain("no tile layers");
    }

    [Fact]
    public void ParseMap_refuses_a_map_with_no_tile_sets()
    {
        //Arrange
        string xml =
            "<map orientation=\"orthogonal\" width=\"2\" height=\"2\" tilewidth=\"16\" tileheight=\"16\">" +
            CsvLayer("Tiles", "1,1,1,1") + "</map>";

        //Act
        TiledMapParseException exception = Assert.Throws<TiledMapParseException>(
            () => TiledMapParser.ParseMap(xml, "Tiled/notilesets.tmx"));

        //Assert
        exception.Message.Should().Contain("no tile sets");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<tileset name=\"x\" tilewidth=\"16\" tileheight=\"16\"/>")]
    [InlineData("<map><not-xml")]
    public void ParseMap_refuses_a_document_that_is_not_a_map(string? xml)
    {
        //Arrange
        Action act = () => TiledMapParser.ParseMap(xml, "Tiled/whatever.tmx");

        //Act, Assert
        act.Should().Throw<TiledMapParseException>();
    }

    [Fact]
    public void ParseTileset_refuses_a_tile_set_with_no_image_at_all()
    {
        //Arrange
        Action act = () => TiledMapParser.ParseTileset(
            "<tileset name=\"x\" tilewidth=\"16\" tileheight=\"16\" tilecount=\"4\" columns=\"2\"/>",
            "Tiled/x.tsx");

        //Act, Assert
        act.Should().Throw<TiledMapParseException>();
    }

    [Fact]
    public void ParseTileset_refuses_a_grid_tile_set_with_no_column_count()
    {
        //Arrange
        Action act = () => TiledMapParser.ParseTileset(
            "<tileset name=\"x\" tilewidth=\"16\" tileheight=\"16\" tilecount=\"4\">" +
            "<image source=\"x.png\"/></tileset>",
            "Tiled/x.tsx");

        //Act, Assert
        act.Should().Throw<TiledMapParseException>();
    }

    [Fact]
    public void TryParseMap_hands_back_the_reason_instead_of_throwing()
    {
        //Arrange
        string xml = BuildMap(CsvLayer("Tiles", "1,1,1,1"), orientation: "isometric");

        //Act
        bool parsed = TiledMapParser.TryParseMap(
            xml, "Tiled/iso.tmx", out TiledMapDocument? map, out string? error);

        //Assert
        parsed.Should().BeFalse();
        map.Should().BeNull();
        error.Should().Contain("isometric");
    }

    [Fact]
    public void TryParseTileset_hands_back_the_reason_instead_of_throwing()
    {
        //Act
        bool parsed = TiledMapParser.TryParseTileset(
            "<tileset name=\"x\"/>", "Tiled/x.tsx", out TiledTilesetDocument? tileset, out string? error);

        //Assert
        parsed.Should().BeFalse();
        tileset.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void ParseMap_reads_an_inline_tile_set()
    {
        //Arrange
        string xml =
            "<map orientation=\"orthogonal\" width=\"2\" height=\"2\" tilewidth=\"16\" tileheight=\"16\">" +
            "<tileset firstgid=\"1\" name=\"inline\" tilewidth=\"16\" tileheight=\"16\" tilecount=\"4\" " +
            "columns=\"2\"><image source=\"tiles.png\" width=\"32\" height=\"32\"/></tileset>" +
            CsvLayer("Tiles", "1,2,3,4") + "</map>";

        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(xml, "Tiled/inline.tmx");

        //Assert
        map.Tilesets.Single().Source.Should().BeNull();
        map.Tilesets.Single().Inline!.Name.Should().Be("inline");
        map.Tilesets.Single().Inline!.ImagePath.Should().Be("tiles.png");
    }

    [Fact]
    public void ParseMap_keeps_map_properties()
    {
        //Act
        TiledMapDocument map = TiledMapParser.ParseMap(
            BuildMap(
                CsvLayer("Tiles", "1,1,1,1"),
                extraXml: "<properties><property name=\"world\" value=\"forest\"/></properties>"),
            "Tiled/props.tmx");

        //Assert
        map.Properties["world"].Should().Be("forest");
    }

    private static string BuildMap(
        string layerXml,
        string extraXml = "",
        string orientation = "orthogonal",
        bool infinite = false)
    {
        string infiniteAttribute = infinite ? " infinite=\"1\"" : " infinite=\"0\"";
        return
            $"<map version=\"1.10\" orientation=\"{orientation}\" renderorder=\"right-down\" width=\"2\" " +
            $"height=\"2\" tilewidth=\"16\" tileheight=\"16\"{infiniteAttribute}>" +
            "<tileset firstgid=\"1\" source=\"tiles.tsx\"/>" +
            layerXml +
            extraXml +
            "</map>";
    }

    private static string CsvLayer(string name, string csv) =>
        $"<layer name=\"{name}\" width=\"2\" height=\"2\"><data encoding=\"csv\">{csv}</data></layer>";

    private static string Base64Layer(string name, uint[] gids, string? compression)
    {
        byte[] raw = new byte[gids.Length * 4];
        for (int index = 0; index < gids.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(index * 4, 4), gids[index]);
        }

        byte[] payload = compression switch
        {
            null => raw,
            "zlib" => Compress(raw, stream => new ZLibStream(stream, CompressionLevel.Optimal)),
            "gzip" => Compress(raw, stream => new GZipStream(stream, CompressionLevel.Optimal)),
            _ => throw new ArgumentOutOfRangeException(nameof(compression)),
        };

        string compressionAttribute = compression is null ? string.Empty : $" compression=\"{compression}\"";
        return
            $"<layer name=\"{name}\" width=\"2\" height=\"2\">" +
            $"<data encoding=\"base64\"{compressionAttribute}>{Convert.ToBase64String(payload)}</data></layer>";
    }

    private static byte[] Compress(byte[] data, Func<Stream, Stream> wrap)
    {
        using MemoryStream output = new();
        using (Stream compressor = wrap(output))
        {
            compressor.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
