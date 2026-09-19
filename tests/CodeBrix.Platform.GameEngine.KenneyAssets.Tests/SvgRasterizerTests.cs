using System;
using System.Drawing;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates the size a vector is rasterized at - intrinsic, scaled, asked for outright, and capped -
/// and that a document whose content does not start at the origin still lands on the bitmap.
/// </summary>
public class SvgRasterizerTests
{
    [Fact]
    public void ResolveRasterSize_uses_the_intrinsic_size_when_nothing_is_asked_for()
    {
        //Act
        Size size = SvgRasterizer.ResolveRasterSize(new SizeF(800f, 480f), null, null);

        //Assert
        size.Width.Should().Be(800);
        size.Height.Should().Be(480);
    }

    [Theory]
    [InlineData(0.5f, 400, 240)]
    [InlineData(1f, 800, 480)]
    [InlineData(2.5f, 2000, 1200)]
    public void ResolveRasterSize_applies_the_scale_to_the_intrinsic_size(
        float scale, int expectedWidth, int expectedHeight)
    {
        //Act
        Size size = SvgRasterizer.ResolveRasterSize(new SizeF(800f, 480f), null, scale);

        //Assert
        size.Width.Should().Be(expectedWidth);
        size.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void ResolveRasterSize_prefers_an_explicit_size_over_the_scale()
    {
        //Act
        // An explicit size is used as asked, even when its aspect ratio is not the document's.
        Size size = SvgRasterizer.ResolveRasterSize(new SizeF(800f, 480f), new Size(128, 128), 4f);

        //Assert
        size.Width.Should().Be(128);
        size.Height.Should().Be(128);
    }

    [Fact]
    public void ResolveRasterSize_caps_a_scaled_size_and_keeps_its_aspect_ratio()
    {
        //Act
        Size size = SvgRasterizer.ResolveRasterSize(new SizeF(800f, 480f), null, 100f);

        //Assert
        size.Width.Should().Be(SvgRasterizer.MaxDimension);
        //800:480 is 5:3, so the capped height is 4096 * 0.6, to within a rounded pixel
        size.Height.Should().BeGreaterThanOrEqualTo(2457);
        size.Height.Should().BeLessThanOrEqualTo(2458);
    }

    [Fact]
    public void ResolveRasterSize_caps_a_size_that_was_asked_for_outright()
    {
        //Act
        Size size = SvgRasterizer.ResolveRasterSize(
            new SizeF(100f, 100f), new Size(8192, 4096), null);

        //Assert
        size.Width.Should().Be(SvgRasterizer.MaxDimension);
        size.Height.Should().Be(SvgRasterizer.MaxDimension / 2);
    }

    [Fact]
    public void ResolveRasterSize_never_returns_a_dimension_below_one_pixel()
    {
        //Act
        Size size = SvgRasterizer.ResolveRasterSize(new SizeF(10f, 2f), null, 0.01f);

        //Assert
        size.Width.Should().Be(1);
        size.Height.Should().Be(1);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ResolveRasterSize_rejects_a_scale_that_is_not_greater_than_zero(float scale)
    {
        //Arrange
        Action act = () => SvgRasterizer.ResolveRasterSize(new SizeF(10f, 10f), null, scale);

        //Act, Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-4, 10)]
    public void ResolveRasterSize_rejects_a_raster_size_with_a_non_positive_dimension(
        int width, int height)
    {
        //Arrange
        Action act = () => SvgRasterizer.ResolveRasterSize(
            new SizeF(10f, 10f), new Size(width, height), null);

        //Act, Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rasterize_draws_a_document_whose_content_does_not_start_at_the_origin()
    {
        //Arrange
        // No viewBox and content at negative coordinates, so the picture's cull rectangle starts left
        // of and above the origin. Scaling without translating by that origin would push the shape off
        // the canvas and rasterize an empty bitmap.
        using Stream stream = SvgStream(
            "<svg xmlns=\"http://www.w3.org/2000/svg\">" +
            "<rect x=\"-50\" y=\"-40\" width=\"50\" height=\"40\" fill=\"#FF0000\"/></svg>");

        //Act
        using SKBitmap bitmap = SvgRasterizer.Rasterize(stream, new Size(20, 20));

        //Assert
        bitmap.Width.Should().Be(20);
        bitmap.Height.Should().Be(20);

        SKColor middle = bitmap.GetPixel(10, 10);
        ((int)middle.Alpha).Should().BeGreaterThan(0);
        ((int)middle.Red).Should().BeGreaterThan(middle.Blue);
        ((int)middle.Red).Should().BeGreaterThan(middle.Green);
    }

    [Fact]
    public void Rasterize_scales_a_document_to_its_intrinsic_size_by_default()
    {
        //Arrange
        using Stream stream = SvgStream(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64px\" height=\"32px\" " +
            "viewBox=\"0 0 64 32\"><rect x=\"0\" y=\"0\" width=\"64\" height=\"32\" " +
            "fill=\"#00FF00\"/></svg>");

        //Act
        using SKBitmap bitmap = SvgRasterizer.Rasterize(stream);

        //Assert
        bitmap.Width.Should().Be(64);
        bitmap.Height.Should().Be(32);
    }

    [Fact]
    public void Rasterize_reports_data_that_is_not_a_renderable_document()
    {
        //Arrange
        using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a vector"));
        Action act = () => SvgRasterizer.Rasterize(stream, null, null, "Vector/broken.svg");

        //Act, Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*Vector/broken.svg*");
    }

    [Fact]
    public void Rasterize_rejects_a_null_stream()
    {
        //Arrange
        Action act = () => SvgRasterizer.Rasterize(null!);

        //Act, Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static Stream SvgStream(string markup) =>
        new MemoryStream(Encoding.UTF8.GetBytes(markup));
}
