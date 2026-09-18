using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Physics.Movement;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Camera behaviour on periodic layers: a wrapped axis is never clamped to the world bounds, and
/// a followed target is tracked through the nearest equivalent image rather than jumping a whole
/// period when the target's canonical position crosses a seam.
/// </summary>
public class WrappedCameraTests : IDisposable
{
    private readonly List<Scene> _scenes = new();

    /// <summary>Clears the global scene registry this fixture populated.</summary>
    public void Dispose()
    {
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ClampToWorldBounds_constrains_only_the_non_wrapped_axes(bool horizontal, bool vertical)
    {
        //Arrange
        var scene = CreateScene();
        var layer = scene.AddLayer(4, 4);
        layer.WrapHorizontally = horizontal;
        layer.WrapVertically = vertical;

        var camera = new Camera(scene)
        {
            WorldBoundsPx = new RectangleF(0, 0, 128, 128),
            GetVisibleWorldSizePx = () => new SizeF(32, 32)
        };

        //Act
        camera.SnapTo(new PointF(200, -30));

        //Assert
        camera.PositionPx.Should().Be(new PointF(horizontal ? 200 : 96, vertical ? -30 : 0));
    }

    [Fact]
    public void Update_follows_the_nearest_image_across_canonicalization()
    {
        //Arrange
        var scene = CreateScene();
        var layer = scene.AddLayer(4, 4);
        layer.WrapHorizontally = true;

        var camera = new Camera(scene)
        {
            WorldBoundsPx = new RectangleF(0, 0, 128, 128),
            GetVisibleWorldSizePx = () => new SizeF(32, 32)
        };

        camera.SnapTo(new PointF(110, 0));
        var target = new PointF(127, 16);
        camera.Follow(() => target, true);

        //Act
        camera.Update(1);

        //Assert
        camera.PositionPx.X.Should().Be(111f);

        //Act - the target crosses the seam; the camera keeps moving forward, not back a period.
        target.X = 1;
        camera.Update(1);

        //Assert
        camera.PositionPx.X.Should().Be(113f);
    }

    [Fact]
    public void FollowCenteredX_selects_the_wrapped_image_by_the_tracked_axis()
    {
        //Arrange
        var scene = CreateScene();
        var layer = scene.AddLayer(
            columnCount: 4,
            rowCount: 4,
            width: 32,
            height: 32,
            coordinateSystem: CoordinateSystemTypes.IsometricAxial);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        var camera = new Camera(scene) { GetVisibleWorldSizePx = () => new SizeF(32, 32) };
        camera.SnapTo(new PointF(184, 84)); // center = (200, 100)

        var target = new TestMovable(layer, MovementSpace.Pixel, new Vector2(130, 0));

        //Act
        camera.FollowCenteredX(target, hard: true);
        camera.Update(1);

        //Assert - the image nearest along the tracked axis wins, and the other axis is unchanged.
        camera.PositionPx.Should().Be(new PointF(178, 84));
    }

    private sealed class TestMovable(SceneLayer sceneLayer, MovementSpace positionSpace, Vector2 position)
        : IMovableOnSceneLayer
    {
        public MovementSpace PositionSpace { get; } = positionSpace;

        public SceneLayer SceneLayer { get; } = sceneLayer;

        private Vector2 Position { get; set; } = position;

        public Vector2 GetPosition() => Position;

        public void SetPosition(Vector2 pos) => Position = pos;
    }
}
