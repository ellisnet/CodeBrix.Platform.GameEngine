using System;
using System.Drawing;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Host.Input;
using CodeBrix.Platform.GameEngine.Rendering;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Pointer normalization: the input adapters report positions in logical Backbuffer ScreenPx, so a
/// click lands on the pixel that was drawn under it however the frame is fitted into the surface.
/// A live <see cref="Rendering.GameSurfaceCanvas"/> needs a platform head, so these exercise the
/// mapping over a test render surface adapter; the canvas wiring itself is verified on X11.
/// </summary>
public class PointerCoordinateMapperTests
{
    [Theory]
    [InlineData(100, 0, 0, 0)]        // the left edge of the presented image
    [InlineData(899, 599, 799, 599)]  // its bottom-right pixel
    [InlineData(99, 0, -1, 0)]        // one pixel into the left bar: outside, not clamped
    [InlineData(0, 0, -100, 0)]       // the far edge of the left bar
    [InlineData(900, 300, 800, 300)]  // one pixel past the right edge of the image
    public void ToScreenPx_maps_a_pillarboxed_surface_without_clamping_the_bars(
        float adapterX,
        float adapterY,
        int expectedX,
        int expectedY)
    {
        //Arrange - an 800x600 logical image presented in a wider surface.
        var adapter = new TestAdapter(800, 600, 1000, 600);

        //Act
        var screenPx = PointerCoordinateMapper.ToScreenPx(adapter, adapterX, adapterY);

        //Assert
        screenPx.Should().Be(new Point(expectedX, expectedY));
    }

    [Theory]
    [InlineData(0, 200, 0, 0)]        // the top edge of the presented image
    [InlineData(0, 199, 0, -1)]       // one pixel into the top bar
    [InlineData(400, 799, 400, 599)]  // its bottom edge
    [InlineData(400, 800, 400, 600)]  // one pixel past it
    public void ToScreenPx_maps_a_letterboxed_surface_without_clamping_the_bars(
        float adapterX,
        float adapterY,
        int expectedX,
        int expectedY)
    {
        //Arrange - the same image presented in a taller surface.
        var adapter = new TestAdapter(800, 600, 800, 1000);

        //Act
        var screenPx = PointerCoordinateMapper.ToScreenPx(adapter, adapterX, adapterY);

        //Assert
        screenPx.Should().Be(new Point(expectedX, expectedY));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 3, 1, 1)]          // one logical pixel covers two surface pixels
    [InlineData(1599, 1199, 799, 599)]
    public void ToScreenPx_divides_by_the_presentation_scale_when_the_image_is_enlarged(
        float adapterX,
        float adapterY,
        int expectedX,
        int expectedY)
    {
        //Arrange - 800x600 presented at exactly 2x, so there are no margins at all.
        var adapter = new TestAdapter(800, 600, 1600, 1200);

        //Assert
        adapter.PresentationScale.Should().BeApproximately(2f, 0.00001f);

        //Act
        var screenPx = PointerCoordinateMapper.ToScreenPx(adapter, adapterX, adapterY);

        //Assert
        screenPx.Should().Be(new Point(expectedX, expectedY));
    }

    [Fact]
    public void ToScreenPx_floors_a_fractional_margin_instead_of_truncating_it_to_the_edge()
    {
        //Arrange - an odd surface width puts the image on a half-pixel boundary.
        var adapter = new TestAdapter(800, 600, 901, 600);

        //Act - half a pixel to the LEFT of the image, which must not read as column zero.
        var screenPx = PointerCoordinateMapper.ToScreenPx(adapter, 50f, 0f);

        //Assert
        screenPx.Should().Be(new Point(-1, 0));
    }

    [Fact]
    public void ToScreenPx_follows_the_surface_when_it_is_resized_under_a_fixed_resolution()
    {
        //Arrange - a pinned resolution, first presented 1:1.
        var adapter = new TestAdapter(1280, 720, 1280, 720);
        PointerCoordinateMapper.ToScreenPx(adapter, 640f, 360f).Should().Be(new Point(640, 360));

        //Act - the window is dragged wider; the image is unchanged, its presentation is not.
        adapter.Resize(1920, 720);

        //Assert - the centre of the image moved with it, and the new bars map outside.
        PointerCoordinateMapper.ToScreenPx(adapter, 960f, 360f).Should().Be(new Point(640, 360));
        PointerCoordinateMapper.ToScreenPx(adapter, 319f, 360f).Should().Be(new Point(-1, 360));
    }

    [Theory]
    [InlineData(12.75f, 8.25f, 12, 8)]
    [InlineData(-0.25f, -0.25f, -1, -1)]
    public void ToScreenPx_passes_a_position_through_when_there_is_no_render_surface_adapter(
        float x,
        float y,
        int expectedX,
        int expectedY)
    {
        //Arrange & Act - an input adapter attached to a plain element, or one attached before the
        //canvas has built its scene pipeline.
        var screenPx = PointerCoordinateMapper.ToScreenPx(null, x, y);

        //Assert
        screenPx.Should().Be(new Point(expectedX, expectedY));
    }

    // The logical Backbuffer size reaches the adapter from the render surface host, which needs a
    // drawing surface this headless assembly has no native Skia for; these cases need the resulting
    // presentation state only, so they set it the way the host does.
    private sealed class TestAdapter : RenderSurfaceAdapterBase
    {
        private static readonly MethodInfo SetBackbufferSizeMethod =
            typeof(RenderSurfaceAdapterBase).GetMethod(
                "SetBackbufferSize", BindingFlags.Instance | BindingFlags.NonPublic)!;

        internal TestAdapter(int bufferWidth, int bufferHeight, int adapterWidth, int adapterHeight)
            : base(adapterWidth, adapterHeight)
        {
            SetBackbufferSizeMethod.Invoke(this, new object[] { bufferWidth, bufferHeight });
        }

        internal void Resize(int width, int height) => SetDestinationSize(width, height);

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
            => bufferImage.Dispose();
    }
}
