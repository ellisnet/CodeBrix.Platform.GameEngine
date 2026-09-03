using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the scene-layer sibling of <see cref="DirectDarknessOverlay"/>: a world-bounded darkness
/// region that scrolls with its layer, refuses lights from other layers, and drops reveal sources
/// when a tracked light layer gives one up.
/// </summary>
public class DirectSceneLayerDarknessOverlayTests
{
    [Fact]
    public void Draw_carves_the_darkness_at_a_reveal_source_center()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var layer = host.Scene.AddLayer(columnCount: 2, rowCount: 2, width: 32, height: 32);

        using var backbuffer = new BitmapBackbuffer(64, 64);
        using var overlay = new DirectSceneLayerDarknessOverlay(host, layer, new Rectangle(0, 0, 64, 64));

        overlay.AddRevealSource(centerWorldPx: new PointF(32f, 32f), radiusWorldPx: 12f);

        //Act
        backbuffer.Canvas.Clear(SKColors.White);
        overlay.Draw(backbuffer, new RectangleF(0f, 0f, 64f, 64f));

        //Assert
        using var snapshot = backbuffer.Snapshot();
        using var result = SKBitmap.FromImage(snapshot);
        var center = result.GetPixel(32, 32);
        var corner = result.GetPixel(0, 0);

        center.Should().Be(SKColors.White);
        (center.Red > corner.Red).Should().BeTrue();
    }

    [Fact]
    public void TrackLight_syncs_the_reveal_source_when_the_light_moves()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var layer = host.Scene.AddLayer(columnCount: 2, rowCount: 2, width: 32, height: 32);

        using var light = new DirectRadialLight(
            Color.FromArgb(180, 255, 190, 80),
            host,
            layer,
            new PointF(12f, 14f),
            8f);

        using var overlay = new DirectSceneLayerDarknessOverlay(host, layer, new Rectangle(0, 0, 64, 64));
        var reveal = overlay.TrackLight(light, radiusScale: 2f, intensityScale: 0.5f);

        //Act
        light.Intensity = 0.8f;
        light.SetRadius(12f);
        light.MoveTo(new PointF(24.25f, 28.5f));

        //Assert
        reveal.CenterWorldPx.Should().Be(new PointF(24.25f, 28.5f));
        reveal.RadiusWorldPx.Should().BeApproximately(24f, 0.001f);
        reveal.Intensity.Should().BeApproximately(0.4f, 0.001f);
    }

    [Fact]
    public void TrackLight_rejects_a_light_on_a_different_scene_layer()
    {
        //Arrange - cross-layer light spill is deliberately not supported
        using var host = new TestRenderSurfaceHost();
        var darknessLayer = host.Scene.AddLayer(2, 2, 32, 32);
        var lightLayer = host.Scene.AddLayer(2, 2, 32, 32);

        using var light = new DirectRadialLight(
            Color.FromArgb(180, 255, 190, 80),
            host,
            lightLayer,
            new PointF(12f, 14f),
            8f);

        using var overlay = new DirectSceneLayerDarknessOverlay(host, darknessLayer, new Rectangle(0, 0, 64, 64));

        //Act
        var trackForeignLight = () => overlay.TrackLight(light);

        //Assert
        trackForeignLight.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackLightLayer_removes_the_reveal_source_when_the_light_is_removed()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var layer = host.Scene.AddLayer(2, 2, 32, 32);
        using var lights = new DirectLightLayer(host, layer);
        using var overlay = new DirectSceneLayerDarknessOverlay(host, layer, new Rectangle(0, 0, 64, 64));

        overlay.TrackLightLayer(lights);

        //Act - a light added AFTER the layer is tracked is picked up automatically
        var torch = lights.AddTorchLight(new PointF(12f, 14f), 8f);
        overlay.RevealSources.Should().ContainSingle();

        lights.Remove(torch);

        //Assert
        overlay.RevealSources.Should().BeEmpty();
    }
}
