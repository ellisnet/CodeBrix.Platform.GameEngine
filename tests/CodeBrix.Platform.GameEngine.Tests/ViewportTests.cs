using System.Drawing;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="Viewport"/> zoom animation: the fixed-duration tween lands exactly on its
/// target, and <see cref="Viewport.VisibleWorldSizePx"/> uses the reciprocal of the zoom factor.
/// </summary>
public class ViewportTests
{
    private const float Tolerance = 0.001f;

    [Fact]
    public void ZoomToOverDuration_snaps_to_the_target_when_the_duration_elapses()
    {
        //Arrange
        var viewport = new Viewport { Zoom = 1f };

        //Act
        viewport.ZoomToOverDuration(targetZoom: 2f, durationSeconds: 0.75f);
        viewport.UpdateZoom(0.50f);

        //Assert
        viewport.IsZoomAnimating.Should().BeTrue();
        viewport.Zoom.Should().NotBe(2f);

        //Act
        viewport.UpdateZoom(0.25f);

        //Assert
        viewport.Zoom.Should().Be(2f);
        viewport.IsZoomAnimating.Should().BeFalse();
    }

    [Fact]
    public void ZoomToOverDuration_snaps_to_the_target_when_a_frame_exceeds_the_remaining_time()
    {
        //Arrange
        var viewport = new Viewport { Zoom = 1f };

        //Act
        viewport.ZoomToOverDuration(targetZoom: 0.5f, durationSeconds: 0.20f);
        viewport.UpdateZoom(0.25f);

        //Assert
        viewport.Zoom.Should().Be(0.5f);
        viewport.IsZoomAnimating.Should().BeFalse();
    }

    [Theory]
    [InlineData(1f, 800f, 600f)]
    [InlineData(2f, 400f, 300f)]
    [InlineData(0.5f, 1600f, 1200f)]
    public void VisibleWorldSizePx_uses_reciprocal_zoom(float zoom, float expectedWidth, float expectedHeight)
    {
        //Arrange
        var viewport = new Viewport
        {
            TargetRectPx = new Rectangle(0, 0, 800, 600),
            Zoom = zoom
        };

        //Act
        SizeF visible = viewport.VisibleWorldSizePx;

        //Assert
        visible.Width.Should().BeApproximately(expectedWidth, Tolerance);
        visible.Height.Should().BeApproximately(expectedHeight, Tolerance);
    }
}
