using System;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers how <see cref="EngineState.MergeFromFile"/> treats IDs that repeat: a repeated ID inside the save
/// only decides which of its own copies wins and never turns overwriting on for the entries after it, and
/// a sprite that the merge skips or replaces is disposed so its collider stops taking part in collisions.
/// </summary>
public class EngineStateMergeTests : IDisposable
{
    private readonly string _savePath = Path.Combine(Path.GetTempPath(), $"ge_merge_{Guid.NewGuid():N}.json");

    /// <summary>Starts every test from an empty engine state.</summary>
    public EngineStateMergeTests()
    {
        ClearAllEngineState();
    }

    /// <summary>Clears the process-global registries this fixture populated and removes the save file.</summary>
    public void Dispose()
    {
        ClearAllEngineState();
        File.Delete(_savePath);
        GC.SuppressFinalize(this);
    }

    private static void ClearAllEngineState()
    {
        Assets.AssetsFile.ClearAll();
        TilesheetRegistry.Instance.Clear();
        Cycle.ClearAllAnimationCycles();
        SpriteManager.Instance.ClearImmediate();
        Scene.ClearAllScenes();
    }

    private static SceneLayer AddLayer(Scene scene) =>
        scene.AddLayer(columnCount: 4, rowCount: 4, width: 16, height: 16, zOrder: 0);

    private static Sprite CreateCollidingSprite(SceneLayer layer, string nickname)
    {
        var sprite = SpriteManager.Instance.CreateSprite(layer, default, nickname);
        sprite.CollisionsEnabled = true;
        return sprite;
    }

    private static int SpriteCollidersIn(SceneLayer layer) =>
        layer.ColliderRegistry.StaticColliders
            .Concat(layer.ColliderRegistry.DynamicColliders)
            .Count(collider => collider.Owner is Sprite);

    [Fact]
    public void a_repeated_scene_id_in_the_save_does_not_overwrite_later_live_scenes()
    {
        //Arrange - the save holds the same scene ID twice, then a scene that also exists live.
        _ = new Scene { ID = "scene-repeated" };
        _ = new Scene { ID = "scene-repeated" };
        _ = new Scene { ID = "scene-later" };
        Engine.Instance.State.SaveToFile(_savePath);

        Scene.ClearAllScenes();
        var liveLater = new Scene { ID = "scene-later" };

        //Act
        EngineState.MergeFromFile(_savePath, overwriteExisting: false);

        //Assert - the live scene is kept; the repeated ID appears once.
        ReferenceEquals(Scene.GetSceneByID("scene-later"), liveLater).Should().BeTrue();
        Scene.GetAllSceneIDs().Count(id => id == "scene-repeated").Should().Be(1);
    }

    [Fact]
    public void a_repeated_scene_id_in_the_save_never_replaces_the_live_scene_of_that_id()
    {
        //Arrange
        _ = new Scene { ID = "scene-repeated" };
        _ = new Scene { ID = "scene-repeated" };
        Engine.Instance.State.SaveToFile(_savePath);

        Scene.ClearAllScenes();
        var live = new Scene { ID = "scene-repeated" };

        //Act
        EngineState.MergeFromFile(_savePath, overwriteExisting: false);

        //Assert
        ReferenceEquals(Scene.GetSceneByID("scene-repeated"), live).Should().BeTrue();
        Scene.GetAllSceneIDs().Count(id => id == "scene-repeated").Should().Be(1);
    }

    [Fact]
    public void a_repeated_sprite_id_in_the_save_does_not_overwrite_later_live_sprites()
    {
        //Arrange
        var scene = new Scene { ID = "scene-sprites" };
        var layer = AddLayer(scene);
        SpriteManager.Instance.CreateSprite(layer, default, "sprite-repeated");
        SpriteManager.Instance.CreateSprite(layer, default, "sprite-repeated");
        SpriteManager.Instance.CreateSprite(layer, default, "sprite-later");
        Engine.Instance.State.SaveToFile(_savePath);

        SpriteManager.Instance.ClearImmediate();
        var liveLater = SpriteManager.Instance.CreateSprite(layer, default, "sprite-later");

        //Act
        EngineState.MergeFromFile(_savePath, overwriteExisting: false, parts: EngineStateParts.Sprites);

        //Assert
        ReferenceEquals(SpriteManager.Instance.GetSpriteByID("sprite-later"), liveLater).Should().BeTrue();
        SpriteManager.Instance.AllSprites.Count(sprite => sprite.Nickname == "sprite-repeated").Should().Be(1);
    }

    [Fact]
    public void a_skipped_incoming_sprite_leaves_no_collider_behind()
    {
        //Arrange - the save holds "hero" on its own scene; a different live scene also has a "hero".
        var savedLayer = AddLayer(new Scene { ID = "scene-saved" });
        CreateCollidingSprite(savedLayer, "hero");
        Engine.Instance.State.SaveToFile(_savePath);

        ClearAllEngineState();
        var liveHero = CreateCollidingSprite(AddLayer(new Scene { ID = "scene-live" }), "hero");

        //Act
        EngineState.MergeFromFile(_savePath, overwriteExisting: false);

        //Assert - the saved scene joined, its "hero" was skipped and holds no collider there.
        var mergedLayer = Scene.GetSceneByID("scene-saved")!.First();
        ReferenceEquals(SpriteManager.Instance.GetSpriteByID("hero"), liveHero).Should().BeTrue();
        SpriteCollidersIn(mergedLayer).Should().Be(0);
    }

    [Fact]
    public void a_replaced_live_sprite_is_disposed_and_leaves_no_collider_behind()
    {
        //Arrange
        var layer = AddLayer(new Scene { ID = "scene-arena" });
        CreateCollidingSprite(layer, "hero");
        Engine.Instance.State.SaveToFile(_savePath);

        var liveHero = SpriteManager.Instance.GetSpriteByID("hero")!;
        SpriteCollidersIn(layer).Should().Be(1);

        //Act
        EngineState.MergeFromFile(_savePath, overwriteExisting: true);

        //Assert - the saved "hero" replaced the live one, whose collider left the old layer.
        ReferenceEquals(SpriteManager.Instance.GetSpriteByID("hero"), liveHero).Should().BeFalse();
        SpriteManager.Instance.AllSprites.Count(sprite => sprite.Nickname == "hero").Should().Be(1);
        liveHero.Collider.Should().BeNull();
        SpriteCollidersIn(layer).Should().Be(0);
    }
}
