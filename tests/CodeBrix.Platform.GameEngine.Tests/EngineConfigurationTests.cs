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
    public void the_fixed_update_hook_is_off_by_default_with_a_cap_of_five_steps()
    {
        //Arrange
        var config = new EngineConfiguration();

        //Act
        int rate = config.FixedUpdateRate;
        int maxSteps = config.MaxFixedUpdateSteps;

        //Assert
        rate.Should().Be(0);
        maxSteps.Should().Be(5);
    }

    [Fact]
    public void FixedUpdateRate_and_MaxFixedUpdateSteps_clamp_out_of_range_values()
    {
        //Arrange
        var config = new EngineConfiguration();

        //Act
        config.FixedUpdateRate = -30;
        config.MaxFixedUpdateSteps = 0;

        //Assert
        config.FixedUpdateRate.Should().Be(0);
        config.MaxFixedUpdateSteps.Should().Be(1);
    }

    [Fact]
    public void Load_reads_the_fixed_update_settings()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"gameengine-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"EngineConfig\": { \"FixedUpdateRate\": 60, \"MaxFixedUpdateSteps\": 3 } }");

        try
        {
            //Act
            var file = EngineConfigurationFile.Load(path);

            //Assert
            file.EngineConfig.FixedUpdateRate.Should().Be(60);
            file.EngineConfig.MaxFixedUpdateSteps.Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
        file.EngineConfig.StartInitializationWaitTimeout.Should().Be(30f);
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
    public void StartInitializationWaitTimeout_defaults_to_thirty_seconds()
        => new EngineConfiguration().StartInitializationWaitTimeout.Should().Be(30f);

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.PositiveInfinity)]
    public void StartInitializationWaitTimeout_clamps_non_positive_and_non_finite_values(float value)
    {
        //Arrange
        var config = new EngineConfiguration();

        //Act
        config.StartInitializationWaitTimeout = value;

        //Assert
        config.StartInitializationWaitTimeout.Should().Be(0.001f);
    }

    [Fact]
    public void StartInitializationWaitTimeout_caps_at_a_value_a_TimeSpan_can_express()
    {
        //Arrange
        var config = new EngineConfiguration();

        //Act
        config.StartInitializationWaitTimeout = float.MaxValue;

        //Assert (Start() feeds this straight to TimeSpan.FromSeconds, which overflows above the cap)
        Action toTimeSpan = () => TimeSpan.FromSeconds(config.StartInitializationWaitTimeout);
        toTimeSpan.Should().NotThrow();
        config.StartInitializationWaitTimeout.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void StartInitializationWaitTimeout_keeps_an_ordinary_positive_value()
    {
        //Arrange
        var config = new EngineConfiguration();

        //Act
        config.StartInitializationWaitTimeout = 2.5f;

        //Assert
        config.StartInitializationWaitTimeout.Should().Be(2.5f);
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
