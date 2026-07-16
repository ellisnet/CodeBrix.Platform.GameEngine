using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Verifies the SkiaSharp-4 replacement for the removed <c>SKFilterQuality</c> enum:
/// <see cref="ImageFilterQuality"/> mapping to <see cref="SKSamplingOptions"/>.
/// </summary>
public class ImageFilterQualityTests
{
    [Fact]
    public void ToSamplingOptions_None_is_nearest_no_mipmap()
    {
        //Arrange
        var sampling = ImageFilterQuality.None.ToSamplingOptions();

        //Assert
        sampling.Filter.Should().Be(SKFilterMode.Nearest);
        sampling.Mipmap.Should().Be(SKMipmapMode.None);
        sampling.UseCubic.Should().BeFalse();
    }

    [Fact]
    public void ToSamplingOptions_Low_is_linear_no_mipmap()
    {
        //Arrange
        var sampling = ImageFilterQuality.Low.ToSamplingOptions();

        //Assert
        sampling.Filter.Should().Be(SKFilterMode.Linear);
        sampling.Mipmap.Should().Be(SKMipmapMode.None);
    }

    [Fact]
    public void ToSamplingOptions_Medium_is_linear_with_mipmap()
    {
        //Arrange
        var sampling = ImageFilterQuality.Medium.ToSamplingOptions();

        //Assert
        sampling.Filter.Should().Be(SKFilterMode.Linear);
        sampling.Mipmap.Should().Be(SKMipmapMode.Linear);
    }

    [Fact]
    public void ToSamplingOptions_High_uses_cubic()
        => ImageFilterQuality.High.ToSamplingOptions().UseCubic.Should().BeTrue();
}
