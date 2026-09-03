using System;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="Tile"/> lifetime behaviour that is not tied to one concrete tile kind: now
/// that layer tiles carry real colliders, disposing a tile whose scene layer was never assigned
/// (a deserialized tile that is discarded before rehydration, for instance) must not throw.
/// </summary>
public class TileTests : IDisposable
{
    /// <summary>Clears the global scene registry in case a test populated it.</summary>
    public void Dispose()
    {
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Dispose_does_not_throw_for_a_tile_with_no_scene_layer()
    {
        //Arrange - the deserialization constructor leaves parentSceneLayer null.
        var tile = new SceneLayerTile();

        //Act
        var act = () => tile.Dispose();

        //Assert
        act.Should().NotThrow();
        tile.Collider.Should().BeNull();
    }

    [Fact]
    public void Dispose_unregisters_the_collider_of_a_layer_tile()
    {
        //Arrange
        var scene = new Scene();
        var layer = scene.AddLayer(columnCount: 2, rowCount: 2, width: 16, height: 16, zOrder: 0, parallax: 1f);
        var tile = layer[0, 0]!;
        tile.CollisionsEnabled = true;
        tile.Collider.Should().NotBeNull();

        //Act
        tile.Dispose();

        //Assert
        tile.Collider.Should().BeNull();
        layer.ColliderRegistry.StaticColliders.Should().NotContain(c => ReferenceEquals(c.Owner, tile));
        layer.ColliderRegistry.DynamicColliders.Should().NotContain(c => ReferenceEquals(c.Owner, tile));

        scene.Dispose();
    }
}
