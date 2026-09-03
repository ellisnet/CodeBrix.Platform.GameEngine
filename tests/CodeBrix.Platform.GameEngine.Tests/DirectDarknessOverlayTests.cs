using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the view-mode <see cref="DirectDarknessOverlay"/>: the darkness quad, the reveal
/// sources that punch holes in it, and the light-tracking that keeps those holes attached to a
/// <see cref="DirectRadialLight"/>.
/// </summary>
public class DirectDarknessOverlayTests
{
    [Fact]
    public void Draw_carves_the_darkness_at_a_reveal_source_center()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var view = AddView(host, new Rectangle(0, 0, 32, 32));
        var layer = host.Scene.AddLayer(columnCount: 1, rowCount: 1, width: 32, height: 32);
        using var backbuffer = new BitmapBackbuffer(32, 32);
        using var overlay = new DirectDarknessOverlay(host, view, layer);

        overlay.AddRevealSource(centerWorldPx: new PointF(16f, 16f), radiusWorldPx: 8f);

        //Act
        backbuffer.Canvas.Clear(SKColors.White);
        overlay.Draw(backbuffer, new RectangleF(0f, 0f, 32f, 32f));

        //Assert - the reveal fully removes the darkness at its center, and the untouched corner
        //stays darkened
        using var snapshot = backbuffer.Snapshot();
        using var result = SKBitmap.FromImage(snapshot);
        var center = result.GetPixel(16, 16);
        var corner = result.GetPixel(0, 0);

        center.Should().Be(SKColors.White);
        (center.Red > corner.Red).Should().BeTrue();
    }

    [Fact]
    public void TrackLight_syncs_the_reveal_source_when_the_light_moves()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var view = AddView(host, new Rectangle(0, 0, 64, 64));
        var layer = host.Scene.AddLayer(columnCount: 2, rowCount: 2, width: 32, height: 32);

        using var light = new DirectRadialLight(
            Color.FromArgb(180, 255, 190, 80),
            host,
            layer,
            new PointF(12f, 14f),
            8f);

        using var overlay = new DirectDarknessOverlay(host, view, layer);
        var reveal = overlay.TrackLight(light, radiusScale: 1.5f, intensityScale: 0.5f);

        //Act
        light.Intensity = 0.8f;
        light.SetRadius(12f);
        light.MoveTo(new PointF(24.25f, 28.5f));

        //Assert - center follows exactly; radius and intensity follow through their scales
        reveal.CenterWorldPx.Should().Be(new PointF(24.25f, 28.5f));
        reveal.RadiusWorldPx.Should().BeApproximately(18f, 0.001f);
        reveal.Intensity.Should().BeApproximately(0.4f, 0.001f);
    }

    [Fact]
    public void TrackLight_removes_the_reveal_source_when_the_light_is_disposed()
    {
        //Arrange
        using var host = new TestRenderSurfaceHost();
        var view = AddView(host, new Rectangle(0, 0, 64, 64));
        var layer = host.Scene.AddLayer(columnCount: 2, rowCount: 2, width: 32, height: 32);

        var light = new DirectRadialLight(
            Color.FromArgb(180, 255, 190, 80),
            host,
            layer,
            new PointF(12f, 14f),
            8f);

        using var overlay = new DirectDarknessOverlay(host, view, layer);
        overlay.TrackLight(light);
        overlay.RevealSources.Should().ContainSingle();

        //Act - a disposed light must not leave a phantom hole in the darkness
        light.Dispose();

        //Assert
        overlay.RevealSources.Should().BeEmpty();
    }

    private static View AddView(TestRenderSurfaceHost host, Rectangle bounds)
    {
        host.ViewManager.AddView(bounds, zOrder: 0);

        return host.ViewManager.Views.Single(view => view.Viewport.TargetRectPx == bounds);
    }
}
