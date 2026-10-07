using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class CardLayoutsTests
{
    [Theory]
    [InlineData(CardLayout.Stack)]
    [InlineData(CardLayout.Row)]
    [InlineData(CardLayout.Fan)]
    [InlineData(CardLayout.Grid)]
    [InlineData(CardLayout.Circle)]
    public void Layouts_have_finite_poses_for_empty_single_and_many_cards(CardLayout layout)
    {
        foreach (int count in new[] { 0, 1, 80 })
        {
            var poses = CardLayouts.Arrange(layout, count, 0, 0, 800, 500);
            poses.Count.Should().Be(count);
            foreach (var p in poses) (float.IsFinite(p.Center.X) && float.IsFinite(p.Center.Y)).Should().BeTrue();
        }
    }
}
