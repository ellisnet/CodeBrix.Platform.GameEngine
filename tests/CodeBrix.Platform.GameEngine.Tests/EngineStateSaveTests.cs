using System;
using System.IO;
using CodeBrix.Platform.GameEngine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Validates the System.Text.Json save pipeline: the versioned save-file envelope,
/// schema gating, and an empty-state save/load round-trip through
/// CodeBrix.Json.Extensions reference handling.
/// </summary>
public class EngineStateSaveTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"ge_state_{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveToFile_writes_versioned_schema_envelope()
    {
        //Arrange
        var path = TempPath();

        try
        {
            //Act
            new EngineState().SaveToFile(path);
            var json = File.ReadAllText(path);

            //Assert
            json.Should().Contain("\"schema\": 1");
            json.Should().Contain("\"state\"");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveToFile_then_LoadFromFile_roundtrips_empty_state()
    {
        //Arrange
        var path = TempPath();

        try
        {
            new EngineState().SaveToFile(path);

            //Act
            var act = () => EngineState.LoadFromFile(path);

            //Assert
            act.Should().NotThrow();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_rejects_unsupported_schema_version()
    {
        //Arrange
        var path = TempPath();
        File.WriteAllText(path, "{\"schema\":999,\"state\":{}}");

        try
        {
            //Act
            var act = () => EngineState.LoadFromFile(path);

            //Assert
            act.Should().Throw<NotSupportedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CurrentSaveSchemaVersion_is_one()
        => EngineState.CurrentSaveSchemaVersion.Should().Be(1);
}
