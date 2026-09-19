using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates what a game gets from materializing the audio in a Kenney pack: a resource registered with
/// the engine's audio manager under the asset's key, at the volume and pan it asked for, from a zip
/// bundle or an extracted folder alike.
/// </summary>
/// <remarks>
/// NOTHING HERE IS PLAYED, but loading a resource still builds an output voice, and that makes the
/// process-wide shared output adopt a sample rate. The core engine's audio tests shut the audio system
/// down after each test so that rate does not outlive them, and these do the same.
/// </remarks>
public class AudioMaterializerTests : IDisposable
{
    private const string ProviderId = KenneyPackIndex.DefaultProviderId;
    private const string ComputerNoisePath = "Audio/computerNoise_000.ogg";
    private const string Lose6Path = "Audio/lose6.ogg";

    private readonly AudioMaterializer _materializer = new();
    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _scratchFolders = [];
    private readonly List<string> _keys = [];

    /// <summary>
    /// Unloads every resource these tests registered, un-claims the shared audio output, and closes the
    /// bundles and scratch folders they opened.
    /// </summary>
    public void Dispose()
    {
        foreach (string key in _keys) { AudioResourceManager.Instance.Unload(key); }

        AudioSystem.Shutdown();

        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Materialize_registers_the_resource_under_the_key()
    {
        //Arrange
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);
        string key = Key(entry);

        //Act
        AudioResource resource = _materializer.Materialize(entry, key);

        //Assert
        key.Should().Be("kenney:sci-fi-sounds/Audio/computerNoise_000");
        resource.Key.Should().Be(key);
        AudioResourceManager.Instance.Contains(key).Should().BeTrue();
        AudioResourceManager.Instance.Get(key).Should().BeSameAs(resource);
        resource.SourceExtension.Should().Be(".ogg");
    }

    [Fact]
    public void Materialize_decodes_the_asset_to_a_plausible_sound()
    {
        //Arrange
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);

        //Act
        AudioResource resource = _materializer.Materialize(entry, Key(entry));

