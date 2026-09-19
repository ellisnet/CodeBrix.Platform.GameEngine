using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates the three picture routes into the engine's tilesheet registry: a loose image as one
/// whole-image tile plus an optional grid, a sprite atlas as one named region per frame, and an SVG
/// rasterized at the size the options ask for. Also covers registering under another key, materializing
/// the same key twice, and the assets this materializer refuses.
/// </summary>
public class TilesheetMaterializerTests : IDisposable
{
    private const string ProviderId = KenneyPackIndex.DefaultProviderId;

    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _scratchFolders = [];
    private readonly TilesheetMaterializer _materializer = new();

    /// <summary>
    /// Starts every test with an empty tilesheet registry, which is process-global.
    /// </summary>
    public TilesheetMaterializerTests()
    {
        TilesheetRegistry.Instance.Clear();
    }

    /// <summary>
    /// Clears the tilesheet registry this fixture filled, closes every source it opened and deletes
    /// every scratch folder it created.
    /// </summary>
    public void Dispose()
    {
        TilesheetRegistry.Instance.Clear();

        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MaterializeTilesheet_registers_a_loose_image_as_one_whole_image_tile()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/PNG/Double/ballBlue");
        string key = entry.GetKey(ProviderId);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, key);

        //Assert
        sheet.Name.Should().Be(key);
        TilesheetRegistry.Instance.TryGet(key, out Tilesheet? registered).Should().BeTrue();
        registered.Should().BeSameAs(sheet);
        sheet.SkBitmap.Width.Should().Be(44);
        sheet.SkBitmap.Height.Should().Be(44);
        sheet.DefaultRegion.TileSize.Should().Be(new Size(44, 44));
        sheet.DefaultRegion.Columns.Should().Be(1);
        sheet.DefaultRegion.Rows.Should().Be(1);
        sheet[0, 0].SkBitmap!.Width.Should().Be(44);
        _materializer.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeTilesheet_adds_a_grid_region_beside_the_whole_image_tile()
    {
        //Arrange
        KenneyAssetEntry entry = SyntheticImageEntry("grid", 64, 32);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { TileSize = new Size(16, 16) });

