using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Sprites on periodic layers: layer wrapping is a rendering and query topology and never enables
/// <see cref="Physics.Movement.MovementController.WrapX"/> on its own, and a camera that follows a
/// sprite takes its topology from the sprite's own layer rather than the first visible layer.
/// </summary>
public class WrappedSpriteTests : IDisposable
{
    private readonly List<Scene> _scenes = new();

    /// <summary>Clears the global sprite and scene registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private Scene CreateScene()
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LayerTopology_does_not_change_the_movement_wrap_opt_in(bool layerWrap, bool movementWrap)
    {
        //Arrange
        var scene = CreateScene();
        var layer = scene.AddLayer(4, 4);
        layer.WrapHorizontally = layerWrap;
        layer.WrapVertically = layerWrap;

        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.SetPosition(new Vector2(3.9f, 1));
        sprite.Movement.WrapX = movementWrap;
        sprite.Movement.SetVelocity(new Vector2(1, 0));

        //Act
        sprite.Movement.AdvanceMovement(0.2f);

        //Assert - only the movement opt-in normalizes the sprite's own grid position.
        sprite.GetPosition().X.Should().BeApproximately(movementWrap ? 0.1f : 4.1f, 0.01f);
    }

    [Fact]
    public void FollowCentered_uses_the_followed_layer_and_a_normalized_sprite_collides_immediately()
    {
        //Arrange - a non-periodic background layer is added first and must NOT supply the topology.
        var scene = CreateScene();
        scene.AddLayer(20, 20);

        var layer = scene.AddLayer(4, 4);
        layer.WrapHorizontally = true;

        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.Visible = true;
        sprite.RenderSize = new Size(16, 16);
        sprite.Collider!.CollisionGroup = 1;
        sprite.Collider.CollidesWith = 1;
        sprite.CollisionType = TileCollisionType.Trigger;
        sprite.SetPosition(new Vector2(3.9f, 1));

        var camera = new Camera(scene)
        {
            WorldBoundsPx = new RectangleF(0, 0, 128, 128),
            GetVisibleWorldSizePx = () => new SizeF(32, 32)
        };

        camera.FollowCentered(sprite, hard: true);
        camera.SnapTo(new PointF(120, 0));
        camera.Update(1);
        float before = camera.PositionPx.X;

        //Act - the sprite normalizes across the seam; the camera keeps following the nearest image.
        sprite.Movement.WrapX = true;
        sprite.Movement.SetVelocity(new Vector2(1, 0));
        sprite.Movement.AdvanceMovement(0.2f);
        camera.Update(1);

        //Assert
        (camera.PositionPx.X - before).Should().BeInRange(5f, 8f);

        //Arrange - a tile on the far side of the seam from the sprite's canonical position.
        var tile = layer[0, 1]!;
        tile.CollisionsEnabled = true;
        tile.Collider!.CollisionGroup = 1;
        tile.Collider.CollidesWith = 1;

        bool hit = false;
        layer.CollisionResolver.TriggerOverlap += (a, b, _) =>
            hit |= ReferenceEquals(a, sprite.Collider) && ReferenceEquals(b, tile.Collider);

        //Act
        layer.CollisionResolver.Resolve();

        //Assert
        hit.Should().BeTrue();

        //Assert - the sprite also renders as a repeated instance one period to the right.
        var instances = layer.GetDrawablesInWorldRect(new Rectangle(125, 30, 30, 30)).Cast<WrappedDrawable>();
        instances.Any(d => ReferenceEquals(d.Owner, sprite) && d.Offset.X == 128).Should().BeTrue();
    }
}
