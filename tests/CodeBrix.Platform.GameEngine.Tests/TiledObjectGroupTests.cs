using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the object layer an import hands back as data: its defaults, the offset, visibility and
/// opacity the map can write, and the document index that places it among the imported tile layers.
/// </summary>
public class TiledObjectGroupTests
{
    [Fact]
    public void Defaults_describe_a_visible_opaque_layer_at_the_origin()
    {
        //Arrange & Act
        var group = new TiledObjectGroup { Name = "Spawns" };

        //Assert
        group.Name.Should().Be("Spawns");
        group.Objects.Should().BeEmpty();
        group.Offset.Should().Be(default(PointF));
        group.Visible.Should().BeTrue();
        group.Opacity.Should().Be(1.0f);
        group.DocumentIndex.Should().Be(0);
        group.Properties.Should().BeEmpty();
    }

    [Fact]
    public void An_offset_layer_reports_the_offset_beside_the_coordinates_the_map_wrote()
    {
        //Arrange
        var spawn = new TiledObject { Id = 1, Bounds = new RectangleF(18.0f, 36.0f, 16.0f, 16.0f) };

        //Act
        var group = new TiledObjectGroup
        {
            Name = "Spawns",
            Objects = [spawn],
            Offset = new PointF(16.0f, -8.0f),
        };

        //Assert
        group.Offset.Should().Be(new PointF(16.0f, -8.0f));
        //The offset is not folded into the object's own rectangle
        group.Objects[0].Bounds.X.Should().Be(18.0f);
        group.Objects[0].Bounds.Y.Should().Be(36.0f);
    }

    [Fact]
    public void A_hidden_or_faded_layer_keeps_its_objects()
    {
        //Arrange & Act
        var group = new TiledObjectGroup
        {
            Name = "Notes",
            Objects = [new TiledObject { Id = 4 }],
            Visible = false,
            Opacity = 0.5f,
        };

        //Assert
        group.Visible.Should().BeFalse();
        group.Opacity.Should().Be(0.5f);
        group.Objects.Should().ContainSingle();
    }

    [Fact]
    public void DocumentIndex_counts_tile_layers_and_object_layers_together()
    {
        //Arrange
        // A map holding tile layer, object layer, tile layer gives the object layer index 1, so a game
        // draws its content between the imported layers at ZOrderBase and ZOrderBase + 2.
        var options = new TiledMapImportOptions { ZOrderBase = 10 };

        //Act
        var group = new TiledObjectGroup { Name = "Spawns", DocumentIndex = 1 };

        //Assert
        group.DocumentIndex.Should().Be(1);
        (options.ZOrderBase + group.DocumentIndex).Should().Be(11);
    }

    [Fact]
    public void Layer_properties_are_kept_apart_from_object_properties()
    {
        //Arrange
        var spawn = new TiledObject
        {
            Id = 1,
            Properties = new Dictionary<string, string> { ["team"] = "blue" },
        };

        //Act
        var group = new TiledObjectGroup
        {
            Name = "Spawns",
            Objects = [spawn],
            Properties = new Dictionary<string, string> { ["purpose"] = "start-points" },
        };

        //Assert
        group.Properties["purpose"].Should().Be("start-points");
        group.Properties.Should().NotContainKey("team");
        group.Objects[0].Properties["team"].Should().Be("blue");
    }
}
