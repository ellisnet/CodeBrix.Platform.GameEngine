using System;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class PixelFramePresenterTests
{
    /// <summary>A headless presenter that only counts paint requests.</summary>
    private sealed class TestPresenter : PixelFramePresenter
    {
        private int _paintRequests;

        public int PaintRequests => Volatile.Read(ref _paintRequests);

        protected override void RequestPaint() => Interlocked.Increment(ref _paintRequests);
    }

    private static byte[] SolidFrame(int width, int height, byte r, byte g, byte b)
    {
        var frame = new byte[width * height * 4];
        for (var i = 0; i < frame.Length; i += 4)
        {
            frame[i] = r;
            frame[i + 1] = g;
            frame[i + 2] = b;
            frame[i + 3] = 0xFF;
        }

        return frame;
    }

    [Fact]
    public void Configure_rejects_non_positive_dimensions()
    {
        //Arrange
        using var presenter = new TestPresenter();

        //Act
        Action zeroWidth = () => presenter.Configure(0, 200, PixelBufferFormat.Rgba8888);
        Action zeroHeight = () => presenter.Configure(320, 0, PixelBufferFormat.Rgba8888);

        //Assert
        zeroWidth.Should().Throw<ArgumentOutOfRangeException>();
        zeroHeight.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PresentFrame_requires_configuration_and_the_exact_length()
    {
        //Arrange
        using var presenter = new TestPresenter();
        Action unconfigured = () => presenter.PresentFrame(new byte[320 * 200 * 4]);

        //Act + Assert
        unconfigured.Should().Throw<InvalidOperationException>();

        presenter.Configure(320, 200, PixelBufferFormat.Rgba8888);
        Action wrongSize = () => presenter.PresentFrame(new byte[320 * 200 * 4 - 1]);
        wrongSize.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PresentFrame_requests_a_paint_per_presented_frame()
    {
        //Arrange
        using var presenter = new TestPresenter();
        presenter.Configure(4, 4, PixelBufferFormat.Rgba8888);

        //Act
        presenter.PresentFrame(SolidFrame(4, 4, 1, 2, 3));
        presenter.PresentFrame(SolidFrame(4, 4, 4, 5, 6));

        //Assert
        presenter.PaintRequests.Should().Be(2);
    }

    [Fact]
    public void Draw_shows_the_latest_presented_frame()
    {
        //Arrange
        using var presenter = new TestPresenter();
        presenter.Configure(4, 4, PixelBufferFormat.Rgba8888, scaleMode: PixelFrameScaleMode.Stretch);
        presenter.PresentFrame(SolidFrame(4, 4, 10, 20, 30));
        presenter.PresentFrame(SolidFrame(4, 4, 200, 100, 50)); // latest wins

        using var surface = SKSurface.Create(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));

        //Act
        presenter.DrawCurrentFrame(surface.Canvas, 4, 4);
        using var snapshot = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(snapshot);
        var center = pixels.GetPixel(2, 2);

        //Assert
        center.Red.Should().Be((byte)200);
        center.Green.Should().Be((byte)100);
        center.Blue.Should().Be((byte)50);
    }

    [Fact]
    public void Rotate90_draws_column_major_buffers_correctly()
    {
        //Arrange - logical 3x2 frame stored column-major (index = x * height + y).
        const int width = 3;
        const int height = 2;
        var frame = new byte[width * height * 4];
        void SetColumnMajorPixel(int x, int y, byte r)
        {
            var index = (x * height + y) * 4;
            frame[index] = r;
            frame[index + 3] = 0xFF;
        }

        // Unique red value per logical pixel: R = 10 + x + 3*y
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                SetColumnMajorPixel(x, y, (byte)(10 + x + 3 * y));
            }
        }

        using var presenter = new TestPresenter();
        presenter.Configure(width, height, PixelBufferFormat.Rgba8888,
            FrameOrientation.Rotate90, PixelFrameScaleMode.Stretch);
        presenter.PresentFrame(frame);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        //Act - draw at 1:1 so logical pixel (x, y) lands at surface (x, y).
        presenter.DrawCurrentFrame(surface.Canvas, width, height);
        using var snapshot = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(snapshot);

        //Assert - every logical pixel must appear at its logical position.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = (byte)(10 + x + 3 * y);
                pixels.GetPixel(x, y).Red.Should().Be(expected);
            }
        }
    }

    [Fact]
    public void Fit_letterboxes_and_maps_coordinates_both_ways()
    {
        //Arrange - 100x100 frame on a 300x100 surface: pillarbox bars 100 wide each side.
        using var presenter = new TestPresenter();
        presenter.Configure(100, 100, PixelBufferFormat.Rgba8888);
        presenter.PresentFrame(SolidFrame(100, 100, 1, 1, 1));

        using var surface = SKSurface.Create(new SKImageInfo(300, 100, SKColorType.Rgba8888, SKAlphaType.Premul));

        //Act
        presenter.DrawCurrentFrame(surface.Canvas, 300, 100);
        var center = presenter.WindowToBuffer(new SKPoint(150, 50));
        var frameLeftEdge = presenter.WindowToBuffer(new SKPoint(100, 0));
        var bufferOriginOnSurface = presenter.BufferToWindow(new SKPoint(0, 0));

        //Assert
        center!.Value.X.Should().Be(50f);
        center.Value.Y.Should().Be(50f);
        frameLeftEdge!.Value.X.Should().Be(0f);
        bufferOriginOnSurface!.Value.X.Should().Be(100f);
        bufferOriginOnSurface.Value.Y.Should().Be(0f);
    }

    [Fact]
    public void Coordinate_mapping_is_null_before_the_first_draw()
    {
        //Arrange
        using var presenter = new TestPresenter();
        presenter.Configure(320, 200, PixelBufferFormat.Rgba8888);

        //Act + Assert
        presenter.WindowToBuffer(new SKPoint(10, 10)).Should().BeNull();
        presenter.BufferToWindow(new SKPoint(10, 10)).Should().BeNull();
    }

    [Fact]
    public void PixelPerfect_uses_whole_number_scaling()
    {
        //Arrange - 100x100 frame on a 250x230 surface: integer scale 2, centered.
        using var presenter = new TestPresenter();
        presenter.Configure(100, 100, PixelBufferFormat.Rgba8888,
            scaleMode: PixelFrameScaleMode.PixelPerfect);
        presenter.PresentFrame(SolidFrame(100, 100, 1, 1, 1));

        using var surface = SKSurface.Create(new SKImageInfo(250, 230, SKColorType.Rgba8888, SKAlphaType.Premul));

        //Act
        presenter.DrawCurrentFrame(surface.Canvas, 250, 230);
        var origin = presenter.BufferToWindow(new SKPoint(0, 0));
        var extent = presenter.BufferToWindow(new SKPoint(100, 100));

        //Assert - a 200x200 draw centered in 250x230 starts at (25, 15).
        origin!.Value.X.Should().Be(25f);
        origin.Value.Y.Should().Be(15f);
        extent!.Value.X.Should().Be(225f);
        extent.Value.Y.Should().Be(215f);
    }

    [Fact]
    public void Reconfigure_switches_frame_sizes()
    {
        //Arrange - the Doom case: 320x200 then 640x400.
        using var presenter = new TestPresenter();
        presenter.Configure(320, 200, PixelBufferFormat.Rgba8888);
        presenter.PresentFrame(SolidFrame(320, 200, 1, 1, 1));

        //Act
        presenter.Configure(640, 400, PixelBufferFormat.Rgba8888);

        //Assert
        presenter.FrameWidth.Should().Be(640);
        Action oldSize = () => presenter.PresentFrame(SolidFrame(320, 200, 1, 1, 1));
        oldSize.Should().Throw<ArgumentException>();
        presenter.PresentFrame(SolidFrame(640, 400, 2, 2, 2));
    }

    [Fact]
    public void Uint_overload_presents_packed_pixels()
    {
        //Arrange
        using var presenter = new TestPresenter();
        presenter.Configure(2, 2, PixelBufferFormat.Rgba8888, scaleMode: PixelFrameScaleMode.Stretch);

        // Little-endian packed RGBA bytes R=0x11 G=0x22 B=0x33 A=0xFF => 0xFF332211u.
        var frame = new uint[] { 0xFF332211u, 0xFF332211u, 0xFF332211u, 0xFF332211u };

        using var surface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));

        //Act
        presenter.PresentFrame(frame);
        presenter.DrawCurrentFrame(surface.Canvas, 2, 2);
        using var snapshot = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(snapshot);
        var pixel = pixels.GetPixel(0, 0);

        //Assert
        pixel.Red.Should().Be((byte)0x11);
        pixel.Green.Should().Be((byte)0x22);
        pixel.Blue.Should().Be((byte)0x33);
    }
}
