using System;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class GpuBackbufferTests
{
    [Fact]
    public void ReleaseGpuSurface_reverts_to_a_valid_cpu_surface()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(64, 48);

        //Act
        backbuffer.ReleaseGpuSurface();

        //Assert - the backbuffer is back in its pre-Initialize state: same dimensions, and a
        //  usable CPU raster surface (Canvas throws when no surface exists).
        backbuffer.Width.Should().Be(64);
        backbuffer.Height.Should().Be(48);
        SKCanvas canvas = backbuffer.Canvas;
        canvas.Should().NotBeNull();
        canvas.Clear(SKColors.Black); // drawing must not throw
    }

    [Fact]
    public void ReleaseGpuSurface_is_safe_to_call_repeatedly()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(32, 32);

        //Act
        backbuffer.ReleaseGpuSurface();
        backbuffer.ReleaseGpuSurface();

        //Assert
        backbuffer.Canvas.Should().NotBeNull();
    }

    [Fact]
    public void ReleaseGpuSurface_is_a_no_op_after_dispose()
    {
        //Arrange
        var backbuffer = new GpuBackbuffer(32, 32);
        backbuffer.Dispose();

        //Act
        Action release = () => backbuffer.ReleaseGpuSurface();

        //Assert
        release.Should().NotThrow();
    }
}
