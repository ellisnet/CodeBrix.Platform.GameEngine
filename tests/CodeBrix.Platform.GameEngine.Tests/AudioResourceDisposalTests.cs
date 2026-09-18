using System;
using System.IO;
using System.Text.Json;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers disposal of an <see cref="AudioResource"/> that was DESERIALIZED rather than loaded — the
/// rehydration spec a save file carries, which owns no output device or wave stream — and the
/// cleanup <see cref="AudioResourceManager"/> owes when a load fails part way through.
/// </summary>
/// <remarks>
/// These pin a fix for a process-killing defect: the deserialization factory
/// (EngineSaveContractResolver) builds these through the parameterless constructor, leaving the
/// device and stream fields null, and disposal ran <c>outputDevice.Dispose()</c> against them
/// unguarded. From the finalizer thread that NullReferenceException is unhandled, so it killed the
/// test host AFTER the run reported success — a crash that no assertion could catch.
/// </remarks>
public class AudioResourceDisposalTests : IDisposable
{
    private const string FailingResourceExtension = ".gefailresource";
    private const string FailingReaderExtension = ".gefailreader";

    public AudioResourceDisposalTests()
    {
        // Two readers that fail on purpose, at the two points a load can fail after the temporary
        // file has been written: building the reader, and building the resource around it. Both
        // declare requiresFile so the load takes the temporary-file path.
        PlatformAudioFactory.Register(
            FailingResourceExtension,
            stream => new FailingWaveStream(stream),
            requiresFile: true);

        PlatformAudioFactory.Register(
            FailingReaderExtension,
            stream =>
            {
                LastReaderStreamPath = (stream as FileStream)?.Name;
                stream.Dispose();
                throw new InvalidDataException("The test reader factory always fails.");
            },
            requiresFile: true);
    }

    /// <summary>
    /// Un-claims the shared audio output, so nothing these tests touched leaks a device format
    /// into the rest of the suite.
    /// </summary>
    public void Dispose()
    {
        AudioSystem.Shutdown();
    }

    private static string? LastReaderStreamPath { get; set; }

    [Fact]
    public void A_deserialized_AudioResource_can_be_disposed_without_throwing()
    {
        //Arrange - exactly what the save-file loader produces.
        var resource = JsonSerializer.Deserialize<AudioResource>("""
            {"SourceFilePath":null,"AssetIdentifier":null,"SourceExtension":".wav","IsLooping":true}
            """, EngineState.SerializerOptions);

        //Act
        var act = () => resource!.Dispose();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void A_deserialized_AudioResource_is_not_finalized()
    {
        //Arrange - the finalizer is what actually took the process down, so this is the assertion
        // that matters. Creating one, dropping it and forcing a full finalization pass would have
        // crashed the host before the fix.
        CreateAndAbandonDeserializedResource();

        //Act
        var act = () =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        };

        //Assert
        act.Should().NotThrow();
    }

    // Kept out of the test method so the local cannot be kept alive by a debug-build stack slot.
    private static void CreateAndAbandonDeserializedResource()
    {
        _ = JsonSerializer.Deserialize<AudioResource>("""
            {"SourceExtension":".ogg","IsLooping":false}
            """, EngineState.SerializerOptions);
    }

    [Fact]
    public void LoadFromStream_disposes_the_reader_and_the_temp_file_when_the_resource_cannot_be_built()
    {
        //Arrange - the reader builds, then the resource rejects it. Before the guard, the reader,
        //the file handle under it and the temporary file all survived the failure.
        const string key = "failed_resource_load";
        FailingWaveStream.Last = null;
        AudioResourceManager.Instance.Unload(key);

        //Act
        var act = () => AudioResourceManager.Instance.LoadFromStream(
            key, new MemoryStream(new byte[] { 1, 2, 3, 4 }), FailingResourceExtension);

        //Assert - the ORIGINAL failure reaches the caller, and nothing is left behind.
        act.Should().Throw<InvalidDataException>();

        var reader = FailingWaveStream.Last;
        reader.Should().NotBeNull();
        reader!.WasDisposed.Should().BeTrue();
        reader.SourceWasClosed.Should().BeTrue();
        File.Exists(reader.SourcePath!).Should().BeFalse();
        AudioResourceManager.Instance.Contains(key).Should().BeFalse();
    }

    [Fact]
    public void LoadFromStream_deletes_the_temp_file_when_the_reader_cannot_be_created()
    {
        //Arrange - the failure happens before there is any reader to dispose, so the temporary
        //file this load wrote is the only thing left to clean up.
        const string key = "failed_reader_load";
        LastReaderStreamPath = null;
        AudioResourceManager.Instance.Unload(key);

        //Act
        var act = () => AudioResourceManager.Instance.LoadFromStream(
            key, new MemoryStream(new byte[] { 5, 6, 7, 8 }), FailingReaderExtension);

        //Assert
        act.Should().Throw<InvalidDataException>();
        LastReaderStreamPath.Should().NotBeNull();
        File.Exists(LastReaderStreamPath!).Should().BeFalse();
        AudioResourceManager.Instance.Contains(key).Should().BeFalse();
    }

    /// <summary>
    /// A reader that builds successfully and then refuses to describe its format, so the failure
    /// lands inside the <see cref="AudioResource"/> construction that follows it. It records its
    /// own disposal, which is what the cleanup guard is asserted on.
    /// </summary>
    private sealed class FailingWaveStream : WaveStream
    {
        private readonly Stream _source;

        internal FailingWaveStream(Stream source)
        {
            _source = source;
            SourcePath = (source as FileStream)?.Name;
            Last = this;
        }

        internal static FailingWaveStream? Last { get; set; }

        internal bool WasDisposed { get; private set; }

        internal string? SourcePath { get; }

        internal bool SourceWasClosed => !_source.CanRead;

        public override WaveFormat WaveFormat
            => throw new InvalidDataException("The test reader cannot describe its format.");

        public override long Length => 0;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;

            if (disposing)
            {
                _source.Dispose(); // a real reader owns the stream it was built over
            }

            base.Dispose(disposing);
        }
    }
}
