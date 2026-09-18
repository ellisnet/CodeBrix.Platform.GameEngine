using System;
using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers loading an SVG resource straight from a stream, the route an asset provider uses for a
/// vector asset that lives inside an archive.
/// </summary>
public class SvgResourceTests
{
    private const string RedRectangleSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="16" viewBox="0 0 24 16">
          <rect x="0" y="0" width="24" height="16" fill="#ff0000" />
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

    private static MemoryStream SvgStream(string svg)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(svg), writable: false);
    }
}
