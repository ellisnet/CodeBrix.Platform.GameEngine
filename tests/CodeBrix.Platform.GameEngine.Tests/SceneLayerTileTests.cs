using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the layer-tile collider defect fixed upstream after the vendored baseline:
/// <see cref="SceneLayerTile"/> held its own collider field that HID <c>Tile.Collider</c>, so the
/// public <c>Collider</c> property was always null and <c>CollisionsEnabled = true</c> was a silent
/// no-op on fixed layer tiles.
/// </summary>
public class SceneLayerTileTests
{
    [Fact]
    public void Collider_is_configurable_and_registers_through_the_public_api()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(
            columnCount: 1,
            rowCount: 1,
            width: 32,
            height: 32,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        var tile = layer[0, 0]!;

        //Act
        var collider = tile.Collider;

        //Assert - the public property is populated at layer construction.
        collider.Should().NotBeNull();
        collider.Should().BeAssignableTo<ICollider>();

        //Act - configure and enable through the public API only.
        collider!.CollisionGroup = scene.CollisionGroups.WorldStatic;
        collider.CollidesWith = scene.CollisionGroups.Actors;
        tile.CollisionsEnabled = true;

        //Assert
        layer.ColliderRegistry.StaticColliders.Should().Contain(collider);

        //Act
        tile.CollisionsEnabled = false;

        //Assert
        layer.ColliderRegistry.StaticColliders.Contains(collider).Should().BeFalse();
    }
}
