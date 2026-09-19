using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates reading a TextureAtlas document and finding the sheet image it belongs to, including the
/// bundles that name an image they do not ship.
/// </summary>
public class SpriteAtlasParserTests : IDisposable
{
    private readonly List<string> _scratchFolders = [];

    /// <summary>
    /// Deletes every scratch folder the tests in this class created.
    /// </summary>
    public void Dispose()
    {
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryParse_reads_a_real_kenney_atlas()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        const string documentPath = "Spritesheet/spritesheet_default.xml";
        string xml = archive.ReadText(documentPath);

        //Act
        bool parsed = SpriteAtlasParser.TryParse(xml, documentPath, out SpriteAtlasDocument? atlas);

        //Assert
        parsed.Should().BeTrue();
        atlas!.Name.Should().Be("spritesheet_default");
        atlas.DocumentPath.Should().Be(documentPath);
        atlas.DeclaredImagePath.Should().Be("spritesheet_default.png");
        atlas.ImagePath.Should().Be("Spritesheet/spritesheet_default.png");
        atlas.Frames.Count.Should().Be(86);
    }

    [Fact]
    public void TryParse_keeps_frame_names_exactly_as_the_document_writes_them()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        const string documentPath = "Spritesheet/spritesheet_default.xml";

        //Act
        SpriteAtlasParser.TryParse(
            archive.ReadText(documentPath), documentPath, out SpriteAtlasDocument? atlas);

        //Assert
        SpriteAtlasFrame frame = atlas!.Frames.Single(f => f.Name == "ballBlue.png");
        frame.Width.Should().Be(22);
        frame.Height.Should().Be(22);
        frame.X.Should().Be(27);
        frame.Y.Should().Be(338);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<Something><SubTexture name=\"a\" x=\"0\" y=\"0\" width=\"1\" height=\"1\"/></Something>")]
    [InlineData("<TextureAtlas imagePath=\"a.png\"></TextureAtlas>")]
    [InlineData("<TextureAtlas imagePath=\"a.png\"><SubTexture name=\"a\"/></TextureAtlas>")]
    [InlineData("<TextureAtlas imagePath=\"a.png\"><SubTexture name=\"a\" x=\"0\" y=\"0\" " +
        "width=\"0\" height=\"0\"/></TextureAtlas>")]
    public void TryParse_declines_anything_that_is_not_a_usable_atlas(string? xml)
    {
        //Act
        bool parsed = SpriteAtlasParser.TryParse(xml, "Spritesheet/sheet.xml", out SpriteAtlasDocument? atlas);

        //Assert
        parsed.Should().BeFalse();
        atlas.Should().BeNull();
    }

    [Fact]
    public void TryParse_accepts_an_image_path_that_names_a_folder()
    {
        //Act
        SpriteAtlasParser.TryParse(
            "<TextureAtlas imagePath=\"../Tilemap/sheet.png\">" +
            "<SubTexture name=\"a\" x=\"0\" y=\"0\" width=\"4\" height=\"4\"/></TextureAtlas>",
            "Spritesheet/sheet.xml",
            out SpriteAtlasDocument? atlas);

        //Assert
        atlas!.ImagePath.Should().Be("Tilemap/sheet.png");
    }

    [Fact]
    public void TryResolveImagePath_takes_the_declared_image_when_it_exists()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        const string documentPath = "Spritesheet/spritesheet_double.xml";
        SpriteAtlasParser.TryParse(
            archive.ReadText(documentPath), documentPath, out SpriteAtlasDocument? atlas);

        //Act
        bool resolved = SpriteAtlasParser.TryResolveImagePath(atlas!, archive, out string? imagePath);

        //Assert
        resolved.Should().BeTrue();
        imagePath.Should().Be("Spritesheet/spritesheet_double.png");
    }

    [Fact]
    public void TryResolveImagePath_falls_back_to_the_sibling_sheet_of_a_stale_image_path()
    {
        //Arrange
        // Real bundles do this: Topdown Shooter and Robot Pack both declare imagePath="sprites.png"
        // while the sheet beside the document has the document's own name.
        string zipPath = BuildBundle("stale-atlas", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Stale Atlas", "1.0")),
            ["Spritesheet/spritesheet_characters.xml"] = Encoding.UTF8.GetBytes(
                "<TextureAtlas imagePath=\"sprites.png\">" +
                "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/></TextureAtlas>"),
            ["Spritesheet/spritesheet_characters.png"] = TestFixtures.FakeImage(),
        });
        using KenneyZipArchive archive = new(zipPath);
        const string documentPath = "Spritesheet/spritesheet_characters.xml";
        SpriteAtlasParser.TryParse(
            archive.ReadText(documentPath), documentPath, out SpriteAtlasDocument? atlas);

        //Act
        bool resolved = SpriteAtlasParser.TryResolveImagePath(atlas!, archive, out string? imagePath);

        //Assert
        resolved.Should().BeTrue();
        imagePath.Should().Be("Spritesheet/spritesheet_characters.png");
    }

    [Fact]
    public void TryResolveImagePath_finds_a_sheet_that_moved_to_another_folder()
    {
        //Arrange
        string zipPath = BuildBundle("moved-sheet", new Dictionary<string, byte[]>
        {
            ["Spritesheet/sheet.xml"] = Encoding.UTF8.GetBytes(
                "<TextureAtlas imagePath=\"sheet_image.png\">" +
                "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/></TextureAtlas>"),
            ["Tilemap/sheet_image.png"] = TestFixtures.FakeImage(),
        });
        using KenneyZipArchive archive = new(zipPath);
        SpriteAtlasParser.TryParse(
            archive.ReadText("Spritesheet/sheet.xml"), "Spritesheet/sheet.xml",
            out SpriteAtlasDocument? atlas);

        //Act
        bool resolved = SpriteAtlasParser.TryResolveImagePath(atlas!, archive, out string? imagePath);

        //Assert
        resolved.Should().BeTrue();
        imagePath.Should().Be("Tilemap/sheet_image.png");
    }

    [Fact]
    public void TryResolveImagePath_reports_an_atlas_whose_sheet_is_not_in_the_bundle()
    {
        //Arrange
        string zipPath = BuildBundle("no-sheet", new Dictionary<string, byte[]>
        {
            ["Spritesheet/sheet.xml"] = Encoding.UTF8.GetBytes(
                "<TextureAtlas imagePath=\"gone.png\">" +
                "<SubTexture name=\"hero.png\" x=\"0\" y=\"0\" width=\"8\" height=\"8\"/></TextureAtlas>"),
        });
        using KenneyZipArchive archive = new(zipPath);
        SpriteAtlasParser.TryParse(
            archive.ReadText("Spritesheet/sheet.xml"), "Spritesheet/sheet.xml",
            out SpriteAtlasDocument? atlas);

        //Act
        bool resolved = SpriteAtlasParser.TryResolveImagePath(atlas!, archive, out string? imagePath);

        //Assert
        resolved.Should().BeFalse();
        imagePath.Should().BeNull();
    }

    [Fact]
    public void TryResolveImagePath_rejects_null_arguments()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        Action nullAtlas = () => SpriteAtlasParser.TryResolveImagePath(null!, archive, out _);

        //Act, Assert
        nullAtlas.Should().Throw<ArgumentNullException>();
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
