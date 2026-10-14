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
/// Validates what a game gets from pointing the provider at a zip bundle, at a folder extracted from
/// one, and at a folder that holds a collection of packs.
/// </summary>
public class KenneyAssetSourceTests : IDisposable
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
    public void Open_a_zip_bundle_yields_one_pack_named_by_its_licence()
    {
        //Arrange, Act
        using KenneyAssetSource source =
            KenneyAssetSource.Open(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));

        //Assert
        source.IsFolder.Should().BeFalse();
        source.Packs.Should().ContainSingle();
        KenneyPack pack = source.Packs[0];
        pack.DisplayName.Should().Be("Puzzle Pack");
        pack.Version.Should().Be("1.1");
        pack.Slug.Should().Be("puzzle-pack");
        pack.LicenseTitle.Should().Be("Puzzle Pack (1.1)");
        pack.LicensePath.Should().Be("License.txt");
        pack.Entries.Should().NotBeEmpty();
    }

    [Fact]
    public void Open_a_zip_bundle_reports_no_warnings_for_a_clean_pack()
    {
        //Arrange, Act
        using KenneyAssetSource source =
            KenneyAssetSource.Open(TestFixtures.BundlePath(TestFixtures.SciFiSoundsFileName));

        //Assert
        source.Warnings.Should().BeEmpty();
        source.Packs[0].Slug.Should().Be("sci-fi-sounds");
    }

    [Fact]
    public void Open_an_extracted_folder_yields_the_same_pack_as_its_zip()
    {
        //Arrange
        string folder = Extract(TestFixtures.PuzzlePackFileName, "source-folder");

        //Act
        using KenneyAssetSource fromZip =
            KenneyAssetSource.Open(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        using KenneyAssetSource fromFolder = KenneyAssetSource.Open(folder);

        //Assert
        fromFolder.IsFolder.Should().BeTrue();
        fromFolder.Packs.Should().ContainSingle();
        fromFolder.Packs[0].Slug.Should().Be(fromZip.Packs[0].Slug);
        fromFolder.Packs[0].Entries.Count.Should().Be(fromZip.Packs[0].Entries.Count);
    }

    [Fact]
    public void Open_a_collection_folder_yields_one_pack_per_child_folder()
    {
        //Arrange
        string root = CreateScratch("collection");
        WritePack(Path.Combine(root, "Puzzle Pack"), "Puzzle Pack", "1.1");
        WritePack(Path.Combine(root, "Sci-Fi Sounds"), "Sci-Fi Sounds", "1.0");

        //Act
        using KenneyAssetSource source = KenneyAssetSource.Open(root);

        //Assert
        string[] slugs = [.. source.Packs.Select(p => p.Slug)];
        string[] expected = ["puzzle-pack", "sci-fi-sounds"];
        slugs.Should().Equal(expected);
    }

    [Fact]
    public void Open_a_collection_folder_finds_packs_below_the_first_level()
    {
        //Arrange
        // This is the shape of Kenney's own all-in-one download: the pack folders sit under category
        // folders, and neither the root nor a category folder carries a licence file.
        string root = CreateScratch("all-in-one");
        WritePack(Path.Combine(root, "2D assets", "Pixel Platformer"), "Pixel Platformer", "1.2");
        WritePack(Path.Combine(root, "Audio", "Sci-Fi Sounds"), "Sci-Fi Sounds", "1.0");

        //Act
        using KenneyAssetSource source = KenneyAssetSource.Open(root);

        //Assert
        string[] slugs = [.. source.Packs.Select(p => p.Slug)];
        string[] expected = ["pixel-platformer", "sci-fi-sounds"];
        slugs.Should().Equal(expected);
    }

    [Fact]
    public void Open_a_folder_without_a_licence_yields_one_pack_named_after_the_folder()
    {
        //Arrange
        string root = CreateScratch("no-licence");
        string packFolder = Path.Combine(root, "kenney_space-shooter");
        Directory.CreateDirectory(packFolder);
        TestFixtures.BuildFolder(packFolder, new Dictionary<string, byte[]>
        {
            ["PNG/hero.png"] = TestFixtures.FakeImage(),
        });

        //Act
        using KenneyAssetSource source = KenneyAssetSource.Open(packFolder);

        //Assert
        source.Packs.Should().ContainSingle();
        source.Packs[0].LicenseTitle.Should().BeNull();
        source.Packs[0].Version.Should().BeNull();
        source.Packs[0].DisplayName.Should().Be("Space Shooter");
        source.Packs[0].Slug.Should().Be("space-shooter");
    }

    [Fact]
    public void Open_a_pack_folder_that_carries_its_own_licence_is_not_a_collection()
    {
        //Arrange
        string root = CreateScratch("single-pack");
        WritePack(root, "Tiny Dungeon", "1.0");
        WritePack(Path.Combine(root, "Extras"), "Extras Pack", "1.0");

        //Act
        using KenneyAssetSource source = KenneyAssetSource.Open(root);

        //Assert
        source.Packs.Should().ContainSingle();
        source.Packs[0].Slug.Should().Be("tiny-dungeon");
    }

    [Fact]
    public void Open_throws_when_nothing_exists_at_the_path()
    {
        //Arrange
        Action act = () => KenneyAssetSource.Open(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "nothing-here.zip"));

        //Act, Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void TryOpen_turns_an_unreadable_bundle_into_a_warning()
    {
        //Arrange
        string root = CreateScratch("broken");
        string zipPath = Path.Combine(root, "kenney_broken.zip");
        File.WriteAllText(zipPath, "this is not a zip file");

        //Act
        bool opened = KenneyAssetSource.TryOpen(zipPath, out KenneyAssetSource? source, out string? warning);

        //Assert
        opened.Should().BeFalse();
        source.Should().BeNull();
        warning.Should().NotBeNull();
        warning.Should().Contain("kenney_broken.zip");
    }

    [Fact]
    public void TryOpen_reports_an_empty_path()
    {
        //Act
        bool opened = KenneyAssetSource.TryOpen(" ", out KenneyAssetSource? source, out string? warning);

        //Assert
        opened.Should().BeFalse();
        source.Should().BeNull();
        warning.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_closes_the_archives_of_every_pack()
    {
        //Arrange
        KenneyAssetSource source =
            KenneyAssetSource.Open(TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName));
        KenneyPack pack = source.Packs[0];

        //Act
        source.Dispose();
        Action act = () => pack.Archive.ReadBytes("License.txt");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(TestFixtures.SimulatedBundleFileName)]
    [InlineData(TestFixtures.PuzzlePackFileName)]
    [InlineData(TestFixtures.BrickKitFileName)]
    public void OpenZip_from_a_seekable_stream_yields_the_same_pack_as_the_path(string bundleFileName)
    {
        //Arrange
        string path = TestFixtures.BundlePath(bundleFileName);
        using KenneyAssetSource fromPath = KenneyAssetSource.Open(path);
        using FileStream file = File.OpenRead(path);

        //Act
        using KenneyAssetSource fromStream = KenneyAssetSource.OpenZip(file, "Assets/Packs/" + bundleFileName);

        //Assert
        fromStream.SourcePath.Should().Be("Assets/Packs/" + bundleFileName);
        fromStream.IsFolder.Should().BeFalse();
        fromStream.IsStream.Should().BeTrue();
        fromPath.IsStream.Should().BeFalse();
        ShouldMatch(fromStream, fromPath);
    }

    [Theory]
    [InlineData(TestFixtures.SimulatedBundleFileName)]
    [InlineData(TestFixtures.PuzzlePackFileName)]
    [InlineData(TestFixtures.BrickKitFileName)]
    public void OpenZip_from_a_stream_that_cannot_seek_yields_the_same_pack_as_the_path(string bundleFileName)
    {
        //Arrange
        using KenneyAssetSource fromPath = KenneyAssetSource.Open(TestFixtures.BundlePath(bundleFileName));
        TestNonSeekableStream stream = TestNonSeekableStream.OverBundle(bundleFileName);

        //Act
        using KenneyAssetSource fromStream = KenneyAssetSource.OpenZip(stream, bundleFileName);

        //Assert
        ShouldMatch(fromStream, fromPath);
        fromStream.Packs[0].Archive.ReadBytes("License.txt")
            .Should().Equal(fromPath.Packs[0].Archive.ReadBytes("License.txt"));
    }

    [Fact]
    public void OpenZip_names_a_pack_without_a_licence_after_the_last_segment_of_the_source_name()
    {
        //Arrange
        string root = CreateScratch("stream-unlicensed");
        string zipPath = Path.Combine(root, "kenney_tiny-things.zip");
        TestFixtures.BuildZip(zipPath, new Dictionary<string, byte[]> { ["PNG/tile.png"] = TestFixtures.FakeImage() });
        using KenneyAssetSource fromPath = KenneyAssetSource.Open(zipPath);
        using FileStream file = File.OpenRead(zipPath);

        //Act
        using KenneyAssetSource fromStream = KenneyAssetSource.OpenZip(file, @"packs\nested/kenney_tiny-things.zip");

        //Assert
        fromStream.Packs[0].Slug.Should().Be(fromPath.Packs[0].Slug);
        fromStream.Packs[0].DisplayName.Should().Be(fromPath.Packs[0].DisplayName);
    }

    [Fact]
    public void OpenZip_disposes_a_seekable_stream_with_the_source_unless_asked_to_leave_it_open()
    {
        //Arrange
        string path = TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName);
        using FileStream kept = File.OpenRead(path);
        FileStream handedOver = File.OpenRead(path);

        //Act
        KenneyAssetSource keeping = KenneyAssetSource.OpenZip(kept, "kept", leaveOpen: true);
        KenneyAssetSource owning = KenneyAssetSource.OpenZip(handedOver, "handed-over", leaveOpen: false);
        bool handedOverOpenWhileInUse = handedOver.CanRead;
        keeping.Dispose();
        owning.Dispose();

        //Assert
        handedOverOpenWhileInUse.Should().BeTrue();
        kept.CanRead.Should().BeTrue();
        handedOver.CanRead.Should().BeFalse();
    }

    [Fact]
    public void OpenZip_leaves_a_buffered_stream_open_only_when_asked_to()
    {
        //Arrange
        using TestNonSeekableStream kept = TestNonSeekableStream.OverBundle(TestFixtures.PuzzlePackFileName);
        TestNonSeekableStream handedOver = TestNonSeekableStream.OverBundle(TestFixtures.PuzzlePackFileName);

        //Act
        KenneyAssetSource keeping = KenneyAssetSource.OpenZip(kept, "kept", leaveOpen: true);
        KenneyAssetSource owning = KenneyAssetSource.OpenZip(handedOver, "handed-over", leaveOpen: false);
        keeping.Dispose();
        owning.Dispose();

        //Assert
        kept.IsDisposed.Should().BeFalse();
        handedOver.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void OpenZip_throws_for_a_stream_that_is_not_a_zip()
    {
        //Arrange
        MemoryStream garbage = new(Encoding.UTF8.GetBytes("this is not a zip file"));

        //Act
        Action act = () => KenneyAssetSource.OpenZip(garbage, "kenney_broken.zip");

        //Assert
        act.Should().Throw<IOException>().WithMessage("*kenney_broken.zip*");
        garbage.CanRead.Should().BeFalse();
    }

    [Fact]
    public void TryOpenZip_turns_a_stream_that_is_not_a_zip_into_an_error()
    {
        //Arrange
        using TestNonSeekableStream garbage = new(Encoding.UTF8.GetBytes("this is not a zip file"));

        //Act
        bool opened = KenneyAssetSource.TryOpenZip(
            garbage, "kenney_broken.zip", leaveOpen: true, out KenneyAssetSource? source, out string? error);

        //Assert
        opened.Should().BeFalse();
        source.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().Contain("kenney_broken.zip");
        garbage.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void TryOpenZip_opens_a_readable_bundle()
    {
        //Arrange
        using FileStream file = File.OpenRead(TestFixtures.BundlePath(TestFixtures.SciFiSoundsFileName));

        //Act
        bool opened = KenneyAssetSource.TryOpenZip(
            file, "sounds", leaveOpen: true, out KenneyAssetSource? source, out string? error);
        using KenneyAssetSource? openedSource = source;

        //Assert
        opened.Should().BeTrue();
        error.Should().BeNull();
        source!.Packs.Should().ContainSingle().Which.Slug.Should().Be("sci-fi-sounds");
    }

    [Fact]
    public void OpenZip_and_TryOpenZip_validate_their_arguments()
    {
        //Arrange
        using MemoryStream stream = new([1, 2, 3]);
        MemoryStream closed = new([1, 2, 3]);
        closed.Dispose();

        //Act
        Action openNull = () => KenneyAssetSource.OpenZip(null!, "name");
        Action openBlank = () => KenneyAssetSource.OpenZip(stream, "  ");
        Action openEmptyName = () => KenneyAssetSource.OpenZip(stream, string.Empty);
        Action openClosed = () => KenneyAssetSource.OpenZip(closed, "closed.zip");
        Action tryNull = () => KenneyAssetSource.TryOpenZip(null!, "name", false, out _, out _);
        Action tryBlank = () => KenneyAssetSource.TryOpenZip(stream, " ", false, out _, out _);
        Action tryClosed = () => KenneyAssetSource.TryOpenZip(closed, "closed.zip", false, out _, out _);

        //Assert
        openNull.Should().Throw<ArgumentNullException>();
        openBlank.Should().Throw<ArgumentException>();
        openEmptyName.Should().Throw<ArgumentException>();
        openClosed.Should().Throw<ArgumentException>();
        tryNull.Should().Throw<ArgumentNullException>();
        tryBlank.Should().Throw<ArgumentException>();
        tryClosed.Should().Throw<ArgumentException>();
        stream.CanRead.Should().BeTrue();
    }

    //A source read from a stream catalogs exactly what the same bundle read by path does
    private static void ShouldMatch(KenneyAssetSource fromStream, KenneyAssetSource fromPath)
    {
        fromStream.Packs.Should().ContainSingle();
        KenneyPack streamPack = fromStream.Packs[0];
        KenneyPack pathPack = fromPath.Packs[0];

        streamPack.Slug.Should().Be(pathPack.Slug);
        streamPack.DisplayName.Should().Be(pathPack.DisplayName);
        streamPack.Version.Should().Be(pathPack.Version);
        streamPack.LicenseTitle.Should().Be(pathPack.LicenseTitle);
        streamPack.Archive.Entries.Select(e => (e.Path, e.SizeBytes))
            .Should().Equal(pathPack.Archive.Entries.Select(e => (e.Path, e.SizeBytes)));
        streamPack.Entries.Select(e => (e.GetKey("kenney"), e.Kind))
            .Should().Equal(pathPack.Entries.Select(e => (e.GetKey("kenney"), e.Kind)));
        fromStream.Warnings.Select(w => w.Replace(fromStream.SourcePath, "<source>"))
            .Should().Equal(fromPath.Warnings.Select(w => w.Replace(fromPath.SourcePath, "<source>")));
    }

    private static void WritePack(string folderPath, string title, string version)
    {
        Directory.CreateDirectory(folderPath);
        TestFixtures.BuildFolder(folderPath, new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText(title, version)),
            ["PNG/tile.png"] = TestFixtures.FakeImage(),
        });
    }

    private string CreateScratch(string purpose)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);
        return folder;
    }

    private string Extract(string bundleFileName, string purpose)
    {
        string folder = TestFixtures.ExtractBundle(bundleFileName, purpose);
        _scratchFolders.Add(folder);
        return folder;
    }
}
