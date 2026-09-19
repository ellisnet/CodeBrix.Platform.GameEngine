using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates the key scheme and the catalog the provider serves from: which files become assets, what
/// key each one takes, how collisions are settled, and what a descriptor carries.
/// </summary>
public class KenneyPackIndexTests : IDisposable
{
    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _scratchFolders = [];

    /// <summary>
    /// Closes every source and deletes every scratch folder the tests in this class created.
    /// </summary>
    public void Dispose()
    {
        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_rejects_a_provider_id_holding_a_colon()
    {
        //Arrange
        Action act = () => new KenneyPackIndex("kenney:extra");

        //Act, Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_materializable_asset_is_keyed_without_its_extension()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);

        //Act
        bool found = index.TryGetDescriptor(
            "kenney:puzzle-pack/PNG/Double/ballBlue", out GameAssetDescriptor? descriptor);

        //Assert
        found.Should().BeTrue();
        descriptor!.Kind.Should().Be(GameAssetKind.Image);
        descriptor.Name.Should().Be("ballBlue");
        descriptor.Path.Should().Be("PNG/Double/ballBlue.png");
        descriptor.Pack.Should().Be("puzzle-pack");
        descriptor.ProviderId.Should().Be("kenney");
        descriptor.SizeBytes.Should().BeGreaterThan(0L);
        descriptor.Properties[KenneyAssetProperties.Materializable].Should().Be("true");
    }

    [Fact]
    public void Keys_are_case_insensitive_and_the_provider_prefix_is_optional()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);

        //Act, Assert
        index.TryGetEntry("KENNEY:PUZZLE-PACK/png/double/ballblue", out _).Should().BeTrue();
        index.TryGetEntry("puzzle-pack/PNG/Double/ballBlue", out _).Should().BeTrue();
        index.TryGetEntry("other:puzzle-pack/PNG/Double/ballBlue", out _).Should().BeFalse();
        index.TryGetEntry(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Folder_names_keep_same_named_images_apart()
    {
        //Arrange
        // PNG/Default and PNG/Double hold 87 identically named files each; the path is what separates
        // them.
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);

        //Act
        bool defaultFound = index.TryGetEntry("puzzle-pack/PNG/Default/ballBlue", out _);
        bool doubleFound = index.TryGetEntry("puzzle-pack/PNG/Double/ballBlue", out _);

        //Assert
        defaultFound.Should().BeTrue();
        doubleFound.Should().BeTrue();
    }

