using System.Text.Json;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Validates that the <see cref="Spacing"/> record struct serializes with its
/// System.Text.Json property names (ported from Newtonsoft's <c>[JsonProperty]</c>).
/// </summary>
public class SpacingTests
{
    [Fact]
    public void Serialize_uses_lowercase_property_names()
    {
        //Arrange
        var spacing = new Spacing(1, 2, 3, 4);

        //Act
        var json = JsonSerializer.Serialize(spacing);

        //Assert
        json.Should().Contain("\"left\":1");
        json.Should().Contain("\"top\":2");
        json.Should().Contain("\"right\":3");
        json.Should().Contain("\"bottom\":4");
    }

    [Fact]
    public void Serialize_then_deserialize_roundtrips()
    {
        //Arrange
        var spacing = new Spacing(5, 10, 15, 20);

        //Act
        var back = JsonSerializer.Deserialize<Spacing>(JsonSerializer.Serialize(spacing));

        //Assert
        back.Should().Be(spacing);
    }

    [Fact]
    public void None_is_empty()
        => Spacing.None.IsEmpty.Should().BeTrue();
}
