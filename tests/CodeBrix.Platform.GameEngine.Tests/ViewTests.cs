using System.Drawing;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Verifies the public zoom contract of <see cref="View"/>: values above one zoom in, the coordinate
/// conversions are invertible, and an anchored zoom keeps the world point under the cursor fixed for
/// the whole animation.
/// </summary>
public class ViewTests
{
    private const float Tolerance = 0.001f;

    [Theory]
    [InlineData(1f, 100f)]
    [InlineData(2f, 200f)]
    [InlineData(0.5f, 50f)]
    public void WorldPxToScreenPx_applies_conventional_zoom(float zoom, float expectedScreenDisplacement)
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100);
        var view = CreateView(scene, zoom: zoom, cameraPositionPx: new PointF(100f, 50f));

        //Act
        PointF screenPx = view.WorldPxToScreenPx(layer, new PointF(200f, 50f));

        //Assert
        screenPx.X.Should().BeApproximately(expectedScreenDisplacement, Tolerance);
        screenPx.Y.Should().BeApproximately(0f, Tolerance);
    }

    [Theory]
    [InlineData(1f, 100f)]
    [InlineData(2f, 50f)]
    [InlineData(0.5f, 200f)]
    public void ScreenPxToWorldPx_applies_inverse_zoom(float zoom, float expectedWorldDisplacement)
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100);
        var view = CreateView(scene, zoom: zoom, cameraPositionPx: new PointF(100f, 50f));

        //Act
        PointF worldPx = view.ScreenPxToWorldPx(layer, new PointF(100f, 0f));

        //Assert
        worldPx.X.Should().BeApproximately(100f + expectedWorldDisplacement, Tolerance);
        worldPx.Y.Should().BeApproximately(50f, Tolerance);
    }

    [Fact]
    public void WorldPxToScreenPx_returns_absolute_adapter_coordinates()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100);
        var view = CreateView(
            scene,
            zoom: 2f,
            targetRectPx: new Rectangle(400, 100, 800, 600),
            screenOffsetPx: new PointF(10f, 20f),
            cameraPositionPx: new PointF(100f, 50f));

        //Act
        PointF screenPx = view.WorldPxToScreenPx(layer, new PointF(125f, 70f));

        //Assert
        screenPx.X.Should().BeApproximately(460f, Tolerance);
        screenPx.Y.Should().BeApproximately(160f, Tolerance);
    }

    [Fact]
    public void ScreenPxToWorldPx_round_trips_with_camera_offsets_zoom_and_parallax()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100, parallax: 0.65f);
        var view = CreateView(
            scene,
            zoom: 1.75f,
            targetRectPx: new Rectangle(300, 120, 900, 500),
            screenOffsetPx: new PointF(7f, -3f),
            cameraPositionPx: new PointF(120f, 80f));
        var originalWorldPx = new PointF(450.25f, 275.75f);

        //Act
        PointF screenPx = view.WorldPxToScreenPx(layer, originalWorldPx);
        PointF restoredWorldPx = view.ScreenPxToWorldPx(layer, screenPx);

        //Assert
        restoredWorldPx.X.Should().BeApproximately(originalWorldPx.X, Tolerance);
        restoredWorldPx.Y.Should().BeApproximately(originalWorldPx.Y, Tolerance);
    }

    [Fact]
    public void WorldRectToScreenRect_scales_position_and_size_by_zoom()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100);
        var view = CreateView(scene, zoom: 2f, cameraPositionPx: new PointF(100f, 50f));

        //Act
        RectangleF screenRect = view.WorldRectToScreenRect(layer, new RectangleF(125f, 70f, 40f, 30f));

        //Assert
        screenRect.X.Should().BeApproximately(50f, Tolerance);
        screenRect.Y.Should().BeApproximately(40f, Tolerance);
        screenRect.Width.Should().BeApproximately(80f, Tolerance);
        screenRect.Height.Should().BeApproximately(60f, Tolerance);
    }

    [Fact]
    public void ScreenRectToWorldRect_round_trips_with_camera_offsets_zoom_and_parallax()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100, parallax: 0.65f);
        var view = CreateView(
            scene,
            zoom: 1.75f,
            targetRectPx: new Rectangle(300, 120, 900, 500),
            screenOffsetPx: new PointF(7f, -3f),
            cameraPositionPx: new PointF(120f, 80f));
        var originalWorldRect = new RectangleF(450.25f, 275.75f, 125.5f, 64.25f);

        //Act
        RectangleF screenRect = view.WorldRectToScreenRect(layer, originalWorldRect);
        RectangleF restoredWorldRect = view.ScreenRectToWorldRect(layer, screenRect);

        //Assert
        restoredWorldRect.X.Should().BeApproximately(originalWorldRect.X, Tolerance);
        restoredWorldRect.Y.Should().BeApproximately(originalWorldRect.Y, Tolerance);
        restoredWorldRect.Width.Should().BeApproximately(originalWorldRect.Width, Tolerance);
        restoredWorldRect.Height.Should().BeApproximately(originalWorldRect.Height, Tolerance);
    }

    [Fact]
    public void ZoomAroundScreenPoint_when_snapped_preserves_the_world_point_under_the_cursor()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100, parallax: 0.5f);
        var view = CreateView(
            scene,
            zoom: 1f,
            targetRectPx: new Rectangle(100, 50, 800, 600),
            screenOffsetPx: new PointF(20f, 10f),
            cameraPositionPx: new PointF(200f, 100f));
        var cursorScreenPx = new PointF(500f, 350f);
        PointF worldBefore = view.ScreenPxToWorldPx(layer, cursorScreenPx);

        //Act
        view.ZoomAroundScreenPoint(layer, cursorScreenPx, targetZoom: 2f, durationSeconds: 0f);
        PointF worldAfter = view.ScreenPxToWorldPx(layer, cursorScreenPx);

        //Assert
        view.Viewport.Zoom.Should().Be(2f);
        worldAfter.X.Should().BeApproximately(worldBefore.X, Tolerance);
        worldAfter.Y.Should().BeApproximately(worldBefore.Y, Tolerance);
    }

    [Fact]
    public void ZoomAroundScreenPoint_when_animated_preserves_the_world_anchor_every_update()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100, parallax: 0.65f);
        var view = CreateView(
            scene,
            zoom: 1f,
            targetRectPx: new Rectangle(100, 50, 800, 600),
            screenOffsetPx: new PointF(20f, 10f),
            cameraPositionPx: new PointF(200f, 100f));
        var screenAnchor = new PointF(500f, 350f);
        PointF worldAnchor = view.ScreenPxToWorldPx(layer, screenAnchor);

        //Act
        view.ZoomAroundScreenPoint(layer, screenAnchor, targetZoom: 2f, durationSeconds: 0.75f);

        //Assert
        for (int i = 0; i < 5; i++)
        {
            view.Update(0.15f);

            PointF currentWorldAnchor = view.ScreenPxToWorldPx(layer, screenAnchor);
            currentWorldAnchor.X.Should().BeApproximately(worldAnchor.X, Tolerance);
            currentWorldAnchor.Y.Should().BeApproximately(worldAnchor.Y, Tolerance);
        }

        view.Viewport.Zoom.Should().Be(2f);
        view.Viewport.IsZoomAnimating.Should().BeFalse();
    }

    [Fact]
    public void ZoomAroundScreenPoint_when_retargeted_preserves_the_anchor_without_wobble()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100);
        var view = CreateView(scene, zoom: 1f, cameraPositionPx: new PointF(80f, 40f));
        var screenAnchor = new PointF(375f, 225f);
        PointF worldAnchor = view.ScreenPxToWorldPx(layer, screenAnchor);

        //Act
        view.ZoomAroundScreenPoint(layer, screenAnchor, targetZoom: 1.5f, durationSeconds: 0.75f);
        view.Update(0.20f);

        //Assert
        PointF afterFirstUpdate = view.ScreenPxToWorldPx(layer, screenAnchor);
        afterFirstUpdate.X.Should().BeApproximately(worldAnchor.X, Tolerance);
        afterFirstUpdate.Y.Should().BeApproximately(worldAnchor.Y, Tolerance);

        //Act (retarget mid-animation)
        view.ZoomAroundScreenPoint(layer, screenAnchor, targetZoom: 2f, durationSeconds: 0.75f);

        //Assert
        for (int i = 0; i < 5; i++)
        {
            view.Update(0.15f);

            PointF currentWorldAnchor = view.ScreenPxToWorldPx(layer, screenAnchor);
            currentWorldAnchor.X.Should().BeApproximately(worldAnchor.X, Tolerance);
            currentWorldAnchor.Y.Should().BeApproximately(worldAnchor.Y, Tolerance);
        }

        view.Viewport.Zoom.Should().Be(2f);
    }

    [Fact]
    public void RenderContext_keeps_view_transform_stable_when_live_state_changes()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(100, 100, parallax: 0.75f);
        var view = CreateView(
            scene,
            zoom: 1.5f,
            targetRectPx: new Rectangle(100, 50, 800, 600),
            screenOffsetPx: new PointF(12f, -8f),
            cameraPositionPx: new PointF(120f, 80f));

        var worldPoint = new PointF(410f, 260f);
        var worldRect = new RectangleF(390f, 240f, 64f, 48f);
        var screenRect = new RectangleF(250f, 180f, 96f, 72f);

        PointF pointBefore;
        PointF pointAfter;
        RectangleF worldRectBefore;
        RectangleF worldRectAfter;
        RectangleF screenRectBefore;
        RectangleF screenRectAfter;
        Rectangle viewportBefore;
        Rectangle viewportAfter;

        //Act
        RenderContext.Push(view, tick: 1);
        try
        {
            pointBefore = view.WorldPxToScreenPx(layer, worldPoint);
            worldRectBefore = view.WorldRectToScreenRect(layer, worldRect);
            screenRectBefore = view.ScreenRectToWorldRect(layer, screenRect);
            viewportBefore = view.GetRenderViewportTargetRectPx();

            view.Camera.SnapTo(new PointF(500f, 350f));
            view.Viewport.Zoom = 2.5f;
            view.Viewport.TargetRectPx = new Rectangle(300, 200, 1024, 768);
            view.Viewport.ScreenOffsetPx = new PointF(-30f, 40f);

            pointAfter = view.WorldPxToScreenPx(layer, worldPoint);
            worldRectAfter = view.WorldRectToScreenRect(layer, worldRect);
            screenRectAfter = view.ScreenRectToWorldRect(layer, screenRect);
            viewportAfter = view.GetRenderViewportTargetRectPx();
        }
        finally
        {
            RenderContext.Pop();
        }

        //Assert (inside the pass every read used the snapshot)
        pointAfter.X.Should().BeApproximately(pointBefore.X, Tolerance);
        pointAfter.Y.Should().BeApproximately(pointBefore.Y, Tolerance);
        worldRectAfter.X.Should().BeApproximately(worldRectBefore.X, Tolerance);
        worldRectAfter.Y.Should().BeApproximately(worldRectBefore.Y, Tolerance);
        worldRectAfter.Width.Should().BeApproximately(worldRectBefore.Width, Tolerance);
        worldRectAfter.Height.Should().BeApproximately(worldRectBefore.Height, Tolerance);
        screenRectAfter.X.Should().BeApproximately(screenRectBefore.X, Tolerance);
        screenRectAfter.Y.Should().BeApproximately(screenRectBefore.Y, Tolerance);
        screenRectAfter.Width.Should().BeApproximately(screenRectBefore.Width, Tolerance);
        screenRectAfter.Height.Should().BeApproximately(screenRectBefore.Height, Tolerance);
        viewportAfter.Should().Be(viewportBefore);

        //Assert (outside the pass the live values are used again)
        view.WorldPxToScreenPx(layer, worldPoint).Should().NotBe(pointAfter);
        view.GetRenderViewportTargetRectPx().Should().NotBe(viewportAfter);
    }

    private static View CreateView(Scene scene,
                                   float zoom,
                                   Rectangle? targetRectPx = null,
                                   PointF? screenOffsetPx = null,
                                   PointF? cameraPositionPx = null)
    {
        var viewport = new Viewport
        {
            TargetRectPx = targetRectPx ?? new Rectangle(0, 0, 800, 600),
            ScreenOffsetPx = screenOffsetPx ?? PointF.Empty,
            Zoom = zoom
        };

        var camera = new Camera(scene);
        var view = new View(camera, viewport);

        camera.SnapTo(cameraPositionPx ?? PointF.Empty);

        return view;
    }
}
