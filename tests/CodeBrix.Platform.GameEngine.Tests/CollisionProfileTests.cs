using System;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Collisions;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the scene-level collision profile system: named profiles resolve collision group names
/// through the scene's <see cref="CollisionGroupRegistry"/>, layers apply their default profile to
/// every fixed tile, sprites take the sprite manager's default profile, and state configured
/// before a collider exists is applied when one is attached.
/// </summary>
public class CollisionProfileTests : IDisposable
{
    /// <summary>Clears the global sprite and scene registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();
        Scene.ClearAllScenes();
        TilesheetRegistry.Instance.Clear();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetMask_combines_registered_groups_and_an_empty_collection_means_none()
    {
        //Arrange
        var groups = new CollisionGroupRegistry();

        //Act
        var empty = groups.GetMask(Array.Empty<string>());
        var combined = groups.GetMask(new[] { "Actors", "Projectiles" });

        //Assert
        empty.Should().Be(CollisionMasks.None);
        combined.Should().Be(groups.Actors | groups.Projectiles);
    }

    [Fact]
    public void DefaultTileCollisionProfile_applies_to_every_fixed_tile_on_the_layer()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(columnCount: 2, rowCount: 1, width: 16, height: 16);

        //Assert - every tile starts on the standard "World" profile.
        foreach (SceneLayerTile tile in layer)
        {
            tile.CollisionProfileName.Should().Be(CollisionProfileNames.World);
            tile.Collider!.CollisionGroup.Should().Be(scene.CollisionGroups.WorldStatic);
            tile.Collider.CollidesWith
                .Should().Be(scene.CollisionGroups.Actors | scene.CollisionGroups.Projectiles);
        }

        //Act
        layer.DefaultTileCollisionProfile = CollisionProfileNames.Sensor;

        //Assert
        foreach (SceneLayerTile tile in layer)
        {
            tile.CollisionProfileName.Should().Be(CollisionProfileNames.Sensor);
            tile.Collider!.CollisionGroup.Should().Be(scene.CollisionGroups.Triggers);
            tile.Collider.CollidesWith.Should().Be(scene.CollisionGroups.Actors);
        }
    }

    [Fact]
    public void ResolveCollisionGroup_resolves_a_custom_profile_through_the_scene_groups()
    {
        //Arrange
        using var scene = new Scene();
        int enemies = scene.CollisionGroups.Define("Enemies");

        //Act
        var profile = scene.CollisionProfiles.Define(
            "Enemy",
            "Enemies",
            new[] { "Actors", "Projectiles" });

        //Assert
        profile.ResolveCollisionGroup(scene.CollisionGroups).Should().Be(enemies);
        profile.ResolveCollidesWith(scene.CollisionGroups)
            .Should().Be(scene.CollisionGroups.Actors | scene.CollisionGroups.Projectiles);
    }

    [Fact]
    public void AttachCollider_applies_the_state_configured_before_the_collider_existed()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);
        using var tile = new DeferredColliderTile(layer);

        tile.SetCollisionProfile(CollisionProfileNames.Actor);
        tile.CollisionType = TileCollisionType.Trigger;
        tile.Collider.Should().BeNull();

        //Act
        tile.AttachTestCollider();

        //Assert - the profile, the response type and the registration all landed at once.
        var collider = tile.Collider;
        collider.Should().NotBeNull();
        collider!.CollisionGroup.Should().Be(scene.CollisionGroups.Actors);
        collider.ResponseType.Should().Be(CollisionResponseType.Trigger);
        tile.CollisionsEnabled.Should().BeTrue();
        layer.ColliderRegistry.DynamicColliders.Should().Contain(collider);

        //Act
        tile.CollisionType = TileCollisionType.None;

        //Assert
        layer.ColliderRegistry.DynamicColliders.Contains(collider).Should().BeFalse();
    }

    [Fact]
    public void CollisionProfileName_on_a_sprite_resolves_when_its_layer_joins_a_scene()
    {
        //Arrange - the layer has no scene yet, so the profile name is only retained.
        var layer = new SceneLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);
        var bitmap = new SKBitmap(16, 16);
        using var tilesheet = TilesheetFactory.FromBitmap("DeferredProfile", bitmap);
        tilesheet.DefaultRegion.TileSize = new Size(16, 16);

        var sprite = SpriteManager.Instance.CreateSprite(
            layer,
            tilesheet.GetFrame(0, 0),
            collisionProfileName: CollisionProfileNames.Actor);

        sprite.Collider!.CollisionGroup.Should().Be(CollisionMasks.None);

        //Act
        using var scene = new Scene();
        scene.AddLayer(layer);

        //Assert
        sprite.Collider.CollisionGroup.Should().Be(scene.CollisionGroups.Actors);
        sprite.Collider.CollidesWith.Should().Be(
            scene.CollisionGroups.WorldStatic |
            scene.CollisionGroups.Actors |
            scene.CollisionGroups.Projectiles |
            scene.CollisionGroups.Triggers);
    }

    /// <summary>
    /// A tile that does not build a collider in its constructor, so a test can configure collision
    /// state first and attach the collider afterwards.
    /// </summary>
    private sealed class DeferredColliderTile : Tile
    {
        private readonly SceneLayer _sceneLayer;

        internal DeferredColliderTile(SceneLayer sceneLayer)
        {
            _sceneLayer = sceneLayer;
        }

        public override bool IsPositionFixed => false;

        public override Rectangle DrawLocationWorld => new(0, 0, 16, 16);

        public override PointF SceneLayerCoordinates => PointF.Empty;

        public override SceneLayer SceneLayer => _sceneLayer;

        internal void AttachTestCollider()
            => AttachCollider(new TileCollider(this, CollisionMasks.None, CollisionMasks.None));
    }
}
