using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the clone-to-another-layer defect fixed upstream after the vendored baseline: the copy
/// constructor built the movement controller and the collider against the SOURCE layer and the
/// destination layer was swapped in afterwards, so a clone wrapped and bounded against the wrong
/// grid. Also covers a throwing <see cref="SpriteManager.SpriteCreated"/> handler: the sprite the caller never
/// received is rolled back, so no orphan stays registered.
/// </summary>
public class SpriteManagerTests : IDisposable
{
    private readonly List<Scene> _scenes = new();
    private readonly List<Sprite> _createdSprites = new();

    /// <summary>Clears the global sprite and scene registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.SpriteCreated -= ThrowingHandler;
        SpriteManager.Instance.ClearImmediate();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private SceneLayer CreateLayer(int columns, int rows, int tileWidth, int tileHeight)
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene.AddLayer(
            columnCount: columns,
            rowCount: rows,
            width: tileWidth,
            height: tileHeight,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);
    }

    private static Sprite CreateSprite(SceneLayer layer, Vector2 position)
    {
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.RenderSize = new Size(layer.TileWidth, layer.TileHeight);
        sprite.SetPosition(position);
        return sprite;
    }

    [Fact]
    public void CloneSprite_to_a_different_layer_binds_movement_to_the_destination_layer()
    {
        //Arrange - a ten-column source layer and a three-column destination layer.
        var sourceLayer = CreateLayer(columns: 10, rows: 1, tileWidth: 32, tileHeight: 32);
        var destinationLayer = CreateLayer(columns: 3, rows: 1, tileWidth: 32, tileHeight: 32);
        var source = CreateSprite(sourceLayer, Vector2.Zero);

        //Act
        var clone = SpriteManager.Instance.CloneSprite(source, destinationLayer);
        clone.SetPosition(new Vector2(2f, 0f));
        clone.Movement.WrapX = true;
        clone.Movement.SetVelocity(new Vector2(2f, 0f));
        clone.Movement.AdvanceMovement(1f);

        //Assert - wrapping used the destination layer's three columns, not the source's ten.
        ReferenceEquals(destinationLayer, clone.SceneLayer).Should().BeTrue();
        clone.GetPosition().Should().Be(new Vector2(1f, 0f));
    }

    [Fact]
    public void CloneSprite_registers_the_clone_with_the_manager()
    {
        //Arrange
        var layer = CreateLayer(columns: 4, rows: 4, tileWidth: 16, tileHeight: 16);
        var source = CreateSprite(layer, new Vector2(1f, 1f));

        //Act
        var clone = SpriteManager.Instance.CloneSprite(source);

        //Assert
        SpriteManager.Instance.AllSprites.Should().Contain(clone);
        ReferenceEquals(layer, clone.SceneLayer).Should().BeTrue();
    }

    private void ThrowingHandler(Sprite sprite)
    {
        _createdSprites.Add(sprite);
        throw new InvalidOperationException("handler failed");
    }

    [Fact]
    public void CreateSprite_rolls_the_sprite_back_when_a_SpriteCreated_handler_throws()
    {
        //Arrange
        var layer = CreateLayer(columns: 4, rows: 4, tileWidth: 16, tileHeight: 16);
        SpriteManager.Instance.SpriteCreated += ThrowingHandler;

        //Act
        var act = () => SpriteManager.Instance.CreateSprite(layer, default, "orphan");

        //Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("handler failed");
        _createdSprites.Should().HaveCount(1);
        SpriteManager.Instance.AllSprites.Should().NotContain(_createdSprites[0]);
        _createdSprites[0].Collider.Should().BeNull();
    }

    [Fact]
    public void CloneSprite_rolls_the_clone_back_when_a_SpriteCreated_handler_throws()
    {
        //Arrange
        var layer = CreateLayer(columns: 4, rows: 4, tileWidth: 16, tileHeight: 16);
        var source = CreateSprite(layer, new Vector2(1f, 1f));
        SpriteManager.Instance.SpriteCreated += ThrowingHandler;

        //Act
        var act = () => SpriteManager.Instance.CloneSprite(source);

        //Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("handler failed");
        _createdSprites.Should().HaveCount(1);
        SpriteManager.Instance.AllSprites.Should().NotContain(_createdSprites[0]);
        SpriteManager.Instance.AllSprites.Should().Contain(source);
        _createdSprites[0].Collider.Should().BeNull();
    }
}
