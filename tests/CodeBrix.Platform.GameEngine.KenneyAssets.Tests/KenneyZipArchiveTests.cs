using System;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates reading a real downloaded Kenney bundle: the entry listing, the reads, and the strict and
/// loose forms of dependency resolution.
/// </summary>
public class KenneyZipArchiveTests
{
    [Fact]
    public void Entries_lists_every_file_and_no_directories()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act
        int count = archive.Entries.Count;

        //Assert
        count.Should().Be(181);
        archive.Entries.Should().AllSatisfy(entry => entry.Path.Should().NotEndWith("/"));
        archive.Entries.Count(e => e.Extension == "png").Should().Be(176);
        archive.Entries.Count(e => e.Extension == "xml").Should().Be(2);
    }

    [Fact]
    public void Entries_are_ordered_by_path_ignoring_case()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        string[] paths = [.. archive.Entries.Select(e => e.Path)];

        //Assert
        paths.Should().Equal([.. paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)]);
    }

    [Fact]
    public void Entries_report_forward_slash_paths_and_derived_names()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        KenneyArchiveEntry entry = archive.Entries
            .Single(e => e.Path == "Tiled/tileset-characters.tsx");

        //Assert
        entry.FileName.Should().Be("tileset-characters.tsx");
        entry.Name.Should().Be("tileset-characters");
        entry.Extension.Should().Be("tsx");
        entry.Folder.Should().Be("Tiled");
        entry.SizeBytes.Should().BeGreaterThan(0L);
    }

    [Fact]
    public void Constructor_throws_when_the_bundle_does_not_exist()
    {
        //Arrange
        Action act = () => new KenneyZipArchive(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "no-such-bundle.zip"));

        //Act, Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void HasEntry_ignores_case_and_leading_slashes()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act, Assert
        archive.HasEntry("License.txt").Should().BeTrue();
        archive.HasEntry("license.TXT").Should().BeTrue();
        archive.HasEntry("/License.txt").Should().BeTrue();
        archive.HasEntry("PNG\\Default\\ballBlue.png").Should().BeTrue();
        archive.HasEntry("nothing.txt").Should().BeFalse();
        archive.HasEntry(null).Should().BeFalse();
    }

    [Fact]
    public void ReadText_returns_the_licence_file_content()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act
        string licence = archive.ReadText("License.txt");

        //Assert
        licence.Should().Contain("Puzzle Pack");
        licence.Should().Contain("kenney.nl");
    }

    [Fact]
    public void ReadBytes_returns_the_whole_entry()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));
        KenneyArchiveEntry entry = archive.Entries.Single(e => e.Path == "Tilemap/tilemap_packed.png");

        //Act
        byte[] bytes = archive.ReadBytes(entry.Path);

        //Assert
        bytes.Length.Should().Be((int)entry.SizeBytes);
    }

    [Fact]
    public void Open_returns_a_seekable_stream_over_the_entry()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));
        KenneyArchiveEntry entry = archive.Entries.Single(e => e.Path == "Fonts/Kenney Space.ttf");

        //Act
        using Stream stream = archive.Open(entry.Path);

        //Assert
        stream.CanSeek.Should().BeTrue();
        stream.Length.Should().Be(entry.SizeBytes);
        stream.Position.Should().Be(0L);
    }

    [Fact]
    public void ReadBytes_throws_for_an_entry_the_bundle_does_not_hold()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        Action act = () => archive.ReadBytes("PNG/Default/nothing.png");

        //Act, Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ResolveDependencyPath_climbs_out_of_the_referencing_folder()
    {
        //Arrange
        // The simulated bundle's tile sets live in Tiled/ and name their images through ../Tilemap/,
        // which only resolves because the search walks up to the archive root.
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Act
        string? resolved = archive.ResolveDependencyPath(
            "Tiled/tileset-tiles.tsx", "../Tilemap/tilemap_packed.png");

        //Assert
        resolved.Should().Be("Tilemap/tilemap_packed.png");
    }

    [Fact]
    public void ResolveDependencyPath_resolves_a_sibling_reference()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act
        string? resolved = archive.ResolveDependencyPath(
            "Spritesheet/spritesheet_default.xml", "spritesheet_default.png");

        //Assert
        resolved.Should().Be("Spritesheet/spritesheet_default.png");
    }

    [Fact]
    public void ResolveDependencyPath_strict_refuses_to_guess_by_bare_file_name()
    {
        //Arrange
        // The brick kit holds Textures/colormap.png three times, once per model format. Guessing by
        // bare file name would hand a GLB model the OBJ folder's texture.
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.BrickKitFileName));
        const string modelPath = "Models/GLB format/bevel-hq-brick-1x1.glb";

        //Act
        string? strict = archive.ResolveDependencyPath(modelPath, "colormap.png");
        string? loose = archive.ResolveDependencyPath(modelPath, "colormap.png", strict: false);

        //Assert
        strict.Should().BeNull();
        loose.Should().NotBeNull();
        loose.Should().EndWith("Textures/colormap.png");
    }

    [Fact]
    public void ResolveDependencyPath_returns_null_for_an_empty_reference()
    {
        //Arrange
        using KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act, Assert
        archive.ResolveDependencyPath("License.txt", null).Should().BeNull();
        archive.ResolveDependencyPath(null, "License.txt").Should().BeNull();
    }

    [Fact]
    public void ReadBytes_after_dispose_throws()
    {
        //Arrange
        KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        archive.Dispose();
        Action act = () => archive.ReadBytes("License.txt");

        //Act, Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        //Arrange
        KenneyZipArchive archive = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Act
        Action act = () =>
        {
            archive.Dispose();
            archive.Dispose();
        };

        //Assert
        act.Should().NotThrow();
    }
}
