using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the provider itself: the keys it builds, the catalog it describes and filters, the raw
/// bytes it hands out, the pack summaries and warnings it publishes, adding sources to a provider
/// that is already serving, the assets it refuses, and what disposing it does.
/// </summary>
public class KenneyGameAssetProviderTests : IDisposable
{
    private const string SimulatedSlug = "simulated-bundle";
    private const string PuzzleSlug = "puzzle-pack";

    private readonly List<KenneyGameAssetProvider> _providers = [];
    private readonly List<string> _scratchFolders = [];

    /// <summary>
    /// Starts every test with an empty tilesheet registry, which is process-global.
    /// </summary>
    public KenneyGameAssetProviderTests()
    {
        TilesheetRegistry.Instance.Clear();
    }

    /// <summary>
    /// Disposes every provider this fixture built, clears the tilesheet registry it filled and deletes
    /// every scratch folder it created.
    /// </summary>
    public void Dispose()
    {
        TilesheetRegistry.Instance.Clear();

        foreach (KenneyGameAssetProvider provider in _providers) { provider.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ProviderId_namespaces_every_key_the_provider_offers()
    {
        //Arrange & Act
        KenneyGameAssetProvider standard = Provider(TestFixtures.SimulatedBundleFileName);
        KenneyGameAssetProvider mods = Provider(
            new KenneyAssetsOptions
            {
                ProviderId = "mods",
                Sources = [TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName)],
            });

        //Assert
        standard.ProviderId.Should().Be(KenneyGameAssetProvider.DefaultProviderId);
        standard.Describe().Should().AllSatisfy(
            descriptor => descriptor.Key.Should().StartWith("kenney:"));
        mods.ProviderId.Should().Be("mods");
        mods.Describe().Should().AllSatisfy(descriptor => descriptor.Key.Should().StartWith("mods:"));
        mods.TryDescribe($"mods:{SimulatedSlug}/Fonts/Kenney Space", out GameAssetDescriptor? font)
            .Should().BeTrue();
        font!.Kind.Should().Be(GameAssetKind.Font);
    }

    [Fact]
    public void SupportedKinds_lists_every_kind_the_provider_can_materialize()
    {
        //Arrange & Act
        KenneyGameAssetProvider provider = Provider();

        //Assert
        GameAssetKind[] expected =
        [
            GameAssetKind.Image,
            GameAssetKind.SpriteAtlas,
            GameAssetKind.Audio,
            GameAssetKind.Font,
            GameAssetKind.Vector,
            GameAssetKind.TiledMap,
            GameAssetKind.Model3D,
        ];
        provider.SupportedKinds.Count.Should().Be(expected.Length);
        provider.SupportedKinds.Should().Contain(expected);
        provider.SupportedKinds.Should().NotContain(GameAssetKind.Document);
        provider.SupportedKinds.Should().NotContain(GameAssetKind.Archive);
        provider.SupportedKinds.Should().NotContain(GameAssetKind.Other);
    }

    [Fact]
    public void Describe_lists_the_addressable_assets_of_every_registered_pack()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.SimulatedBundleFileName, TestFixtures.PuzzlePackFileName);

        //Act
        IReadOnlyList<GameAssetDescriptor> described = provider.Describe();

