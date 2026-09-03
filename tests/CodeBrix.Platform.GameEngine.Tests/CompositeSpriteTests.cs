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
/// Covers the composite anchor defect fixed upstream after the vendored baseline:
/// <see cref="CompositeSprite.GetPosition"/> returned the anchor in WORLD PIXELS while
/// <see cref="CompositeSprite.SetPosition"/> and <see cref="CompositeSprite.AddChildWithOffset"/>
/// interpret vectors in GRID coordinates, so feeding a read position straight back into
/// <c>SetPosition</c> teleported the composite by a factor of the tile size.
/// </summary>
public class CompositeSpriteTests : IDisposable
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
    public void GetPosition_returns_the_anchor_in_grid_coordinates()
    {
        //Arrange - a non-square tile makes a pixel-space answer obvious if it ever comes back.
        var layer = CreateLayer(columns: 10, rows: 10, tileWidth: 32, tileHeight: 16);
        var first = CreateSprite(layer, new Vector2(2f, 3f));
        var second = CreateSprite(layer, new Vector2(4f, 5f));
        var composite = new CompositeSprite(first, second);

        //Act + Assert - top-left anchor.
        composite.AnchorMode = CompositeAnchorMode.TopLeft;
        composite.GetPosition().Should().Be(new Vector2(2f, 3f));

        //Act + Assert - centre anchor.
        composite.AnchorMode = CompositeAnchorMode.Center;
        composite.GetPosition().Should().Be(new Vector2(3.5f, 4.5f));
    }

    [Fact]
    public void SetPosition_moves_children_using_grid_space()
    {
        //Arrange
        var layer = CreateLayer(columns: 12, rows: 12, tileWidth: 32, tileHeight: 16);
        var first = CreateSprite(layer, new Vector2(2f, 3f));
        var second = CreateSprite(layer, new Vector2(4f, 5f));
        var composite = new CompositeSprite(first, second);

        //Act
        composite.SetPosition(new Vector2(6f, 7f));

        //Assert - the whole composite shifted by (4, 4) grid units.
        first.GetPosition().Should().Be(new Vector2(6f, 7f));
        second.GetPosition().Should().Be(new Vector2(8f, 9f));
    }

    [Fact]
    public void AddChildWithOffset_interprets_the_offset_in_grid_space()
    {
        //Arrange
        var layer = CreateLayer(columns: 10, rows: 10, tileWidth: 32, tileHeight: 16);
        var body = CreateSprite(layer, new Vector2(2f, 3f));
        var child = CreateSprite(layer, Vector2.Zero);
        var composite = new CompositeSprite(body);

        //Act
        composite.AddChildWithOffset(child, new Vector2(1f, -1f));

        //Assert
        child.GetPosition().Should().Be(new Vector2(3f, 2f));
    }
}
