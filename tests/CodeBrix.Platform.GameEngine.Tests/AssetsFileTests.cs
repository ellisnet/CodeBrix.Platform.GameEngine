using System;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="AssetsFile.Validate"/>, the strict bundle check used by authoring tools: a
/// bundle written by the engine passes, an entry key that cannot be parsed (or names an unknown
/// asset type, or an empty asset name) is rejected, two entries that resolve to the same key are
/// rejected, and a damaged payload is only reported when payload verification is requested.
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
}
