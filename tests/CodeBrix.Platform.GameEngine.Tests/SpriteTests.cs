using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the two <see cref="Sprite"/> defects fixed upstream after the vendored baseline:
/// world-pixel translation used to rebuild the absolute position from the INTEGER collision
/// rectangle (losing sub-pixel position on every solid push-out), and <c>ZOrder</c> hid the base
/// property instead of overriding it, so the minimum-of-one clamp was bypassed whenever the sprite
/// was reached through a <see cref="Tile"/>-typed reference.
/// </summary>
public class SpriteTests : IDisposable
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

    private SceneLayer CreateLayer(int columns = 10, int rows = 10, int tileWidth = 16, int tileHeight = 16)
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
    public void TranslateWorldPx_keeps_fractional_position()
    {
        //Arrange - a sprite parked between two whole grid columns on a 16 px layer.
        var layer = CreateLayer(tileWidth: 16, tileHeight: 16);
        var sprite = CreateSprite(layer, new Vector2(2.4f, 3f));

        //Act - a three-pixel push-out along X, the shape collision resolution applies.
        sprite.TranslateWorldPx(3, 0);

        //Assert - the position moved by exactly 3/16 of a tile; the fraction survived.
        var position = sprite.GetPosition();
        Math.Abs(position.X - (2.4f + (3f / 16f))).Should().BeLessThan(0.0005f);
        Math.Abs(position.Y - 3f).Should().BeLessThan(0.0005f);
    }

    [Fact]
    public void TranslateWorldPx_with_no_offset_leaves_the_position_untouched()
    {
        //Arrange
        var layer = CreateLayer(tileWidth: 16, tileHeight: 16);
        var sprite = CreateSprite(layer, new Vector2(2.4f, 3.7f));

        //Act
        sprite.TranslateWorldPx(0, 0);

        //Assert
        var position = sprite.GetPosition();
        position.X.Should().Be(2.4f);
        position.Y.Should().Be(3.7f);
    }

    [Fact]
    public void ZOrder_clamps_to_one_through_a_base_tile_reference()
    {
        //Arrange
        var layer = CreateLayer();
        var sprite = CreateSprite(layer, new Vector2(1f, 1f));
        Tile tile = sprite;

        //Act - the clamp used to be bypassed here, because Sprite.ZOrder only HID Tile.ZOrder.
        tile.ZOrder = 0;

        //Assert
        tile.ZOrder.Should().Be(1);
        sprite.ZOrder.Should().Be(1);
    }

    [Fact]
    public void ZOrder_keeps_values_above_the_minimum()
    {
        //Arrange
        var layer = CreateLayer();
        var sprite = CreateSprite(layer, new Vector2(1f, 1f));
        Tile tile = sprite;

        //Act
        tile.ZOrder = 7;

        //Assert
        sprite.ZOrder.Should().Be(7);
        tile.ZOrder.Should().Be(7);
    }
}