        //Assert - a sound effect of a few seconds, and short enough that the engine decoded it once to
        //  PCM up front rather than leaving it to stream on the audio thread
        (resource.Duration.TotalSeconds is > 0.1 and < 60.0).Should().BeTrue(
            $"a sound effect should last a fraction of a minute but this one reports {resource.Duration}");
        resource.IsPreloaded.Should().BeTrue();
    }

    [Fact]
    public void Materialize_applies_the_volume_and_pan()
    {
        //Arrange
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);

        //Act
        AudioResource resource = _materializer.Materialize(entry, Key(entry), volume: 0.4f, pan: -0.75f);

        //Assert
        resource.Volume.Should().Be(0.4f);
        resource.Pan.Should().Be(-0.75f);
    }

    [Fact]
    public void Materialize_returns_the_resource_already_registered_under_the_key()
    {
        //Arrange
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);
        string key = Key(entry);
        AudioResource first = _materializer.Materialize(entry, key, volume: 0.25f);

        //Act
        AudioResource second = _materializer.Materialize(entry, key, volume: 1.0f, pan: 1.0f);

        //Assert - the first materialization of a key wins, volume and pan included; re-registering would
        //  dispose a resource a game may already be playing
        second.Should().BeSameAs(first);
        second.Volume.Should().Be(0.25f);
        second.Pan.Should().Be(0f);
    }

    [Fact]
    public void Materialize_returns_the_registered_resource_to_another_materializer_too()
    {
        //Arrange - the audio manager is the cache, so nothing depends on which materializer instance a
        //  provider happens to hold
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);
        string key = Key(entry);
        AudioResource first = _materializer.Materialize(entry, key);

        //Act
        AudioResource second = new AudioMaterializer().Materialize(entry, key);

        //Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Materialize_loads_an_asset_from_an_extracted_folder_like_its_zip()
    {
        //Arrange - the same sound reached both ways; two keys, because one key can only hold one resource
        KenneyAssetEntry fromZip = AudioEntry(TestFixtures.SimulatedBundleFileName, Lose6Path);
        KenneyAssetEntry fromFolder = FolderAudioEntry(TestFixtures.SimulatedBundleFileName, Lose6Path);

        //Act
        AudioResource zipResource = _materializer.Materialize(fromZip, Key(fromZip, "zip"));
        AudioResource folderResource = _materializer.Materialize(fromFolder, Key(fromFolder, "folder"));

        //Assert
        folderResource.Should().NotBeSameAs(zipResource);
        folderResource.SourceExtension.Should().Be(zipResource.SourceExtension);
        folderResource.Duration.Should().Be(zipResource.Duration);
    }

    [Fact]
    public void Materialize_reports_the_asset_when_the_bytes_cannot_be_decoded()
    {
        //Arrange
        string bundlePath = BuildBundle("broken-audio", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Broken Audio", "1.0")),
            ["Audio/broken.ogg"] = Encoding.UTF8.GetBytes("this is not an Ogg Vorbis stream"),
        });
        KenneyAssetEntry entry = Entry(OpenSource(bundlePath), "Audio/broken.ogg");
        string key = Key(entry);

        //Act
        Action act = () => _materializer.Materialize(entry, key);

        //Assert - the message names the asset, not just the decoder's complaint, and a failed load leaves
        //  the key free
        InvalidDataException thrown = Assert.Throws<InvalidDataException>(act);
        thrown.Message.Should().Contain("Audio/broken.ogg");
        thrown.Message.Should().Contain(key);
        thrown.InnerException.Should().NotBeNull();
        AudioResourceManager.Instance.Contains(key).Should().BeFalse();
    }

    [Fact]
    public void Materialize_throws_for_an_asset_that_is_not_audio()
    {
        //Arrange - a stray desktop.ini sits among the sounds of the real pack, and an image is an asset
        //  the library materializes elsewhere; neither is this materializer's business
        KenneyAssetSource sounds = OpenBundle(TestFixtures.SciFiSoundsFileName);
        KenneyAssetEntry strayFile = Entry(sounds, "Audio/desktop.ini");
        KenneyAssetEntry image = Entry(OpenBundle(TestFixtures.SimulatedBundleFileName), "PNG/tile_0079.png");
        string strayKey = Key(strayFile);

        //Act
        UnsupportedGameAssetException notMaterializable =
            Assert.Throws<UnsupportedGameAssetException>(() => _materializer.Materialize(strayFile, strayKey));
        UnsupportedGameAssetException anotherKind =
            Assert.Throws<UnsupportedGameAssetException>(() => _materializer.Materialize(image, Key(image)));

        //Assert
        strayFile.Kind.Should().Be(GameAssetKind.Other);
        notMaterializable.Kind.Should().Be(GameAssetKind.Other);
        notMaterializable.Key.Should().Be(strayKey);
        notMaterializable.Message.Should().StartWith(UnsupportedGameAssetException.DefaultMessage);
        anotherKind.Kind.Should().Be(GameAssetKind.Image);
        AudioResourceManager.Instance.Contains(strayKey).Should().BeFalse();
    }

    [Fact]
    public void Materialize_rejects_a_missing_entry_and_a_blank_key()
    {
        //Arrange
        KenneyAssetEntry entry = AudioEntry(TestFixtures.SciFiSoundsFileName, ComputerNoisePath);

        //Act
        Action noEntry = () => _materializer.Materialize(null!, "kenney:pack/Audio/sound");
        Action blankKey = () => _materializer.Materialize(entry, "  ");

        //Assert
        noEntry.Should().Throw<ArgumentNullException>();
        blankKey.Should().Throw<ArgumentException>();
    }

    //Every key a test asks for is also a key the fixture unloads afterwards
    private string Key(KenneyAssetEntry entry) => Track(entry.GetKey(ProviderId));

    //A second key for the same asset, standing in for a caller's own registration key
    private string Key(KenneyAssetEntry entry, string suffix) => Track($"{entry.GetKey(ProviderId)}-{suffix}");

    private static KenneyAssetEntry Entry(KenneyAssetSource source, string path) =>
        source.Packs[0].Entries.Single(e => e.Path == path);

    private string Track(string key)
    {
        _keys.Add(key);
        return key;
    }

    private KenneyAssetEntry AudioEntry(string bundleFileName, string path) =>
        Entry(OpenBundle(bundleFileName), path);

    private KenneyAssetEntry FolderAudioEntry(string bundleFileName, string path)
    {
        string folder = TestFixtures.ExtractBundle(bundleFileName, "audio-folder");
        _scratchFolders.Add(folder);
        return Entry(OpenSource(folder), path);
    }

    private KenneyAssetSource OpenBundle(string bundleFileName) =>
        OpenSource(TestFixtures.BundlePath(bundleFileName));

    private KenneyAssetSource OpenSource(string path)
    {
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