        //Assert
        TilesheetRegion grid = sheet.GetRegion(TilesheetMaterializer.GridRegionName)!;
        grid.Should().NotBeNull();
        grid.Columns.Should().Be(4);
        grid.Rows.Should().Be(2);
        grid.Area.Should().Be(new Rectangle(0, 0, 64, 32));
        sheet[TilesheetMaterializer.GridRegionName, 3, 1].SkBitmap!.Width.Should().Be(16);
        //The whole-image tile is still there, so both addressings work on the one sheet
        sheet.DefaultRegion.TileSize.Should().Be(new Size(64, 32));
        _materializer.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeTilesheet_counts_grid_tiles_with_the_padding_and_margin_given()
    {
        //Arrange
        // 64 wide: 16-pixel tiles with a pixel of padding each side occupy 18, so three fit; two
        // pixels of margin each side leave 60, so three 16-pixel tiles fit there too.
        KenneyAssetEntry entry = SyntheticImageEntry("spaced", 64, 32);

        //Act
        Tilesheet padded = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions
            {
                TileSize = new Size(16, 16),
                Padding = new Spacing(1, 1, 1, 1),
                RegisterAs = "test:padded",
            });
        Tilesheet margined = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions
            {
                TileSize = new Size(16, 16),
                Margin = new Spacing(2, 2, 2, 2),
                RegisterAs = "test:margined",
            });

        //Assert
        TilesheetRegion paddedGrid = padded.GetRegion(TilesheetMaterializer.GridRegionName)!;
        paddedGrid.Columns.Should().Be(3);
        paddedGrid.Rows.Should().Be(1);
        paddedGrid.TilePadding.Should().Be(new Spacing(1, 1, 1, 1));

        TilesheetRegion marginedGrid = margined.GetRegion(TilesheetMaterializer.GridRegionName)!;
        marginedGrid.Columns.Should().Be(3);
        marginedGrid.Rows.Should().Be(1);
        marginedGrid.RegionMargin.Should().Be(new Spacing(2, 2, 2, 2));
    }

    [Fact]
    public void MaterializeTilesheet_warns_instead_of_adding_a_grid_of_no_usable_size()
    {
        //Arrange
        KenneyAssetEntry entry = SyntheticImageEntry("unusable-grid", 32, 32);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { TileSize = new Size(0, 16) });

        //Assert
        sheet.GetRegion(TilesheetMaterializer.GridRegionName).Should().BeNull();
        sheet.DefaultRegion.Columns.Should().Be(1);
        _materializer.Warnings.Should().ContainSingle();
        _materializer.Warnings[0].Should().Contain(entry.GetKey(ProviderId));
        _materializer.Warnings[0].Should().Contain("not positive");
    }

    [Fact]
    public void MaterializeTilesheet_warns_about_a_tile_size_too_large_for_the_image()
    {
        //Arrange
        KenneyAssetEntry entry = SyntheticImageEntry("oversized-grid", 32, 32);

        //Act
        _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { TileSize = new Size(48, 48) });

        //Assert
        _materializer.Warnings.Should().ContainSingle();
        _materializer.Warnings[0].Should().Contain("leaves no whole tile");
    }

    [Fact]
    public void MaterializeTilesheet_gives_every_region_it_creates_the_collision_asked_for()
    {
        //Arrange
        KenneyAssetEntry entry = SyntheticImageEntry("collision", 32, 32);
        CollisionAdjust adjust = new(1, 2, 3, 4);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions
            {
                TileSize = new Size(16, 16),
                CollisionType = TileCollisionType.Blocking,
                CollisionAdjust = adjust,
            });

        //Assert
        // A tile takes its collision from the region it comes from unless it is given its own, so every
        // tile of both regions is blocking without any per-frame override.
        sheet.DefaultRegion.CollisionType.Should().Be(TileCollisionType.Blocking);
        sheet.DefaultRegion.CollisionAdjust.Should().Be(adjust);
        Frame gridTile = sheet[TilesheetMaterializer.GridRegionName, 1, 1];
        gridTile.CollisionType.Should().Be(TileCollisionType.Blocking);
        gridTile.CollisionAdjust.Should().Be(adjust);
        gridTile.HasCollisionTypeOverride.Should().BeFalse();
    }

    [Fact]
    public void MaterializeTilesheet_gives_a_sprite_atlas_one_named_region_per_frame()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName),
            "kenney:puzzle-pack/Spritesheet/spritesheet_default");
        string key = entry.GetKey(ProviderId);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, key);

        //Assert
        sheet.SkBitmap.Width.Should().Be(364);
        sheet.Regions.Count(r => r.Name != TilesheetRegion.DefaultRegionName)
            .Should().Be(entry.SpriteAtlas!.Frames.Count);

        TilesheetRegion ball = sheet.GetRegion("ballBlue")!;
        ball.Should().NotBeNull();
        //The source rectangle is the one the atlas document writes
        ball.Area.Should().Be(new Rectangle(27, 338, 22, 22));
        ball.TileSize.Should().Be(new Size(22, 22));
        ball.Columns.Should().Be(1);
        ball.Rows.Should().Be(1);
        sheet["ballBlue", 0, 0].SkBitmap!.Width.Should().Be(22);
        _materializer.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeTilesheet_keeps_atlas_frame_extensions_when_asked_to()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName),
            "kenney:puzzle-pack/Spritesheet/spritesheet_default");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { StripFrameExtension = false });

        //Assert
        sheet.GetRegion("ballBlue.png").Should().NotBeNull();
        sheet.GetRegion("ballBlue").Should().BeNull();
    }

    [Theory]
    [InlineData("ballBlue.png", true, "ballBlue")]
    [InlineData("ballBlue.png", false, "ballBlue.png")]
    [InlineData("hud_1.5x.png", true, "hud_1.5x")]
    [InlineData("panel_blue", true, "panel_blue")]
    [InlineData("tile.9.gif", true, "tile.9")]
    [InlineData("no_image.wav", true, "no_image.wav")]
    public void RegionNameForFrame_removes_only_an_image_extension(
        string frameName, bool strip, string expected)
    {
        //Act
        string name = TilesheetMaterializer.RegionNameForFrame(frameName, strip);

        //Assert
        name.Should().Be(expected);
    }

    [Fact]
    public void MaterializeTilesheet_keeps_the_first_of_two_frames_whose_names_collide()
    {
        //Arrange
        // Two frames whose names differ only by image extension land on one region name once the
        // extension is stripped.
        KenneyAssetEntry entry = SyntheticAtlasEntry(
            "colliding",
            32,
            32,
            "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/>" +
            "<SubTexture name=\"hero.jpg\" x=\"8\" y=\"0\" width=\"16\" height=\"16\"/>");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, entry.GetKey(ProviderId));

        //Assert
        sheet.Regions.Count(r => r.Name != TilesheetRegion.DefaultRegionName).Should().Be(1);
        sheet.GetRegion("hero")!.Area.Should().Be(new Rectangle(0, 0, 8, 8));
        _materializer.Warnings.Should().ContainSingle();
        _materializer.Warnings[0].Should().Contain("already taken");
        _materializer.Warnings[0].Should().Contain("hero.jpg");
    }

    [Fact]
    public void MaterializeTilesheet_leaves_out_a_frame_that_falls_outside_the_sheet_image()
    {
        //Arrange
        KenneyAssetEntry entry = SyntheticAtlasEntry(
            "overhanging",
            16,
            16,
            "<SubTexture name=\"inside.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/>" +
            "<SubTexture name=\"outside.png\" x=\"10\" y=\"10\" width=\"20\" height=\"20\"/>");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, entry.GetKey(ProviderId));

        //Assert
        sheet.GetRegion("inside").Should().NotBeNull();
        sheet.GetRegion("outside").Should().BeNull();
        _materializer.Warnings.Should().ContainSingle();
        _materializer.Warnings[0].Should().Contain("outside");
    }

    [Fact]
    public void MaterializeTilesheet_resolves_a_stale_atlas_image_path_to_the_sibling_sheet()
    {
        //Arrange
        // Real bundles do this: the document names sprites.png while the sheet beside it carries the
        // document's own name.
        string zipPath = BuildBundle("stale-atlas", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Utf8(TestFixtures.LicenseText("Stale Atlas", "1.0")),
            ["Spritesheet/spritesheet_characters.xml"] = Utf8(
                "<TextureAtlas imagePath=\"sprites.png\">" +
                "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/>" +
                "</TextureAtlas>"),
            ["Spritesheet/spritesheet_characters.png"] = SolidPng(24, 24, SKColors.Orange),
        });
        KenneyAssetEntry entry = EntryOf(
            IndexOf(zipPath), "stale-atlas/Spritesheet/spritesheet_characters");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, entry.GetKey(ProviderId));

        //Assert
        sheet.SkBitmap.Width.Should().Be(24);
        sheet.GetRegion("hero").Should().NotBeNull();
        sheet["hero", 0, 0].SkBitmap!.Width.Should().Be(8);
    }

    [Fact]
    public void MaterializeTilesheet_reports_a_sprite_atlas_whose_sheet_image_is_gone()
    {
        //Arrange
        // The pack index only lists an atlas whose sheet image it found, so this is the case of a pack
        // that changed underneath the index - an extracted folder someone tidied up.
        string zipPath = BuildBundle("lost-sheet", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Utf8(TestFixtures.LicenseText("Lost Sheet", "1.0")),
            ["Spritesheet/sheet.xml"] = Utf8(
                "<TextureAtlas imagePath=\"gone.png\">" +
                "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/>" +
                "</TextureAtlas>"),
        });
        KenneyPack pack = Open(zipPath).Packs[0];
        SpriteAtlasParser.TryParse(
            pack.Archive.ReadText("Spritesheet/sheet.xml"),
            "Spritesheet/sheet.xml",
            out SpriteAtlasDocument? atlas).Should().BeTrue();
        KenneyAssetEntry entry = new(
            pack,
            new KenneyArchiveEntry("Spritesheet/sheet.xml", 128L),
            GameAssetKind.SpriteAtlas,
            "Spritesheet/sheet",
            isMaterializable: true,
            atlas);
        Action act = () => _materializer.MaterializeTilesheet(entry, entry.GetKey(ProviderId));

        //Act, Assert
        act.Should().Throw<FileNotFoundException>().WithMessage("*gone.png*");
        TilesheetRegistry.Instance.Count.Should().Be(0);
    }

    [Fact]
    public void MaterializeTilesheet_reads_an_atlas_the_same_way_from_a_zip_and_from_a_folder()
    {
        //Arrange
        KenneyAssetEntry fromZip = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName),
            "kenney:puzzle-pack/Spritesheet/spritesheet_default");
        KenneyAssetEntry fromFolder = EntryOf(
            IndexOf(ExtractedBundle(TestFixtures.PuzzlePackFileName, "puzzle-folder")),
            "kenney:puzzle-pack/Spritesheet/spritesheet_default");

        //Act
        Tilesheet zipSheet = _materializer.MaterializeTilesheet(fromZip, "test:from-zip");
        Tilesheet folderSheet = _materializer.MaterializeTilesheet(fromFolder, "test:from-folder");

        //Assert
        folderSheet.Should().NotBeSameAs(zipSheet);
        folderSheet.SkBitmap.Width.Should().Be(zipSheet.SkBitmap.Width);
        folderSheet.SkBitmap.Height.Should().Be(zipSheet.SkBitmap.Height);
        folderSheet.Regions.Count.Should().Be(zipSheet.Regions.Count);
        folderSheet.GetRegion("ballBlue")!.Area.Should().Be(zipSheet.GetRegion("ballBlue")!.Area);
        _materializer.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeTilesheet_rasterizes_a_vector_at_its_intrinsic_size()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/Vector/puzzleAssets_vector");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(entry, entry.GetKey(ProviderId));

        //Assert
        // The document declares 800 by 480.
        sheet.SkBitmap.Width.Should().Be(800);
        sheet.SkBitmap.Height.Should().Be(480);
        sheet.DefaultRegion.TileSize.Should().Be(new Size(800, 480));
        sheet.DefaultRegion.Columns.Should().Be(1);
        HasAnyVisiblePixel(sheet.SkBitmap).Should().BeTrue();
    }

    [Fact]
    public void MaterializeTilesheet_scales_a_vector_by_the_factor_given()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/Vector/puzzleAssets_vector");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { VectorScale = 0.25f });

        //Assert
        sheet.SkBitmap.Width.Should().Be(200);
        sheet.SkBitmap.Height.Should().Be(120);
        HasAnyVisiblePixel(sheet.SkBitmap).Should().BeTrue();
    }

    [Fact]
    public void MaterializeTilesheet_rasterizes_a_vector_at_the_size_asked_for()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.SimulatedBundleFileName), "kenney:simulated-bundle/Vector/vector_blackIcons");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions
            {
                VectorRasterSize = new Size(256, 128),
                //An explicit size wins over a scale
                VectorScale = 8f,
            });

        //Assert
        sheet.SkBitmap.Width.Should().Be(256);
        sheet.SkBitmap.Height.Should().Be(128);
        sheet.DefaultRegion.TileSize.Should().Be(new Size(256, 128));
        HasAnyVisiblePixel(sheet.SkBitmap).Should().BeTrue();
    }

    [Fact]
    public void MaterializeTilesheet_caps_a_vector_at_the_largest_size_it_rasterizes()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/Vector/puzzleAssets_vector");

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry,
            entry.GetKey(ProviderId),
            new TilesheetMaterializeOptions { VectorRasterSize = new Size(16_000, 9_600) });

        //Assert
        sheet.SkBitmap.Width.Should().Be(SvgRasterizer.MaxDimension);
        //16000 by 9600 is 5:3, and the cap keeps that to within a rounded pixel
        sheet.SkBitmap.Height.Should().BeGreaterThanOrEqualTo(2457);
        sheet.SkBitmap.Height.Should().BeLessThanOrEqualTo(2458);
    }

    [Fact]
    public void MaterializeTilesheet_hands_back_the_tilesheet_already_registered_under_the_key()
    {
        //Arrange
        // Registering over a key disposes the tilesheet registered there, and with it every frame the
        // caller is holding, so the second call must not load again.
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/PNG/Default/ballBlue");
        string key = entry.GetKey(ProviderId);
        Tilesheet first = _materializer.MaterializeTilesheet(entry, key);

        //Act
        Tilesheet second = _materializer.MaterializeTilesheet(
            entry, key, new TilesheetMaterializeOptions { TileSize = new Size(11, 11) });

        //Assert
        second.Should().BeSameAs(first);
        TilesheetRegistry.Instance.Count.Should().Be(1);
        //The options of the second call are ignored along with the rest of it
        second.GetRegion(TilesheetMaterializer.GridRegionName).Should().BeNull();
    }

    [Fact]
    public void MaterializeTilesheet_registers_under_the_key_RegisterAs_names()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/PNG/Default/ballBlue");
        string key = entry.GetKey(ProviderId);

        //Act
        Tilesheet sheet = _materializer.MaterializeTilesheet(
            entry, key, new TilesheetMaterializeOptions { RegisterAs = "test:ball-small" });

        //Assert
        sheet.Name.Should().Be("test:ball-small");
        TilesheetRegistry.Instance.TryGet("test:ball-small", out _).Should().BeTrue();
        TilesheetRegistry.Instance.TryGet(key, out _).Should().BeFalse();
        TilesheetRegistry.Instance.Count.Should().Be(1);
    }

    [Fact]
    public void MaterializeTilesheet_refuses_an_asset_that_is_not_a_picture()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/License.txt");
        string key = entry.GetKey(ProviderId);
        Action act = () => _materializer.MaterializeTilesheet(entry, key);

        //Act, Assert
        UnsupportedGameAssetException thrown =
            act.Should().Throw<UnsupportedGameAssetException>().Which;
        thrown.Kind.Should().Be(GameAssetKind.Document);
        thrown.Key.Should().Be(key);
        thrown.Message.Should().Contain(UnsupportedGameAssetException.DefaultMessage);
        TilesheetRegistry.Instance.Count.Should().Be(0);
    }

    [Fact]
    public void MaterializeTilesheet_rejects_a_missing_entry_or_a_blank_key()
    {
        //Arrange
        KenneyAssetEntry entry = EntryOf(
            IndexOf(TestFixtures.PuzzlePackFileName), "kenney:puzzle-pack/PNG/Default/ballBlue");
        Action nullEntry = () => _materializer.MaterializeTilesheet(null!, "test:key");
        Action blankKey = () => _materializer.MaterializeTilesheet(entry, "  ");

        //Act, Assert
        nullEntry.Should().Throw<ArgumentNullException>();
        blankKey.Should().Throw<ArgumentException>();
    }

    private static bool HasAnyVisiblePixel(SKBitmap bitmap)
    {
        //A rasterized vector is mostly transparent, so a handful of sample rows is not enough; walk it
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0) { return true; }
            }
        }

        return false;
    }

    private static byte[] SolidPng(int width, int height, SKColor color)
    {
        using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(color);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static KenneyAssetEntry EntryOf(KenneyPackIndex index, string key)
    {
        index.TryGetEntry(key, out KenneyAssetEntry? entry).Should().BeTrue();

        return entry!;
    }

    private KenneyAssetEntry SyntheticImageEntry(string purpose, int width, int height)
    {
        string zipPath = BuildBundle(purpose, new Dictionary<string, byte[]>
        {
            ["License.txt"] = Utf8(TestFixtures.LicenseText(purpose, "1.0")),
            ["PNG/sheet.png"] = SolidPng(width, height, SKColors.CornflowerBlue),
        });

        return EntryOf(IndexOf(zipPath), $"{KenneyNames.Slugify(purpose)}/PNG/sheet");
    }

    private KenneyAssetEntry SyntheticAtlasEntry(
        string purpose, int width, int height, string subTextures)
    {
        string zipPath = BuildBundle(purpose, new Dictionary<string, byte[]>
        {
            ["License.txt"] = Utf8(TestFixtures.LicenseText(purpose, "1.0")),
            ["Spritesheet/sheet.xml"] = Utf8(
                $"<TextureAtlas imagePath=\"sheet.png\">{subTextures}</TextureAtlas>"),
            ["Spritesheet/sheet.png"] = SolidPng(width, height, SKColors.SeaGreen),
        });

        return EntryOf(IndexOf(zipPath), $"{KenneyNames.Slugify(purpose)}/Spritesheet/sheet");
    }

    private string BuildBundle(string purpose, IReadOnlyDictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);
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

    private KenneyPackIndex IndexOf(string bundleFileNameOrPath)
    {
        KenneyPackIndex index = new();
        index.AddSource(Open(bundleFileNameOrPath));

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
}
