using System;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers loading an SVG resource straight from a stream, the route an asset provider uses for a
/// vector asset that lives inside an archive, and where the rasterized content lands for documents
/// that are not anchored at the origin.
/// </summary>
/// <remarks>
/// Nothing here pins pixels. The placement assertions are coverage figures and the bounding box of
/// the non-transparent pixels, which is what "the content is there, and it is where the document
/// says" means without depending on a rasterizer's exact anti-aliasing.
/// </remarks>
public class SvgResourceTests
{
    private const string RedRectangleSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="16" viewBox="0 0 24 16">
          <rect x="0" y="0" width="24" height="16" fill="#ff0000" />
        </svg>
        """;

    // No width, height or viewBox, so the document's extent is its content — which an exporter left
    // entirely at negative coordinates. This is the shape that rasterized to nothing at all.
    private const string NegativeContentSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg">
          <rect x="-24" y="-16" width="24" height="16" fill="#ff0000" />
        </svg>
        """;

    // The same idea with the content straddling the origin, which rasterized clipped rather than empty.
    private const string StraddlingContentSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg">
          <rect x="-12" y="-8" width="24" height="16" fill="#ff0000" />
        </svg>
        """;

    // Content pushed away from the origin the other way. The document then runs from the origin to the
    // far corner of the content, so the art occupies the bottom-right of the raster - and must stay there.
    private const string OffsetContentSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg">
          <rect x="40" y="40" width="24" height="16" fill="#ff0000" />
        </svg>
        """;

    [Fact]
    public void Load_from_a_stream_reports_the_intrinsic_size()
    {
        //Arrange
        using var stream = SvgStream(RedRectangleSvg);

        //Act
        using var resource = SvgResource.Load(stream);

        //Assert
        resource.IntrinsicSize.Width.Should().Be(24f);
        resource.IntrinsicSize.Height.Should().Be(16f);
    }

    [Fact]
    public void Load_from_a_stream_produces_a_rasterizable_resource()
    {
        //Arrange
        using var stream = SvgStream(RedRectangleSvg);

        //Act
        using var resource = SvgResource.Load(stream);
        var bitmap = resource.Rasterize(48, 32);

        //Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().Be(48);
        bitmap.Height.Should().Be(32);
        resource.Rasterize(48, 32).Should().BeSameAs(bitmap);
    }

    [Fact]
    public void Load_rejects_a_missing_stream()
    {
        //Arrange & Act
        Action nullStream = () => SvgResource.Load((Stream)null!);

        //Assert
        nullStream.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rasterize_covers_an_origin_anchored_document_completely()
    {
        //Arrange
        using var resource = SvgResource.Load(SvgStream(RedRectangleSvg));

        //Act
        var bitmap = resource.Rasterize(24, 16);
        var content = OpaqueContent(bitmap);

        //Assert - the unchanged baseline: a document that starts at the origin fills the raster.
        content.Coverage.Should().BeGreaterThan(0.95f);
        content.MinX.Should().BeLessThan(2);
        content.MinY.Should().BeLessThan(2);
        content.MaxX.Should().BeGreaterThan(bitmap.Width - 3);
        content.MaxY.Should().BeGreaterThan(bitmap.Height - 3);
    }

    [Fact]
    public void Rasterize_fills_the_raster_for_content_at_negative_coordinates()
    {
        //Arrange - art an exporter left wholly at negative coordinates, which happens in real asset packs.
        using var resource = SvgResource.Load(SvgStream(NegativeContentSvg));

        //Act
        var bitmap = resource.Rasterize(24, 16);
        var content = OpaqueContent(bitmap);
        var middle = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

        //Assert - the whole point: this used to rasterize to nothing at all.
        content.Coverage.Should().BeGreaterThan(0.9f);
        ((int)middle.Alpha).Should().BeGreaterThan(0);
        ((int)middle.Red).Should().BeGreaterThan(middle.Green);
        ((int)middle.Red).Should().BeGreaterThan(middle.Blue);
    }

    [Fact]
    public void Rasterize_keeps_content_that_straddles_the_origin_whole()
    {
        //Arrange - half the art at negative coordinates, which used to be cropped away.
        using var resource = SvgResource.Load(SvgStream(StraddlingContentSvg));

        //Act
        var bitmap = resource.Rasterize(24, 16);
        var content = OpaqueContent(bitmap);

        //Assert - all four edges carry content, so nothing was cropped.
        content.Coverage.Should().BeGreaterThan(0.95f);
        content.MinX.Should().BeLessThan(2);
        content.MinY.Should().BeLessThan(2);
        content.MaxX.Should().BeGreaterThan(bitmap.Width - 3);
        content.MaxY.Should().BeGreaterThan(bitmap.Height - 3);
    }

    [Fact]
    public void Rasterize_leaves_positively_offset_content_where_the_document_puts_it()
    {
        //Arrange - the document runs from the origin to the far corner of art that sits well away from
        // it, so the art belongs in the bottom-right of the raster and must not be dragged elsewhere.
        using var resource = SvgResource.Load(SvgStream(OffsetContentSvg));

        //Act
        var bitmap = resource.Rasterize(24, 16);
        var content = OpaqueContent(bitmap);

        //Assert
        content.Coverage.Should().BeGreaterThan(0f);
        content.MinX.Should().BeGreaterThan(bitmap.Width / 2);
        content.MinY.Should().BeGreaterThan(bitmap.Height / 2);
        content.MaxX.Should().BeGreaterThan(bitmap.Width - 3);
        content.MaxY.Should().BeGreaterThan(bitmap.Height - 3);
    }

    /// <summary>
    /// The fraction of non-transparent pixels in a bitmap and the bounding box they occupy, with the
    /// bounding box reported as an empty box when nothing was drawn.
    /// </summary>
    private static (float Coverage, int MinX, int MinY, int MaxX, int MaxY) OpaqueContent(SKBitmap bitmap)
    {
        var count = 0;
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0) { continue; }

                count++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return (count / (float)(bitmap.Width * bitmap.Height), minX, minY, maxX, maxY);
    }

    private static MemoryStream SvgStream(string svg)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(svg), writable: false);
    }
}