    [Fact]
    public void A_document_keeps_its_extension_and_is_not_materializable()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);

        //Act
        bool found = index.TryGetDescriptor(
            "kenney:puzzle-pack/License.txt", out GameAssetDescriptor? descriptor);

        //Assert
        found.Should().BeTrue();
        descriptor!.Kind.Should().Be(GameAssetKind.Document);
        descriptor.Properties[KenneyAssetProperties.Materializable].Should().Be("false");
        index.TryGetEntry("kenney:puzzle-pack/License", out _).Should().BeFalse();
    }

    [Fact]
    public void A_sprite_atlas_is_one_asset_and_its_sheet_image_is_not_listed_on_its_own()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);

        //Act
        bool atlasFound = index.TryGetDescriptor(
            "kenney:puzzle-pack/Spritesheet/spritesheet_default", out GameAssetDescriptor? atlas);

        //Assert
        atlasFound.Should().BeTrue();
        atlas!.Kind.Should().Be(GameAssetKind.SpriteAtlas);
        atlas.Path.Should().Be("Spritesheet/spritesheet_default.xml");
        atlas.Properties[KenneyAssetProperties.AtlasFrameCount].Should().Be("86");
        atlas.Properties[KenneyAssetProperties.AtlasImagePath]
            .Should().Be("Spritesheet/spritesheet_default.png");

        index.Descriptors.Should().NotContain(d => d.Path == "Spritesheet/spritesheet_default.png");
    }

    [Fact]
    public void A_tile_set_document_is_not_an_asset_and_its_image_is_reached_through_the_map()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.SimulatedBundleFileName);

        //Act
        IReadOnlyList<GameAssetDescriptor> all = index.Describe();

        //Assert
        all.Should().NotContain(d => d.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        all.Should().NotContain(d => d.Path == "Tilemap/tilemap_packed.png");
        all.Should().NotContain(d => d.Path == "Tilemap/tilemap-characters_packed.png");
    }

    [Fact]
    public void A_tile_map_is_keyed_without_its_extension_and_carries_its_geometry()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.SimulatedBundleFileName);

        //Act
        bool found = index.TryGetDescriptor(
            "kenney:simulated-bundle/Tiled/tilemap-example-a", out GameAssetDescriptor? map);

        //Assert
        found.Should().BeTrue();
        map!.Kind.Should().Be(GameAssetKind.TiledMap);
        map.Properties[KenneyAssetProperties.MapWidth].Should().Be("26");
        map.Properties[KenneyAssetProperties.MapHeight].Should().Be("15");
        map.Properties[KenneyAssetProperties.TileWidth].Should().Be("18");
        map.Properties[KenneyAssetProperties.TileLayerCount].Should().Be("4");
        map.Properties[KenneyAssetProperties.TilesetCount].Should().Be("2");
        map.Properties.ContainsKey(KenneyAssetProperties.TiledMapError).Should().BeFalse();
    }

    [Fact]
    public void A_gltf_model_is_materializable_while_its_other_formats_are_only_listed()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.BlockyCharactersFileName);

        //Act
        bool glbFound = index.TryGetDescriptor(
            "kenney:blocky-characters/Models/GLB format/character-a", out GameAssetDescriptor? glb);
        bool objFound = index.TryGetDescriptor(
            "kenney:blocky-characters/Models/OBJ format/character-a.obj", out GameAssetDescriptor? obj);
        bool mtlFound = index.TryGetDescriptor(
            "kenney:blocky-characters/Models/OBJ format/character-a.mtl", out GameAssetDescriptor? mtl);

        //Assert
        glbFound.Should().BeTrue();
        glb!.Kind.Should().Be(GameAssetKind.Model3D);
        glb.Properties[KenneyAssetProperties.Materializable].Should().Be("true");

        //A model and its material would share one key if the extension were dropped
        objFound.Should().BeTrue();
        obj!.Properties[KenneyAssetProperties.Materializable].Should().Be("false");
        mtlFound.Should().BeTrue();
        mtl!.Properties[KenneyAssetProperties.Materializable].Should().Be("false");
    }

    [Fact]
    public void Stray_files_are_listed_rather_than_treated_as_errors()
    {
        //Arrange
        KenneyPackIndex soundsIndex = IndexOf(TestFixtures.SciFiSoundsFileName);
        KenneyPackIndex bundleIndex = IndexOf(TestFixtures.SimulatedBundleFileName);

        //Act
        bool iniFound = soundsIndex.TryGetDescriptor(
            "kenney:sci-fi-sounds/Audio/desktop.ini", out GameAssetDescriptor? ini);
        bool blendFound = bundleIndex.TryGetDescriptor(
            "kenney:simulated-bundle/Extras/characterMedium.blend", out GameAssetDescriptor? blend);
        bool swfFound = bundleIndex.TryGetDescriptor(
            "kenney:simulated-bundle/Extras/nautical_vector.swf", out _);

        //Assert
        iniFound.Should().BeTrue();
        ini!.Kind.Should().Be(GameAssetKind.Other);
        blendFound.Should().BeTrue();
        blend!.Kind.Should().Be(GameAssetKind.Other);
        swfFound.Should().BeTrue();
        soundsIndex.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Every_key_in_an_index_is_unique()
    {
        //Arrange
        KenneyPackIndex index = new();

        //Act
        foreach (string bundle in new[]
        {
            TestFixtures.PuzzlePackFileName,
            TestFixtures.SciFiSoundsFileName,
            TestFixtures.SimulatedBundleFileName,
            TestFixtures.BlockyCharactersFileName,
            TestFixtures.BrickKitFileName,
        })
        {
            index.AddSource(Open(bundle));
        }

        //Assert
        string[] keys = [.. index.Descriptors.Select(d => d.Key)];
        keys.Should().OnlyHaveUniqueItems();
        index.Count.Should().Be(keys.Length);
        index.Packs.Count.Should().Be(5);
    }

    [Fact]
    public void Two_packs_wanting_the_same_slug_are_kept_apart_by_a_suffix()
    {
        //Arrange
        KenneyPackIndex index = new();

        //Act
        index.AddSource(Open(TestFixtures.PuzzlePackFileName));
        index.AddSource(Open(TestFixtures.PuzzlePackFileName));

        //Assert
        string[] slugs = [.. index.Packs.Select(p => p.Slug)];
        string[] expected = ["puzzle-pack", "puzzle-pack-2"];
        slugs.Should().Equal(expected);
        index.TryGetEntry("puzzle-pack-2/PNG/Double/ballBlue", out _).Should().BeTrue();
    }

    [Fact]
    public void Two_materializable_assets_wanting_one_key_are_settled_by_kind_then_path()
    {
        //Arrange
        // hero.png and hero.ogg would both be keyed "Art/hero"; Image sorts before Audio, so the image
        // keeps the short key and the sound keeps its extension.
        string zipPath = BuildBundle("collision", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Collision Pack", "1.0")),
            ["Art/hero.png"] = TestFixtures.FakeImage(),
            ["Art/hero.ogg"] = TestFixtures.FakeImage(),
        });
        KenneyPackIndex index = new();

        //Act
        index.AddSource(Open(zipPath));

        //Assert
        index.TryGetDescriptor("kenney:collision-pack/Art/hero", out GameAssetDescriptor? kept)
            .Should().BeTrue();
        kept!.Kind.Should().Be(GameAssetKind.Image);
        index.TryGetDescriptor("kenney:collision-pack/Art/hero.ogg", out GameAssetDescriptor? moved)
            .Should().BeTrue();
        moved!.Kind.Should().Be(GameAssetKind.Audio);
        index.Count.Should().Be(3);
    }

    [Fact]
    public void An_atlas_whose_sheet_image_is_missing_stays_a_document_and_is_reported()
    {
        //Arrange
        string zipPath = BuildBundle("orphan-atlas", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Orphan Atlas", "1.0")),
            ["Spritesheet/sheet.xml"] = Encoding.UTF8.GetBytes(
                "<TextureAtlas imagePath=\"nowhere.png\">" +
                "<SubTexture name=\"a.png\" x=\"0\" y=\"0\" width=\"4\" height=\"4\"/></TextureAtlas>"),
        });
        KenneyPackIndex index = new();

        //Act
        index.AddSource(Open(zipPath));

        //Assert
        index.TryGetDescriptor("kenney:orphan-atlas/Spritesheet/sheet.xml", out GameAssetDescriptor? document)
            .Should().BeTrue();
        document!.Kind.Should().Be(GameAssetKind.Document);
        index.Warnings.Should().Contain(w => w.Contains("nowhere.png"));
    }

    [Fact]
    public void A_map_that_cannot_be_imported_is_still_listed_with_the_reason()
    {
        //Arrange
        string zipPath = BuildBundle("bad-map", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Bad Map Pack", "1.0")),
            ["Tiled/iso.tmx"] = Encoding.UTF8.GetBytes(
                "<map orientation=\"isometric\" width=\"2\" height=\"2\" tilewidth=\"16\" tileheight=\"16\">" +
                "</map>"),
        });
        KenneyPackIndex index = new();

        //Act
        index.AddSource(Open(zipPath));

        //Assert
        index.TryGetDescriptor("kenney:bad-map-pack/Tiled/iso", out GameAssetDescriptor? map)
            .Should().BeTrue();
        map!.Kind.Should().Be(GameAssetKind.TiledMap);
        map.Properties[KenneyAssetProperties.TiledMapError].Should().Contain("isometric");
        index.Warnings.Should().Contain(w => w.Contains("isometric"));
    }

    [Fact]
    public void Describe_filters_by_kind_pack_name_and_path()
    {
        //Arrange
        KenneyPackIndex index = new();
        index.AddSource(Open(TestFixtures.PuzzlePackFileName));
        index.AddSource(Open(TestFixtures.SciFiSoundsFileName));

        //Act
        IReadOnlyList<GameAssetDescriptor> audio =
            index.Describe(new GameAssetQuery { Kind = GameAssetKind.Audio });
        IReadOnlyList<GameAssetDescriptor> inPack =
            index.Describe(new GameAssetQuery { Pack = "PUZZLE-PACK" });
        IReadOnlyList<GameAssetDescriptor> named =
            index.Describe(new GameAssetQuery { NameContains = "ballblue" });
        IReadOnlyList<GameAssetDescriptor> underPath =
            index.Describe(new GameAssetQuery { PathPrefix = "PNG/Double/" });

        //Assert
        audio.Should().NotBeEmpty();
        audio.Should().AllSatisfy(d => d.Pack.Should().Be("sci-fi-sounds"));
        inPack.Should().AllSatisfy(d => d.Pack.Should().Be("puzzle-pack"));
        named.Count.Should().Be(2);
        underPath.Count.Should().Be(86);
    }

    [Fact]
    public void Descriptors_carry_the_pack_identity_of_their_pack()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.SimulatedBundleFileName);

        //Act
        GameAssetDescriptor descriptor = index.Descriptors[0];

        //Assert
        descriptor.Properties[KenneyAssetProperties.PackName].Should().Be("Simulated Bundle");
        descriptor.Properties[KenneyAssetProperties.PackVersion].Should().Be("1.0");
        descriptor.Properties[KenneyAssetProperties.License].Should().Be("Simulated Bundle (1.0)");
    }

    [Fact]
    public void The_same_asset_always_describes_to_the_same_descriptor_instance()
    {
        //Arrange
        // Later code compares descriptors, and a record holding a dictionary compares by reference, so
        // handing out a fresh instance per lookup would make equal assets look different.
        KenneyPackIndex index = IndexOf(TestFixtures.PuzzlePackFileName);
        const string key = "kenney:puzzle-pack/PNG/Double/ballBlue";

        //Act
        index.TryGetDescriptor(key, out GameAssetDescriptor? first);
        index.TryGetDescriptor(key, out GameAssetDescriptor? second);

        //Assert
        second.Should().BeSameAs(first);
        index.Descriptors.Should().Contain(first!);
    }

    [Fact]
    public void Describe_without_a_query_lists_every_asset_in_index_order()
    {
        //Arrange
        KenneyPackIndex index = IndexOf(TestFixtures.SciFiSoundsFileName);

        //Act
        IReadOnlyList<GameAssetDescriptor> described = index.Describe();

        //Assert
        described.Count.Should().Be(index.Count);
        described.Count(d => d.Kind == GameAssetKind.Audio).Should().Be(73);
    }

    private KenneyPackIndex IndexOf(string bundleFileName)
    {
        KenneyPackIndex index = new();
        index.AddSource(Open(bundleFileName));
        return index;
    }

    private KenneyAssetSource Open(string bundleFileNameOrPath)
    {
        string path = Path.IsPathRooted(bundleFileNameOrPath)
            ? bundleFileNameOrPath
            : TestFixtures.BundlePath(bundleFileNameOrPath);
        KenneyAssetSource source = KenneyAssetSource.Open(path);
        _sources.Add(source);
        return source;
    }

    private string BuildBundle(string purpose, IReadOnlyDictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);
        string zipPath = Path.Combine(folder, $"kenney_{purpose}.zip");
        TestFixtures.BuildZip(zipPath, entries);
        return zipPath;
    }
}
