using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeBrix.Platform.GameEngine.Configuration;
using Microsoft.Extensions.Configuration;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Validates that <see cref="EngineConfiguration"/> round-trips through System.Text.Json
/// after the Newtonsoft -> STJ save-pipeline port.
/// </summary>
public class EngineConfigurationTests
{
    [Fact]
    public void Serialize_then_deserialize_preserves_values()
    {
        //Arrange
        var config = new EngineConfiguration
        {
            TargetFPS = 30,
            SamplingTimeForCPS = 2.5,
            TimeBetweenKeyboardEvents = 0.05,
        };

        //Act
        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<EngineConfiguration>(json);

        //Assert
        back.Should().NotBeNull();
        back!.TargetFPS.Should().Be(30);
        back.SamplingTimeForCPS.Should().Be(2.5);
        back.TimeBetweenKeyboardEvents.Should().Be(0.05);
    }

    [Fact]
    public void Default_TargetFPS_is_positive()
        => new EngineConfiguration().TargetFPS.Should().BeGreaterThan(0);

    [Fact]
    public void Load_reads_the_shipped_default_file()
    {
        //Arrange
        var shipped = Path.Combine(AppContext.BaseDirectory, "gameengine.json");
        File.Exists(shipped).Should().BeTrue();

        //Act
        using var document = JsonDocument.Parse(File.ReadAllText(shipped));
        var file = EngineConfigurationFile.Load(shipped);

        //Assert (the root key must be the section name Load() asks for, or the file is ignored)
        document.RootElement.TryGetProperty(nameof(EngineConfigurationFile.EngineConfig), out _)
            .Should().BeTrue();
        file.EngineConfig.TargetFPS.Should().Be(60);
        file.EngineConfig.SamplingTimeForCPS.Should().Be(1.5);
        file.EngineConfig.TimeBetweenMouseEvents.Should().Be(0.03);
    }

    [Fact]
    public void Load_reads_values_from_a_file_with_the_EngineConfig_root_key()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"gameengine-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"EngineConfig\": { \"TargetFPS\": 42, \"SamplingTimeForCPS\": 2.5 } }");

        try
        {
            //Act
            var file = EngineConfigurationFile.Load(path);

            //Assert
            file.EngineConfig.TargetFPS.Should().Be(42);
            file.EngineConfig.SamplingTimeForCPS.Should().Be(2.5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_saves_the_configuration_when_AutoSave_is_enabled()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"gameengine-{Guid.NewGuid():N}.json");

        try
        {
            //Act
            var file = EngineConfigurationFile.CreateNew(path, autoSave: true);
            file.EngineConfig.TargetFPS = 42;
            file.Dispose();

            //Assert (the file exists, and what it wrote round-trips through Load)
            File.Exists(path).Should().BeTrue();
            EngineConfigurationFile.Load(path).EngineConfig.TargetFPS.Should().Be(42);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Load_reads_the_file_once_and_leaves_no_reload_watcher_behind()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"gameengine-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"EngineConfig\": { \"TargetFPS\": 42 } }");

        try
        {
            //Act
            var root = EngineConfigurationFile.BuildConfigurationRoot(path);

            try
            {
                var source = root.Providers.OfType<FileConfigurationProvider>().Single().Source;

                //Assert - a reloading provider keeps a live file watcher for the life of the process,
                //and Load() drops the root as soon as it has read the settings.
                source.ReloadOnChange.Should().BeFalse();
            }
            finally
            {
                (root as IDisposable)?.Dispose();
            }

            EngineConfigurationFile.Load(path).EngineConfig.TargetFPS.Should().Be(42);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_does_not_save_the_configuration_when_AutoSave_is_disabled()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"gameengine-{Guid.NewGuid():N}.json");

        try
        {
            //Act
            var file = EngineConfigurationFile.CreateNew(path, autoSave: false);
            file.EngineConfig.TargetFPS = 42;
            file.Dispose();

            //Assert
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