        //Assert
        described.Count.Should().Be(provider.AssetCount);
        described.Count.Should().Be(provider.Packs.Sum(pack => pack.AssetCount));
        described.Select(descriptor => descriptor.Pack).Distinct().Should()
            .BeEquivalentTo(new[] { SimulatedSlug, PuzzleSlug });
        described.Should().AllSatisfy(
            descriptor => descriptor.ProviderId.Should().Be(provider.ProviderId));
    }

    [Fact]
    public void Describe_filters_by_pack_kind_name_and_path_prefix()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.SimulatedBundleFileName, TestFixtures.PuzzlePackFileName);

        //Act
        IReadOnlyList<GameAssetDescriptor> audio =
            provider.Describe(new GameAssetQuery { Kind = GameAssetKind.Audio });
        IReadOnlyList<GameAssetDescriptor> puzzle =
            provider.Describe(new GameAssetQuery { Pack = PuzzleSlug });
        IReadOnlyList<GameAssetDescriptor> named =
            provider.Describe(new GameAssetQuery { NameContains = "radar" });
        IReadOnlyList<GameAssetDescriptor> underFonts =
            provider.Describe(new GameAssetQuery { PathPrefix = "Fonts/" });
        IReadOnlyList<GameAssetDescriptor> combined = provider.Describe(
            new GameAssetQuery { Pack = SimulatedSlug, Kind = GameAssetKind.Model3D });

        //Assert
        // The simulated bundle ships six sound effects and music loops.
        audio.Count.Should().Be(6);
        audio.Should().AllSatisfy(descriptor => descriptor.Kind.Should().Be(GameAssetKind.Audio));
        puzzle.Should().AllSatisfy(descriptor => descriptor.Pack.Should().Be(PuzzleSlug));
        puzzle.Count.Should().BeLessThan(provider.AssetCount);
        named.Should().ContainSingle();
        named[0].Key.Should().Be($"kenney:{SimulatedSlug}/Audio/radar2");
        underFonts.Count.Should().Be(3);
        underFonts.Should().AllSatisfy(descriptor => descriptor.Kind.Should().Be(GameAssetKind.Font));
        //Three glTF models, and the .fbx copies are in another pack
        combined.Count.Should().Be(3);
    }

    [Fact]
    public void TryDescribe_accepts_a_namespaced_or_a_bare_key_and_refuses_another_providers()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);
        const string path = "Tiled/tilemap-example-a";

        //Act
        bool namespaced = provider.TryDescribe(
            $"kenney:{SimulatedSlug}/{path}", out GameAssetDescriptor? fromNamespaced);
        bool bare = provider.TryDescribe($"{SimulatedSlug}/{path}", out GameAssetDescriptor? fromBare);
        bool foreign = provider.TryDescribe(
            $"other:{SimulatedSlug}/{path}", out GameAssetDescriptor? fromForeign);
        bool unknown = provider.TryDescribe("kenney:no-such-pack/nothing", out _);

        //Assert
        namespaced.Should().BeTrue();
        fromNamespaced!.Kind.Should().Be(GameAssetKind.TiledMap);
        fromNamespaced.Properties[KenneyAssetProperties.MapWidth].Should().Be("26");
        bare.Should().BeTrue();
        fromBare.Should().BeSameAs(fromNamespaced);
        foreign.Should().BeFalse();
        fromForeign.Should().BeNull();
        unknown.Should().BeFalse();
    }

    [Fact]
    public void OpenRaw_opens_an_asset_the_provider_cannot_materialize_too()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.BlockyCharactersFileName);
        GameAssetDescriptor licence = Descriptor(provider, "kenney:blocky-characters/License.txt");
        GameAssetDescriptor model = Descriptor(
            provider, "kenney:blocky-characters/Models/FBX format/character-a.fbx");

        //Act
        string licenceText;
        using (Stream stream = provider.OpenRaw(licence))
        {
            using StreamReader reader = new(stream);
            licenceText = reader.ReadToEnd();
        }

        long modelBytes;
        using (Stream stream = provider.OpenRaw(model)) { modelBytes = stream.Length; }

        //Assert
        licence.Properties[KenneyAssetProperties.Materializable].Should().Be("false");
        licenceText.Should().Contain("Kenney");
        model.Kind.Should().Be(GameAssetKind.Model3D);
        model.Properties[KenneyAssetProperties.Materializable].Should().Be("false");
        modelBytes.Should().BeGreaterThan(0L);
    }

    [Fact]
    public void A_descriptor_from_another_provider_is_not_found()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);
        GameAssetDescriptor foreign = new()
        {
            ProviderId = "other",
            Key = "other:pack/Audio/laser",
            Kind = GameAssetKind.Audio,
            Name = "laser",
        };
        GameAssetDescriptor unknown = Descriptor(
            provider, $"kenney:{SimulatedSlug}/Audio/radar2") with
        {
            Key = "kenney:simulated-bundle/Audio/no-such-sound",
        };

        //Act
        Action openForeign = () => provider.OpenRaw(foreign);
        Action materializeForeign = () => provider.MaterializeAudio(foreign);
        Action materializeUnknown = () => provider.MaterializeAudio(unknown);

        //Assert
        openForeign.Should().Throw<KeyNotFoundException>().WithMessage("*other*");
        materializeForeign.Should().Throw<KeyNotFoundException>();
        materializeUnknown.Should().Throw<KeyNotFoundException>().WithMessage("*no-such-sound*");
    }

    [Fact]
    public void Packs_summarize_what_each_registered_pack_holds()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);

        //Act
        KenneyPackSummary pack = Assert.Single(provider.Packs);

        //Assert
        pack.Slug.Should().Be(SimulatedSlug);
        pack.DisplayName.Should().Be("Simulated Bundle");
        pack.Version.Should().Be("1.0");
        pack.LicenseTitle.Should().Contain("Simulated Bundle");
        pack.SourcePath.Should().Be(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));
        pack.AssetCount.Should().Be(provider.Describe(new GameAssetQuery { Pack = pack.Slug }).Count);
        pack.MaterializableAssetCount.Should().BeLessThan(pack.AssetCount);
        pack.CountsByKind[GameAssetKind.Audio].Should().Be(6);
        pack.CountsByKind[GameAssetKind.Font].Should().Be(3);
        pack.CountsByKind[GameAssetKind.Vector].Should().Be(2);
        pack.CountsByKind[GameAssetKind.TiledMap].Should().Be(1);
        pack.CountsByKind[GameAssetKind.Model3D].Should().Be(3);
        pack.ToString().Should().Contain(SimulatedSlug);
    }

    [Fact]
    public void An_unreadable_source_becomes_a_warning_rather_than_a_failure()
    {
        //Arrange
        string missing = Path.Combine(TestFixtures.BundlePath("no-such-bundle.zip"));

        //Act
        KenneyGameAssetProvider provider = Provider(
            missing, TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Assert
        provider.Packs.Should().ContainSingle();
        provider.Warnings.Should().Contain(warning => warning.Contains("no-such-bundle.zip"));
        provider.TryDescribe($"kenney:{SimulatedSlug}/Audio/radar2", out _).Should().BeTrue();
    }

    [Fact]
    public void An_unreadable_source_throws_when_the_options_say_not_to_ignore_it()
    {
        //Arrange
        KenneyAssetsOptions options = new()
        {
            Sources = [TestFixtures.BundlePath("no-such-bundle.zip")],
            IgnoreUnreadableSources = false,
        };

        //Act
        Action act = () => Provider(options);

        //Assert
        act.Should().Throw<FileNotFoundException>().WithMessage("*no-such-bundle.zip*");
    }

    [Fact]
    public void AddSources_adds_to_the_provider_that_is_already_serving()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);
        int before = provider.AssetCount;

        //Act
        IReadOnlyList<string> warnings = provider.AddSources(
            TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Assert
        warnings.Should().BeEmpty();
        provider.Packs.Count.Should().Be(2);
        provider.AssetCount.Should().BeGreaterThan(before);
        //The keys the provider already had still resolve
        provider.TryDescribe($"kenney:{SimulatedSlug}/Audio/radar2", out _).Should().BeTrue();
        provider.TryDescribe($"kenney:{PuzzleSlug}/PNG/Double/ballBlue", out _).Should().BeTrue();
    }

    [Fact]
    public void AddNewSources_skips_a_source_that_is_already_registered()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);
        int before = provider.AssetCount;

        //Act
        KenneyAssetsRegistration registration = provider.AddNewSources(
            TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName),
            TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Assert - no second slug with a numeric suffix, and the new pack arrived
        provider.Packs.Select(pack => pack.Slug).Should().BeEquivalentTo([SimulatedSlug, PuzzleSlug]);
        provider.AssetCount.Should().BeGreaterThan(before);
        registration.Provider.Should().BeSameAs(provider);
        registration.Sources.Select(source => source.Status).Should().BeEquivalentTo(
            [KenneySourceStatus.AlreadyRegistered, KenneySourceStatus.Read]);
        registration.Sources[0].Packs.Should().ContainSingle().Which.Slug.Should().Be(SimulatedSlug);
        registration.Sources[1].Packs.Should().ContainSingle().Which.Slug.Should().Be(PuzzleSlug);
        registration.Packs.Count.Should().Be(2);
        registration.Unavailable.Should().BeEmpty();
    }

    [Fact]
    public void AddNewSources_recognizes_a_path_given_twice_in_one_call_and_a_relative_spelling()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(new KenneyAssetsOptions());
        string full = TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName);
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, full);

        //Act
        KenneyAssetsRegistration registration = provider.AddNewSources(full, relative);

        //Assert
        provider.Packs.Should().ContainSingle();
        registration.Sources.Select(source => source.Status).Should().BeEquivalentTo(
            [KenneySourceStatus.Read, KenneySourceStatus.AlreadyRegistered]);
        registration.Packs.Should().ContainSingle();
    }

    [Fact]
    public void AddNewSources_reports_a_missing_and_an_unreadable_source()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(new KenneyAssetsOptions());
        string folder = TestFixtures.CreateScratchFolder("broken");
        _scratchFolders.Add(folder);
        string broken = Path.Combine(folder, "kenney_broken.zip");
        File.WriteAllText(broken, "this is not a zip file");
        string missing = TestFixtures.BundlePath("no-such-bundle.zip");

        //Act
        KenneyAssetsRegistration registration = provider.AddNewSources(
            missing, broken, TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Assert
        registration.Sources.Select(source => source.Status).Should().BeEquivalentTo(
            [KenneySourceStatus.Missing, KenneySourceStatus.Unreadable, KenneySourceStatus.Read]);
        registration.Unavailable.Count.Should().Be(2);
        registration.Sources[0].Message.Should().Contain("no-such-bundle.zip");
        registration.Sources[1].Message.Should().Contain("kenney_broken.zip");
        registration.Sources[0].Packs.Should().BeEmpty();
        registration.Warnings.Should().Contain(warning => warning.Contains("no-such-bundle.zip"));
        provider.Packs.Should().ContainSingle();
    }

    [Fact]
    public void AddNewSources_gives_the_same_catalog_as_AddSources_for_a_source_read_once()
    {
        //Arrange
        KenneyGameAssetProvider viaAdd = Provider(new KenneyAssetsOptions());
        KenneyGameAssetProvider viaAddNew = Provider(new KenneyAssetsOptions());
        string[] paths =
        [
            TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName),
            TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName),
        ];

        //Act
        IReadOnlyList<string> warnings = viaAdd.AddSources(paths);
        KenneyAssetsRegistration registration = viaAddNew.AddNewSources(paths);

        //Assert
        registration.Warnings.Should().BeEquivalentTo(warnings);
        viaAddNew.Describe().Select(descriptor => descriptor.Key)
            .Should().BeEquivalentTo(viaAdd.Describe().Select(descriptor => descriptor.Key));
        viaAddNew.Packs.Should().BeEquivalentTo(viaAdd.Packs);
    }

    [Fact]
    public void AddNewSources_refuses_options_that_name_another_provider()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(new KenneyAssetsOptions());

        //Act
        Action act = () => provider.AddNewSources(new KenneyAssetsOptions { ProviderId = "mods" });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreditLines_lists_one_line_per_registered_pack()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.SimulatedBundleFileName, TestFixtures.PuzzlePackFileName);

        //Act
        IReadOnlyList<string> lines = provider.CreditLines;

        //Assert
        lines.Should().BeEquivalentTo(
            ["Simulated Bundle (1.0) - Kenney (CC0)", "Puzzle Pack (1.1) - Kenney (CC0)"],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void CheckKeys_reports_the_missing_keys_and_the_kind_of_each_found_one()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.PuzzlePackFileName);
        string atlas = $"kenney:{PuzzleSlug}/Spritesheet/spritesheet_default";
        string image = $"{PuzzleSlug}/PNG/Double/ballBlue";
        string missing = $"kenney:{PuzzleSlug}/PNG/Double/ballPurple";

        //Act
        KenneyKeyCheck check = provider.CheckKeys([atlas, image, missing, "other:thing"]);

        //Assert
        check.AllFound.Should().BeFalse();
        check.MissingKeys.Should().BeEquivalentTo([missing, "other:thing"]);
        check.Keys.Count.Should().Be(4);
        check[atlas].Kind.Should().Be(GameAssetKind.SpriteAtlas);
        check[atlas].AtlasFrameCount.Should().BeGreaterThan(0);
        check[image].Kind.Should().Be(GameAssetKind.Image);
        check[image].SizeBytes.Should().BeGreaterThan(0);
        check[missing].Kind.Should().Be(GameAssetKind.Unknown);
        check.CountsByKind[GameAssetKind.SpriteAtlas].Should().Be(1);
        check.CountsByKind[GameAssetKind.Image].Should().Be(1);
        check.CountsByKind.Should().NotContainKey(GameAssetKind.Unknown);
    }

    [Fact]
    public void CheckKeys_of_every_described_key_finds_them_all()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);

        //Act
        KenneyKeyCheck check = provider.CheckKeys(provider.Describe().Select(descriptor => descriptor.Key));

        //Assert
        check.AllFound.Should().BeTrue();
        check.Keys.Count.Should().Be(provider.AssetCount);
    }

    [Fact]
    public void GetKeyCatalog_lists_keys_by_pack_with_kinds_and_atlas_frame_names()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.SimulatedBundleFileName, TestFixtures.PuzzlePackFileName);

        //Act
        string catalog = provider.GetKeyCatalog();
        string[] lines = catalog.Split(Environment.NewLine);

        //Assert
        lines[0].Should().Be($"Asset keys of provider 'kenney': 2 pack(s), {provider.AssetCount} key(s).");
        lines.Should().Contain($"{SimulatedSlug} - Simulated Bundle (1.0) - {provider.Packs[0].AssetCount} key(s)");
        lines.Should().Contain($"{PuzzleSlug} - Puzzle Pack (1.1) - {provider.Packs[1].AssetCount} key(s)");
        lines.Should().Contain($"  kenney:{SimulatedSlug}/Audio/radar2  [Audio]");
        lines.Should().Contain(line => line.StartsWith(
            $"  kenney:{PuzzleSlug}/Spritesheet/spritesheet_default  [SpriteAtlas, ", StringComparison.Ordinal));
        //Assert - frame names are the region names, extension removed, indented under the atlas
        lines.Should().Contain("      ballBlue");
        lines.Should().NotContain("      ballBlue.png");
        lines.Should().Contain(line => line.EndsWith("listed only]", StringComparison.Ordinal));
        catalog.IndexOf(SimulatedSlug + " - ", StringComparison.Ordinal)
            .Should().BeLessThan(catalog.IndexOf(PuzzleSlug + " - ", StringComparison.Ordinal));
    }

    [Fact]
    public void GetKeyCatalog_frame_names_are_the_ones_the_materialized_atlas_answers_to()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.PuzzlePackFileName);
        string key = $"kenney:{PuzzleSlug}/Spritesheet/spritesheet_default";
        GameAssetDescriptor descriptor = Descriptor(provider, key);

        //Act
        string[] frames = provider
            .GetKeyCatalog(new GameAssetQuery { Kind = GameAssetKind.SpriteAtlas, NameContains = "spritesheet_default" })
            .Split(Environment.NewLine)
            .Where(line => line.StartsWith("      ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();
        Tilesheet sheet = provider.MaterializeTilesheet(descriptor);

        //Assert
        frames.Should().NotBeEmpty();
        frames.Should().AllSatisfy(frame => sheet.GetRegion(frame).Should().NotBeNull());
    }

    [Fact]
    public void WriteKeyCatalog_applies_the_query_and_leaves_out_packs_with_no_listed_key()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.SimulatedBundleFileName, TestFixtures.PuzzlePackFileName);
        using StringWriter writer = new();

        //Act
        provider.WriteKeyCatalog(writer, new GameAssetQuery { Kind = GameAssetKind.Audio });
        string[] lines = writer.ToString().Split(Environment.NewLine);

        //Assert
        lines[0].Should().Be("Asset keys of provider 'kenney': 1 pack(s), 6 key(s) matching the query.");
        lines.Should().NotContain(line => line.StartsWith(PuzzleSlug, StringComparison.Ordinal));
        lines.Where(line => line.StartsWith("  kenney:", StringComparison.Ordinal))
            .Should().AllSatisfy(line => line.Should().EndWith("[Audio]"));
    }

    [Fact]
    public void AddSources_refuses_options_that_name_another_provider()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider();
        KenneyAssetsOptions options = new()
        {
            ProviderId = "mods",
            Sources = [TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName)],
        };

        //Act
        Action act = () => provider.AddSources(options);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*mods*");
    }

    [Fact]
    public void A_folder_of_pack_folders_becomes_one_pack_each_only_when_asked_for()
    {
        //Arrange - two extracted bundles side by side under a folder with no licence of its own
        string collection = CollectionFolder();

        //Act
        KenneyGameAssetProvider asOnePack = Provider(collection);
        KenneyGameAssetProvider asAcollection = Provider(
            new KenneyAssetsOptions { Sources = [collection], RecursiveFolders = true });

        //Assert
        asOnePack.Packs.Should().ContainSingle();
        asAcollection.Packs.Count.Should().Be(2);
        //Both packs carry the same licence title, so the later arrival is made unique
        string[] slugs = [.. asAcollection.Packs.Select(pack => pack.Slug)];
        slugs.Should().BeEquivalentTo(new[] { PuzzleSlug, $"{PuzzleSlug}-2" });
        asAcollection.TryDescribe($"kenney:{PuzzleSlug}-2/PNG/Double/ballBlue", out _).Should().BeTrue();
    }

    [Fact]
    public void A_zip_and_a_folder_extracted_from_it_can_be_registered_together()
    {
        //Arrange
        string extracted = ExtractedBundle(TestFixtures.PuzzlePackFileName, "puzzle-folder");

        //Act
        KenneyGameAssetProvider provider = Provider(
            TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName), extracted);

        //Assert
        provider.Packs.Count.Should().Be(2);
        provider.Packs[0].Slug.Should().Be(PuzzleSlug);
        provider.Packs[1].Slug.Should().Be($"{PuzzleSlug}-2");
        provider.Packs[0].AssetCount.Should().Be(provider.Packs[1].AssetCount);
        provider.TryDescribe($"kenney:{PuzzleSlug}/PNG/Double/ballBlue", out _).Should().BeTrue();
        provider.TryDescribe($"kenney:{PuzzleSlug}-2/PNG/Double/ballBlue", out _).Should().BeTrue();
    }

    [Fact]
    public void An_asset_listed_for_discovery_only_is_refused_by_every_materializing_route()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.BlockyCharactersFileName);
        GameAssetDescriptor fbx = Descriptor(
            provider, "kenney:blocky-characters/Models/FBX format/character-a.fbx");
        GameAssetDescriptor licence = Descriptor(provider, "kenney:blocky-characters/License.txt");

        //Act
        Action asModel = () => provider.MaterializeModel(fbx);
        Action asSprites = () => provider.MaterializeTilesheet(fbx);
        Action asAudio = () => provider.MaterializeAudio(licence);

        //Assert
        UnsupportedGameAssetException thrown = asModel.Should()
            .Throw<UnsupportedGameAssetException>().Which;
        thrown.Kind.Should().Be(GameAssetKind.Model3D);
        thrown.Key.Should().Be(fbx.Key);
        thrown.Message.Should().Contain(UnsupportedGameAssetException.DefaultMessage);
        asSprites.Should().Throw<UnsupportedGameAssetException>();
        asAudio.Should().Throw<UnsupportedGameAssetException>()
            .Which.Kind.Should().Be(GameAssetKind.Document);
    }

    [Fact]
    public void An_image_collection_tile_set_does_not_hide_the_sprites_it_lists()
    {
        //Arrange - a pack whose tile set holds one image per tile, which is the shape three packs of
        //  Kenney's own collection ship. No map can be imported from such a tile set, so its pictures
        //  have to stay addressable as ordinary sprites.
        string bundle = SyntheticBundle("tile-collection", new Dictionary<string, byte[]>
        {
            ["Map/tile-a.png"] = TestFixtures.FakeImage(),
            ["Map/tile-b.png"] = TestFixtures.FakeImage(),
            ["Map/tiles.tsx"] = Utf8(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <tileset version="1.10" name="tiles" tilewidth="16" tileheight="16" tilecount="2">
                 <tile id="0"><image source="tile-a.png" width="16" height="16"/></tile>
                 <tile id="1"><image source="tile-b.png" width="16" height="16"/></tile>
                </tileset>
                """),
        });

        //Act
        KenneyGameAssetProvider provider = Provider(bundle);

        //Assert
        IReadOnlyList<GameAssetDescriptor> images =
            provider.Describe(new GameAssetQuery { Kind = GameAssetKind.Image });
        images.Count.Should().Be(2);
        images.Select(image => image.Name).Should().BeEquivalentTo(new[] { "tile-a", "tile-b" });
        provider.TryDescribe("kenney:tile-collection/Map/tile-a", out _).Should().BeTrue();
        //The tile set document itself is never an asset
        provider.Describe().Should().NotContain(asset => asset.Path.EndsWith(".tsx"));
    }

    [Fact]
    public void Warnings_also_carry_what_a_materialized_asset_had_to_give_up()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.PuzzlePackFileName);
        GameAssetDescriptor image = Descriptor(provider, $"kenney:{PuzzleSlug}/PNG/Double/ballBlue");

        //Act - a tile size of nothing cannot make a grid, but the picture is still usable
        Tilesheet sheet = provider.MaterializeTilesheet(
            image, new TilesheetMaterializeOptions { TileSize = new Size(0, 0) });

        //Assert
        sheet.Name.Should().Be(image.Key);
        provider.Warnings.Should().Contain(warning => warning.Contains(image.Key));
    }

    [Fact]
    public void Dispose_closes_the_archives_and_leaves_the_catalog_readable()
    {
        //Arrange
        KenneyGameAssetProvider provider = Provider(TestFixtures.SimulatedBundleFileName);
        GameAssetDescriptor image = Descriptor(provider, $"kenney:{SimulatedSlug}/PNG/tile_0079");
        Tilesheet registered = provider.MaterializeTilesheet(image);
        int assets = provider.AssetCount;

        //Act
        provider.Dispose();
        provider.Dispose();

        //Assert - the catalog is pure data and answers as before
        provider.AssetCount.Should().Be(assets);
        provider.Packs.Should().ContainSingle();
        provider.Describe().Count.Should().Be(assets);
        provider.TryDescribe(image.Key, out _).Should().BeTrue();

        //Assert - nothing can be read or materialized any more
        Action materialize = () => provider.MaterializeTilesheet(image);
        Action open = () => provider.OpenRaw(image);
        materialize.Should().Throw<ObjectDisposedException>();
        open.Should().Throw<ObjectDisposedException>();

        //Assert - what the engine was already given stays registered and usable
        TilesheetRegistry.Instance.TryGet(image.Key, out Tilesheet? stillThere).Should().BeTrue();
        stillThere.Should().BeSameAs(registered);
        registered.SkBitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_provider_identifier_that_would_make_keys_ambiguous_is_refused()
    {
        //Arrange
        Action withColon = () => new KenneyGameAssetProvider(
            new KenneyAssetsOptions { ProviderId = "ken:ney" });
        Action blank = () => new KenneyGameAssetProvider(new KenneyAssetsOptions { ProviderId = "  " });

        //Act & Assert
        withColon.Should().Throw<ArgumentException>().WithMessage("*colon*");
        blank.Should().Throw<ArgumentException>();
    }

    private static GameAssetDescriptor Descriptor(KenneyGameAssetProvider provider, string key)
    {
        provider.TryDescribe(key, out GameAssetDescriptor? descriptor).Should().BeTrue();

        return descriptor!;
    }

    private KenneyGameAssetProvider Provider(params string[] bundleFileNamesOrPaths)
    {
        string[] paths =
        [
            .. bundleFileNamesOrPaths.Select(
                path => Path.IsPathRooted(path) ? path : TestFixtures.BundlePath(path)),
        ];

        return Provider(new KenneyAssetsOptions { Sources = paths });
    }

    private KenneyGameAssetProvider Provider(KenneyAssetsOptions options)
    {
        KenneyGameAssetProvider provider = new(options);
        _providers.Add(provider);

        return provider;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    //A bundle built for a case no shipped fixture covers, written under the test output directory
    private string SyntheticBundle(string purpose, Dictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);

        entries["License.txt"] = Utf8(TestFixtures.LicenseText(purpose, "1.0"));
        string zipPath = Path.Combine(folder, $"kenney_{purpose}.zip");
        TestFixtures.BuildZip(zipPath, entries);

        return zipPath;
    }

    private string ExtractedBundle(string bundleFileName, string purpose)
    {
        string folder = TestFixtures.ExtractBundle(bundleFileName, purpose);
        _scratchFolders.Add(folder);

        return folder;
    }

    //The all-in-one shape: pack folders inside a folder that carries no licence of its own
    private string CollectionFolder()
    {
        string root = TestFixtures.CreateScratchFolder("collection");
        _scratchFolders.Add(root);

        foreach (string child in new[] { "2D assets/Puzzle Pack", "2D assets/Puzzle Pack Again" })
        {
            string folder = Path.Combine(root, child.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);
            ZipFile.ExtractToDirectory(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName), folder);
        }

        return root;
    }
}
