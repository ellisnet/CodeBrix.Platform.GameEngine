using System;
using System.Text.Json;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers disposal of an <see cref="AudioResource"/> that was DESERIALIZED rather than loaded — the
/// rehydration spec a save file carries, which owns no output device or wave stream.
/// </summary>
/// <remarks>
/// These pin a fix for a process-killing defect: the deserialization factory
/// (EngineSaveContractResolver) builds these through the parameterless constructor, leaving the
/// device and stream fields null, and disposal ran <c>outputDevice.Dispose()</c> against them
/// unguarded. From the finalizer thread that NullReferenceException is unhandled, so it killed the
/// test host AFTER the run reported success — a crash that no assertion could catch.
/// </remarks>
public class AudioResourceDisposalTests
{
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
}
