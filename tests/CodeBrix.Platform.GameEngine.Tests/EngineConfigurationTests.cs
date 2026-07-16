using System.Text.Json;
using CodeBrix.Platform.GameEngine.Configuration;
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
}
