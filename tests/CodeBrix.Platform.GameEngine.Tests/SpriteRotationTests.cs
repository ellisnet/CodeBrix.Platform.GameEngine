using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="Sprite.Rotation"/> and the axis-aligned bounds it produces:
/// <see cref="Sprite.VisualBoundsWorld"/> and <see cref="Sprite.GetVisualBoundsScreen(View)"/>.
/// Rotation is a rendering concern only — the collision rectangle stays axis-aligned.
/// </summary>
public class SpriteRotationTests : IDisposable
{
    private const float Tolerance = 0.001f;

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

    private SceneLayer CreateLayer(int columns = 10, int rows = 10, int tileWidth = 64, int tileHeight = 64)
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

    private Sprite CreateSprite(Size renderSize)
    {
        var layer = CreateLayer();
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.RenderSize = renderSize;
        sprite.SetPosition(new Vector2(3f, 4f));
        return sprite;
    }

    [Fact]
    public void Rotation_defaults_to_zero_and_leaves_the_visual_bounds_alone()
    {
        //Arrange
        var sprite = CreateSprite(new Size(64, 32));

        //Act
        var bounds = sprite.VisualBoundsWorld;

        //Assert - an unrotated sprite occupies exactly its draw rectangle.
        sprite.Rotation.Should().Be(0f);
        bounds.Should().Be(sprite.DrawLocationWorld);
    }

    [Fact]
    public void Rotation_expands_visual_bounds_around_the_render_centre()
    {
        //Arrange
        var sprite = CreateSprite(new Size(64, 32));
        var unrotated = sprite.DrawLocationWorld;

        //Act - a quarter turn swaps width and height about the same centre.
        sprite.Rotation = 90f;
        var rotated = sprite.VisualBoundsWorld;

        //Assert
        rotated.Height.Should().Be(unrotated.Width);
        rotated.Width.Should().Be(unrotated.Height);
        (rotated.Left + (rotated.Width / 2f)).Should().Be(unrotated.Left + (unrotated.Width / 2f));
        (rotated.Top + (rotated.Height / 2f)).Should().Be(unrotated.Top + (unrotated.Height / 2f));
    }

    [Fact]
    public void Rotation_at_45_degrees_grows_the_bounds_by_the_diagonal()
    {
        //Arrange - a square makes the expected diagonal easy to state; the sprite sits at
        //grid (3, 4) on a 64 px layer, so its draw rectangle is (192, 256, 64, 64).
        var sprite = CreateSprite(new Size(64, 64));
        var unrotated = sprite.DrawLocationWorld;
        unrotated.Should().Be(new Rectangle(192, 256, 64, 64));

        //Act
        sprite.Rotation = 45f;
        var rotated = sprite.VisualBoundsWorld;

        //Assert - a 64 px square turned a half-quarter spans its 90.51 px diagonal about the
        //same centre (224, 288), rounded outward to whole pixels.
        rotated.Should().Be(Rectangle.FromLTRB(178, 242, 270, 334));
        rotated.Contains(unrotated).Should().BeTrue();
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, 45f)]
    [InlineData(360f, 0f)]
    [InlineData(450f, 90f)]
    [InlineData(-90f, 270f)]
    [InlineData(-450f, 270f)]
    public void Rotation_normalises_into_the_zero_to_360_range(float assigned, float expected)
    {
        //Arrange
        var sprite = CreateSprite(new Size(32, 32));

        //Act
        sprite.Rotation = assigned;

        //Assert
        sprite.Rotation.Should().BeApproximately(expected, Tolerance);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Rotation_rejects_values_that_are_not_finite(float assigned)
    {
        //Arrange
        var sprite = CreateSprite(new Size(32, 32));

        //Act
        Action act = () => { sprite.Rotation = assigned; };

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        sprite.Rotation.Should().Be(0f);
    }

    [Fact]
    public void Rotation_leaves_the_collision_area_axis_aligned()
    {
        //Arrange
        var sprite = CreateSprite(new Size(64, 32));
        var collisionArea = sprite.CollisionArea;

        //Act
        sprite.Rotation = 30f;

        //Assert - rotation is a rendering concern; collisions do not move.
        sprite.CollisionArea.Should().Be(collisionArea);
    }

    [Fact]
    public void CloneSprite_preserves_rotation()
    {
        //Arrange
        var source = CreateSprite(new Size(64, 64));
        source.Rotation = 137.5f;

        //Act
        var clone = SpriteManager.Instance.CloneSprite(source);

        //Assert
        clone.Rotation.Should().BeApproximately(137.5f, Tolerance);
        clone.VisualBoundsWorld.Should().Be(source.VisualBoundsWorld);
    }

    [Fact]
    public void GetVisualBoundsScreen_expands_the_screen_rectangle_of_a_rotated_sprite()
    {
        //Arrange
        var layer = CreateLayer(tileWidth: 64, tileHeight: 64);
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.RenderSize = new Size(64, 32);
        sprite.SetPosition(new Vector2(1f, 1f));

        var viewport = new Viewport
        {
            TargetRectPx = new Rectangle(0, 0, 800, 600),
            ScreenOffsetPx = PointF.Empty,
            Zoom = 1f
        };

        var camera = new Camera(layer.Scene!);
        var view = new View(camera, viewport);
        camera.SnapTo(PointF.Empty);

        var unrotated = sprite.GetVisualBoundsScreen(view);

        //Act
        sprite.Rotation = 90f;
        var rotated = sprite.GetVisualBoundsScreen(view);

        //Assert - width and height swap about an unchanged centre.
        rotated.Width.Should().BeApproximately(unrotated.Height, Tolerance);
        rotated.Height.Should().BeApproximately(unrotated.Width, Tolerance);
        (rotated.Left + (rotated.Width / 2f)).Should().BeApproximately(
            unrotated.Left + (unrotated.Width / 2f), Tolerance);
        (rotated.Top + (rotated.Height / 2f)).Should().BeApproximately(
            unrotated.Top + (unrotated.Height / 2f), Tolerance);
    }

    [Fact]
    public void GetSpritesInWorldRectRange_finds_a_sprite_only_the_rotated_bounds_reach()
    {
        //Arrange - a wide, short sprite anchored at the layer origin: (0, 0, 64, 16).
        var layer = CreateLayer(tileWidth: 64, tileHeight: 64);
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.HorizAlign = HorizontalAlignment.Left;
        sprite.VertAlign = VerticalAlignment.Top;
        sprite.RenderSize = new Size(64, 16);
        sprite.SetPosition(new Vector2(0f, 0f));
        sprite.DrawLocationWorld.Should().Be(new Rectangle(0, 0, 64, 16));

        //A probe well below the unrotated sprite finds nothing.
        var probeRect = new Rectangle(28, 25, 4, 4);
        SpriteManager.Instance.GetSpritesInWorldRectRange(probeRect, layer).Should().BeEmpty();

        //Act - a quarter turn about the centre makes it span x 24..40, y -24..40.
        sprite.Rotation = 90f;

        //Assert
        sprite.VisualBoundsWorld.Should().Be(Rectangle.FromLTRB(24, -24, 40, 40));
        SpriteManager.Instance.GetSpritesInWorldRectRange(probeRect, layer).Should().Contain(sprite);
    }
}
