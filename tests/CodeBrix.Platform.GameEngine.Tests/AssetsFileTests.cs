using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="AssetsFile.Validate"/>, the strict bundle check used by authoring tools: a
/// bundle written by the engine passes, an entry key that cannot be parsed (or names an unknown
/// asset type, or an empty asset name) is rejected, two entries that resolve to the same key are
/// rejected, and a damaged payload is only reported when payload verification is requested. Also
/// covers loading a bundle from a <see cref="Stream"/>: the caller keeps the stream, a failed load
/// registers nothing, and a stream-loaded bundle has no file path to save to.
/// </summary>
/// <remarks>
/// Run-time loading stays permissive — it logs and skips an entry it does not recognise — so these
/// tests deliberately assert the stricter behaviour of the validation entry point.
/// </remarks>
public class AssetsFileTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary directory the fixture writes its bundles into.</summary>
    public AssetsFileTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_assetsfile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        AssetsFile.ClearAll();
    }

    /// <summary>Clears the global assets registry and removes the temporary directory.</summary>
    public void Dispose()
    {
        AssetsFile.ClearAll();

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            /* best effort */
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] CreatePayload(int seed, int length)
    {
        //Pseudo-random bytes so no entry compresses down to a handful of bytes, which leaves
        //room for the payload corruption used below whichever entry the bundle writes first.
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);

        return bytes;
    }

    private string CreateEngineBundle(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using var bundle = AssetsFile.LoadOrCreate(path);
        bundle.Add(AssetTypes.Image, "tiles/terrain.png", new MemoryStream(CreatePayload(seed: 1234, length: 4096)));
        bundle.Add(AssetTypes.Audio, "music/theme.raw", new MemoryStream(CreatePayload(seed: 5678, length: 4096)));
        bundle.Save();

        return path;
    }

    private string CreateRawZip(string fileName, params string[] entryNames)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using var stream = File.Create(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);

        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName);

            using var entryStream = entry.Open();
            entryStream.Write(Encoding.UTF8.GetBytes("payload"));
        }

        return path;
    }

    private static void CorruptFirstEntryPayload(string path)
    {
        //Local file header layout: 30 fixed bytes, then the entry name, then the extra field,
        //then the (compressed) payload. Damaging a payload byte leaves every header intact.
        var bytes = File.ReadAllBytes(path);
        var nameLength = BitConverter.ToUInt16(bytes, 26);
        var extraLength = BitConverter.ToUInt16(bytes, 28);
        var dataStart = 30 + nameLength + extraLength;

        bytes[dataStart + 16] ^= 0xFF;

        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void Validate_accepts_a_bundle_written_by_the_engine()
    {
        //Arrange
        var path = CreateEngineBundle("valid.gaf");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_accepts_an_encrypted_bundle_when_the_password_is_supplied()
    {
        //Arrange
        var path = Path.Combine(_workDirectory, "secret.gaf");

        using (var bundle = AssetsFile.LoadOrCreate(path, "open sesame", encrypt: true))
        {
            bundle.Add(AssetTypes.Image, "tiles/terrain.png", new MemoryStream(CreatePayload(seed: 99, length: 2048)));
            bundle.Save();
        }

        //Act
        var act = () => AssetsFile.Validate(path, "open sesame");

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_rejects_an_entry_key_that_is_not_in_the_expected_format()
    {
        //Arrange - run-time loading logs and skips this entry; validation must reject it.
        var path = CreateRawZip("garbage-name.gaf", "not-a-valid-key.png");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid bundle entry key*");
    }

    [Fact]
    public void Validate_rejects_an_entry_key_naming_an_unknown_asset_type()
    {
        //Arrange - the asset-type parser accepts a bare number, so the defined-value check matters.
        var path = CreateRawZip("unknown-type.gaf", "99_terrain.png");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid bundle entry key*");
    }

    [Fact]
    public void Validate_rejects_an_entry_key_with_an_empty_asset_name()
    {
        //Arrange
        var path = CreateRawZip("empty-name.gaf", "Image_");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid bundle entry key*");
    }

    [Fact]
    public void Validate_rejects_two_entries_that_resolve_to_the_same_key()
    {
        //Arrange - asset names compare case-insensitively, so these two entries collide.
        var path = CreateRawZip("duplicate-key.gaf", "Image_logo.png", "Image_LOGO.PNG");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Duplicate bundle entry key*");
    }

    [Fact]
    public void Validate_reports_a_damaged_payload_when_payload_verification_is_requested()
    {
        //Arrange
        var path = CreateEngineBundle("damaged.gaf");
        CorruptFirstEntryPayload(path);

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*integrity check failed*");
    }

    [Fact]
    public void Validate_skips_payload_verification_when_testData_is_false()
    {
        //Arrange - the same damaged bundle: structure and keys are intact, only the payload is not.
        var path = CreateEngineBundle("damaged-structure-ok.gaf");
        CorruptFirstEntryPayload(path);

        //Act
        var act = () => AssetsFile.Validate(path, testData: false);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_checks_entry_keys_even_when_payload_verification_is_skipped()
    {
        //Arrange
        var path = CreateRawZip("garbage-name-no-data-test.gaf", "not-a-valid-key.png");

        //Act
        var act = () => AssetsFile.Validate(path, testData: false);

        //Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid bundle entry key*");
    }

    [Fact]
    public void Validate_does_not_register_the_inspected_bundle()
    {
        //Arrange
        var path = CreateEngineBundle("unregistered.gaf");
        AssetsFile.ClearAll();

        //Act
        AssetsFile.Validate(path);

        //Assert
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    [Fact]
    public void LoadOrCreate_does_not_register_a_bundle_whose_password_is_wrong()
    {
        //Arrange
        var path = Path.Combine(_workDirectory, "locked.gaf");

        using (var bundle = AssetsFile.LoadOrCreate(path, "open sesame", encrypt: true))
        {
            bundle.Add(AssetTypes.Image, "tiles/terrain.png", new MemoryStream(CreatePayload(seed: 7, length: 2048)));
            bundle.Save();
        }

        //Act
        var act = () => AssetsFile.LoadOrCreate(path, "wrong password");

        //Assert
        act.Should().Throw<Exception>();
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    [Fact]
    public void LoadOrCreate_does_not_register_a_bundle_that_is_not_an_archive()
    {
        //Arrange
        var path = Path.Combine(_workDirectory, "not-an-archive.gaf");
        File.WriteAllBytes(path, CreatePayload(seed: 11, length: 512));

        //Act
        var act = () => AssetsFile.LoadOrCreate(path);

        //Assert
        act.Should().Throw<Exception>();
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    [Fact]
    public void LoadOrCreate_registers_a_bundle_that_loads()
    {
        //Arrange
        var path = CreateEngineBundle("registered.gaf");
        AssetsFile.ClearAll();

        //Act
        var bundle = AssetsFile.LoadOrCreate(path);

        //Assert
        AssetsFile.AllAssetsFiles.Should().ContainSingle().Which.Should().BeSameAs(bundle);
    }

    [Fact]
    public void a_bundle_holding_definition_entries_loads_and_keeps_them_by_type()
    {
        //Arrange - definition formats the engine does not read yet, beside an ordinary image.
        var path = CreateRawZip(
            "definitions.gaf",
            "SceneDefinition_level1.gscn",
            "AnimationDefinition_hero.gani",
            "AudioDefinition_sounds.gsnd",
            "SpriteDefinition_actors.gspr",
            "Image_logo.png");

        //Act
        var bundle = AssetsFile.LoadOrCreate(path);

        //Assert
        bundle.GetAllEntries().Select(entry => entry.AssetType).Should().BeEquivalentTo(new[]
        {
            AssetTypes.SceneDefinition,
            AssetTypes.AnimationDefinition,
            AssetTypes.AudioDefinition,
            AssetTypes.SpriteDefinition,
            AssetTypes.Image
        });
        bundle.Get(AssetTypes.SceneDefinition, "level1.gscn").Should().NotBeNull();
        bundle.Get(AssetTypes.SpriteDefinition, "actors").Should().NotBeNull();
    }

    [Fact]
    public void Validate_accepts_definition_entries()
    {
        //Arrange
        var path = CreateRawZip("definitions-valid.gaf", "SceneDefinition_level1.gscn", "AudioDefinition_sounds.gsnd");

        //Act
        var act = () => AssetsFile.Validate(path);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void the_definition_asset_types_keep_their_numbers()
    {
        //Assert - the numbers are part of the bundle format and must not move.
        ((int)AssetTypes.TilesheetDefinition).Should().Be(7);
        ((int)AssetTypes.SceneDefinition).Should().Be(8);
        ((int)AssetTypes.AnimationDefinition).Should().Be(9);
        ((int)AssetTypes.AudioDefinition).Should().Be(10);
        ((int)AssetTypes.SpriteDefinition).Should().Be(11);
    }

    private byte[] CreateEngineBundleBytes(string fileName, string? password = null)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using (var bundle = AssetsFile.LoadOrCreate(path, password, encrypt: password is not null))
        {
            bundle.Add(AssetTypes.Image, "tiles/terrain.png", new MemoryStream(CreatePayload(seed: 1234, length: 4096)));
            bundle.Add(AssetTypes.Audio, "music/theme.raw", new MemoryStream(CreatePayload(seed: 5678, length: 4096)));
            bundle.Save();
        }

        AssetsFile.ClearAll();

        return File.ReadAllBytes(path);
    }

    private static byte[] ReadAll(Stream? stream)
    {
        stream.Should().NotBeNull();
        using var copy = new MemoryStream();
        stream!.CopyTo(copy);

        return copy.ToArray();
    }

    [Fact]
    public void Load_reads_a_bundle_from_a_stream()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("from-stream.gaf"));

        //Act
        using var bundle = AssetsFile.Load(source);

        //Assert
        bundle.GetAllEntries().Should().HaveCount(2);
        ReadAll(bundle.Get(AssetTypes.Image, "tiles/terrain.png")).Should().Equal(CreatePayload(seed: 1234, length: 4096));
        bundle.FilePath.Should().BeEmpty();
    }

    [Fact]
    public void Load_leaves_the_stream_open_and_needs_it_no_longer()
    {
        //Arrange
        var source = new MemoryStream(CreateEngineBundleBytes("caller-owned.gaf"));

        //Act
        using var bundle = AssetsFile.Load(source);
        var stillOpen = source.CanRead;
        source.Dispose();

        //Assert
        stillOpen.Should().BeTrue();
        ReadAll(bundle.Get(AssetTypes.Audio, "music/theme.raw")).Should().Equal(CreatePayload(seed: 5678, length: 4096));
    }

    [Fact]
    public void Load_reads_a_stream_that_cannot_seek()
    {
        //Arrange
        using var source = new ForwardOnlyStream(CreateEngineBundleBytes("forward-only.gaf"));

        //Act
        using var bundle = AssetsFile.Load(source);

        //Assert
        bundle.GetAllEntries().Should().HaveCount(2);
        source.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Load_reads_a_bundle_that_starts_partway_through_a_stream()
    {
        //Arrange - a 100 byte header in front of the bundle, the stream positioned past it.
        var bundleBytes = CreateEngineBundleBytes("embedded.gaf");
        var combined = new byte[100 + bundleBytes.Length];
        bundleBytes.CopyTo(combined, 100);
        using var source = new MemoryStream(combined) { Position = 100 };

        //Act
        using var bundle = AssetsFile.Load(source);

        //Assert
        bundle.GetAllEntries().Should().HaveCount(2);
    }

    [Fact]
    public void Load_reads_an_encrypted_bundle_with_its_password()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("encrypted-stream.gaf", "open sesame"));

        //Act
        using var bundle = AssetsFile.Load(source, "open sesame");

        //Assert
        ReadAll(bundle.Get(AssetTypes.Image, "tiles/terrain.png")).Should().Equal(CreatePayload(seed: 1234, length: 4096));
    }

    [Fact]
    public void Load_registers_the_bundle_by_default()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("registered-stream.gaf"));

        //Act
        var bundle = AssetsFile.Load(source);

        //Assert
        AssetsFile.AllAssetsFiles.Should().ContainSingle().Which.Should().BeSameAs(bundle);
    }

    [Fact]
    public void Load_without_registering_leaves_the_registry_alone()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("detached-stream.gaf"));

        //Act
        using var bundle = AssetsFile.Load(source, register: false);

        //Assert
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
        bundle.GetAllEntries().Should().HaveCount(2);
    }

    [Fact]
    public void Load_of_a_stream_that_is_not_an_archive_throws_and_registers_nothing()
    {
        //Arrange
        using var source = new MemoryStream(CreatePayload(seed: 11, length: 512));

        //Act
        var act = () => AssetsFile.Load(source);

        //Assert
        act.Should().Throw<Exception>();
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    [Fact]
    public void a_failed_Load_leaves_the_stream_open()
    {
        //Arrange
        using var source = new MemoryStream(CreatePayload(seed: 12, length: 512));

        //Act
        var act = () => AssetsFile.Load(source);

        //Assert
        act.Should().Throw<Exception>();
        source.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Load_with_a_wrong_password_throws_and_registers_nothing()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("locked-stream.gaf", "open sesame"));

        //Act
        var act = () => AssetsFile.Load(source, "wrong password");

        //Assert
        act.Should().Throw<Exception>();
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    [Fact]
    public void Load_rejects_a_null_stream()
    {
        //Act
        var act = () => AssetsFile.Load(null!);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Load_rejects_a_stream_that_cannot_be_read()
    {
        //Arrange
        var source = new MemoryStream();
        source.Dispose();

        //Act
        var act = () => AssetsFile.Load(source);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_stream_loaded_bundle_cannot_be_saved()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("no-path.gaf"));
        using var bundle = AssetsFile.Load(source);

        //Act
        var act = () => bundle.Save();

        //Assert
        act.Should().Throw<InvalidOperationException>();
        bundle.GetAllEntries().Should().HaveCount(2);
    }

    [Fact]
    public void disposing_a_stream_loaded_bundle_unregisters_it()
    {
        //Arrange
        using var source = new MemoryStream(CreateEngineBundleBytes("dispose-stream.gaf"));
        var bundle = AssetsFile.Load(source);

        //Act
        bundle.Dispose();

        //Assert
        AssetsFile.AllAssetsFiles.Should().BeEmpty();
    }

    /// <summary>A read-only stream that cannot seek, like a network or compressed stream.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] bytes) => _inner = new MemoryStream(bytes);

        public bool IsDisposed { get; private set; }

        public override bool CanRead => !IsDisposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
