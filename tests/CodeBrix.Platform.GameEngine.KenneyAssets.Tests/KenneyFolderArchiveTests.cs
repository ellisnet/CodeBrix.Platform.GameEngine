using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates that an extracted bundle folder is read exactly as its zip file would be, which is what
/// lets a game switch between the two without changing a single asset key.
/// </summary>
public class KenneyFolderArchiveTests : IDisposable
{
    private readonly string _extractedPath;

    /// <summary>
    /// Extracts the puzzle pack bundle into a scratch folder for the tests in this class.
    /// </summary>
    public KenneyFolderArchiveTests()
    {
        _extractedPath = TestFixtures.ExtractBundle(TestFixtures.PuzzlePackFileName, "folder-archive");
    }

    /// <summary>
    /// Deletes the scratch folder.
    /// </summary>
    public void Dispose()
    {
        TestFixtures.DeleteScratchFolder(_extractedPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Entries_match_the_zip_archive_entry_for_entry()
    {
        //Arrange
        using KenneyZipArchive zip = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        using KenneyFolderArchive folder = new(_extractedPath);

        //Act
        string[] zipPaths = [.. zip.Entries.Select(e => e.Path)];
        string[] folderPaths = [.. folder.Entries.Select(e => e.Path)];

        //Assert
        folderPaths.Should().Equal(zipPaths);
    }

    [Fact]
    public void Entries_report_the_same_sizes_as_the_zip_archive()
    {
        //Arrange
        using KenneyZipArchive zip = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        using KenneyFolderArchive folder = new(_extractedPath);

        //Act
        long zipTotal = zip.Entries.Sum(e => e.SizeBytes);
        long folderTotal = folder.Entries.Sum(e => e.SizeBytes);

        //Assert
        folderTotal.Should().Be(zipTotal);
    }

    [Fact]
    public void ReadBytes_returns_the_same_bytes_as_the_zip_archive()
    {
        //Arrange
        using KenneyZipArchive zip = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        using KenneyFolderArchive folder = new(_extractedPath);
        const string path = "Spritesheet/spritesheet_default.xml";

        //Act
        byte[] fromZip = zip.ReadBytes(path);
        byte[] fromFolder = folder.ReadBytes(path);

        //Assert
        fromFolder.Should().Equal(fromZip);
    }

    [Fact]
    public void HasEntry_ignores_case_and_separator_style()
    {
        //Arrange
        using KenneyFolderArchive folder = new(_extractedPath);

        //Act, Assert
        folder.HasEntry("license.txt").Should().BeTrue();
        folder.HasEntry("PNG\\Double\\ballBlue.png").Should().BeTrue();
        folder.HasEntry("PNG/Double/nothing.png").Should().BeFalse();
    }

    [Fact]
    public void ResolveDependencyPath_behaves_like_the_zip_archive()
    {
        //Arrange
        using KenneyZipArchive zip = new(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        using KenneyFolderArchive folder = new(_extractedPath);
        const string atlasPath = "Spritesheet/spritesheet_double.xml";

        //Act
        string? fromZip = zip.ResolveDependencyPath(atlasPath, "spritesheet_double.png");
        string? fromFolder = folder.ResolveDependencyPath(atlasPath, "spritesheet_double.png");

        //Assert
        fromFolder.Should().Be(fromZip);
        fromFolder.Should().Be("Spritesheet/spritesheet_double.png");
    }

    [Fact]
    public void Open_returns_a_readable_stream()
    {
        //Arrange
        using KenneyFolderArchive folder = new(_extractedPath);
        KenneyArchiveEntry entry = folder.Entries.Single(e => e.Path == "License.txt");

        //Act
        using Stream stream = folder.Open(entry.Path);

        //Assert
        stream.Length.Should().Be(entry.SizeBytes);
    }

    [Fact]
    public void Constructor_throws_when_the_folder_does_not_exist()
    {
        //Arrange
        Action act = () => new KenneyFolderArchive(Path.Combine(_extractedPath, "no-such-folder"));

        //Act, Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void A_nested_zip_is_one_entry_and_is_never_expanded()
    {
        //Arrange
        string folderPath = TestFixtures.CreateScratchFolder("nested-zip");
        try
        {
            string nestedZipPath = Path.Combine(folderPath, "Webfonts A.zip");
            TestFixtures.BuildZip(nestedZipPath, new Dictionary<string, byte[]>
            {
                ["inside/hidden.ttf"] = TestFixtures.FakeImage(),
            });
            TestFixtures.BuildFolder(folderPath, new Dictionary<string, byte[]>
            {
                ["License.txt"] = Encoding.UTF8.GetBytes(
                    TestFixtures.LicenseText("Nested Zip Pack", "1.0")),
                ["Fonts/real.ttf"] = TestFixtures.FakeImage(),
            });

            //Act
            using KenneyFolderArchive folder = new(folderPath);

            //Assert
            string[] paths = [.. folder.Entries.Select(e => e.Path)];
            string[] expected = ["Fonts/real.ttf", "License.txt", "Webfonts A.zip"];
            paths.Should().Equal(expected);
            folder.HasEntry("Webfonts A.zip/inside/hidden.ttf").Should().BeFalse();
        }
        finally
        {
            TestFixtures.DeleteScratchFolder(folderPath);
        }
    }

    [Fact]
    public void ReadBytes_after_dispose_throws()
    {
        //Arrange
        KenneyFolderArchive folder = new(_extractedPath);
        folder.Dispose();
        Action act = () => folder.ReadBytes("License.txt");

        //Act, Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}
