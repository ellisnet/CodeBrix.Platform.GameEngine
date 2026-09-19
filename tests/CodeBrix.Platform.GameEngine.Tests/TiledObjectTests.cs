using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the object data an import hands back for one object of a tile map's object layer: the
/// defaults an unremarkable shape object carries, the rotation and visibility the map can write, and
/// the tile a tile object draws.
/// </summary>
public class TiledObjectTests
{
    [Fact]
    public void Defaults_describe_a_visible_unrotated_shape_object()
    {
        //Arrange & Act
        var mapObject = new TiledObject();

        //Assert
        mapObject.Id.Should().Be(0);
        mapObject.Name.Should().BeEmpty();
        mapObject.Type.Should().BeEmpty();
        mapObject.Bounds.Should().Be(default(RectangleF));
        mapObject.Rotation.Should().Be(0.0f);
        mapObject.Visible.Should().BeTrue();
        mapObject.Tile.Should().BeNull();
        mapObject.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Bounds_and_Rotation_are_kept_apart_so_the_rectangle_stays_unrotated()
    {
        //Arrange & Act
        var mapObject = new TiledObject
        {
            Id = 7,
            Name = "start",
            Type = "spawn",
            Bounds = new RectangleF(18.0f, 36.0f, 16.0f, 32.0f),
            Rotation = 45.0f,
        };

        //Assert
        mapObject.Bounds.Should().Be(new RectangleF(18.0f, 36.0f, 16.0f, 32.0f));
        mapObject.Rotation.Should().Be(45.0f);
    }

    [Fact]
    public void An_invisible_object_is_reported_rather_than_dropped()
    {
        //Arrange & Act
        var mapObject = new TiledObject { Name = "hidden", Visible = false };

        //Assert
        mapObject.Visible.Should().BeFalse();
        mapObject.Name.Should().Be("hidden");
    }

    [Fact]
    public void A_tile_object_carries_the_tile_it_draws()
    {
        //Arrange
        var tile = new TiledTileInfo
        {
            LayerName = "Props",
            GlobalTileId = 42,
            LocalTileId = 41,
            TilesetName = "tiles",
            TileType = "crate",
            TileProperties = new Dictionary<string, string> { ["collision"] = "blocking" },
            FlippedHorizontally = true,
        };

        //Act
        var mapObject = new TiledObject { Id = 3, Bounds = new RectangleF(0, 18, 18, 18), Tile = tile };

        //Assert
        mapObject.Tile.Should().NotBeNull();
        mapObject.Tile!.TilesetName.Should().Be("tiles");
        mapObject.Tile.GlobalTileId.Should().Be(42);
        mapObject.Tile.LocalTileId.Should().Be(41);
        mapObject.Tile.TileType.Should().Be("crate");
        mapObject.Tile.TileProperties["collision"].Should().Be("blocking");
        mapObject.Tile.FlippedHorizontally.Should().BeTrue();
        //An object sits at pixel coordinates, so it has no cell of its own
        mapObject.Tile.LayerName.Should().Be("Props");
        mapObject.Tile.Column.Should().Be(0);
        mapObject.Tile.Row.Should().Be(0);
    }

    [Fact]
    public void An_altered_copy_keeps_the_untouched_members()
    {
        //Arrange
        var mapObject = new TiledObject { Id = 9, Rotation = 90.0f, Visible = false };

        //Act
        var altered = mapObject with { Rotation = 0.0f };

        //Assert
        altered.Id.Should().Be(9);
        altered.Visible.Should().BeFalse();
        altered.Rotation.Should().Be(0.0f);
        mapObject.Rotation.Should().Be(90.0f);
    }
}
