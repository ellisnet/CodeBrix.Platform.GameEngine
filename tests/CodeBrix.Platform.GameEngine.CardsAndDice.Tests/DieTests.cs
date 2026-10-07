using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class DieTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(100)]
    public void Dice_support_arbitrary_sides_and_seeded_results(int sides)
    {
        var a = new Die(sides);
        var b = new Die(sides);
        var ar = new Random(77);
        var br = new Random(77);
        for (int i = 0; i < 1000; i++)
        {
            int value = a.Roll(ar).Value;
            value.Should().BeInRange(1, sides);
            b.Roll(br).Value.Should().Be(value);
        }
        Action oneSided = () => new Die(1);
        oneSided.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Traditional_labels_are_culture_invariant_like_numbered_dice()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var traditional = Die.Traditional();
            traditional.Faces.Select(f => f.Label).Should().Equal(new Die(6).Faces.Select(f => f.Label));
            traditional.Faces.Select(f => f.Label).Should().Equal("1", "2", "3", "4", "5", "6");
            traditional.Faces.Select(f => f.Artwork).Should().Equal(Enumerable.Range(1, 6).Select(i => $"dice/pips/{i}.svg"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private sealed class FixedRandom(int index) : Random
    {
        public override int Next(int maxValue) => index;
    }

    [Fact]
    public void Percentile_pair_covers_one_through_one_hundred()
    {
        var tens = Die.PercentileTens();
        var units = Die.PercentileUnits();
        Action unrolled = () => Die.PercentileResult(tens, units);
        unrolled.Should().Throw<InvalidOperationException>();
        var values = new HashSet<int>();
        for (int t = 0; t < 10; t++)
        {
            for (int u = 0; u < 10; u++)
            {
                tens.Roll(new FixedRandom(t));
                units.Roll(new FixedRandom(u));
                values.Add(Die.PercentileResult(tens, units));
            }
        }
        values.Order().Should().Equal(Enumerable.Range(1, 100));
    }
}
