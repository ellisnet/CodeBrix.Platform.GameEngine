using System.Drawing;
using CodeBrix.Platform.GameEngine.Effects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the directional reveal rectangles that the fill and erase effects clip with.
/// </summary>
public class EffectGeometryTests
{
    [Theory]
    [InlineData(EffectDirection.FromLeftToRight, 0f, 0f, 25f, 50f)]
    [InlineData(EffectDirection.FromRightToLeft, 75f, 0f, 25f, 50f)]
    [InlineData(EffectDirection.FromTopToBottom, 0f, 0f, 100f, 12.5f)]
    [InlineData(EffectDirection.FromBottomToTop, 0f, 37.5f, 100f, 12.5f)]
    public void GetRevealRect_uses_the_requested_direction(
        EffectDirection direction,
        float x,
        float y,
        float width,
        float height)
    {
        //Arrange
        var bounds = new RectangleF(0f, 0f, 100f, 50f);

        //Act
        RectangleF actual = EffectGeometry.GetRevealRect(bounds, direction, 0.25f);

        //Assert
        actual.Should().Be(new RectangleF(x, y, width, height));
    }

    [Fact]
    public void GetRevealRect_is_empty_at_zero_progress_and_whole_at_full_progress()
    {
        //Arrange
        var bounds = new RectangleF(10f, 20f, 100f, 50f);

        //Act
        RectangleF none = EffectGeometry.GetRevealRect(bounds, EffectDirection.FromLeftToRight, 0f);
        RectangleF all = EffectGeometry.GetRevealRect(bounds, EffectDirection.FromLeftToRight, 1f);
        RectangleF undirected = EffectGeometry.GetRevealRect(bounds, EffectDirection.None, 0.25f);

        //Assert
        none.Should().Be(RectangleF.Empty);
        all.Should().Be(bounds);
        undirected.Should().Be(bounds);
    }
}
