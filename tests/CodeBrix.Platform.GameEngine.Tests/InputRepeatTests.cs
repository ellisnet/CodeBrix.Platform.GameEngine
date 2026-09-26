using System;
using CodeBrix.Platform.GameEngine.Input.Actions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>Tests for <see cref="InputRepeat"/>: the delay and interval it accepts.</summary>
public class InputRepeatTests
{
    [Fact]
    public void the_values_are_kept()
    {
        //Arrange + Act
        var repeat = new InputRepeat(0.35, 0.1);

        //Assert
        repeat.DelaySeconds.Should().Be(0.35);
        repeat.IntervalSeconds.Should().Be(0.1);
    }

    [Theory]
    [InlineData(-0.1, 0.1)]
    [InlineData(double.NaN, 0.1)]
    [InlineData(0.35, 0.0)]
    [InlineData(0.35, double.PositiveInfinity)]
    public void out_of_range_values_are_rejected(double delay, double interval) =>
        ((Func<InputRepeat>)(() => new InputRepeat(delay, interval))).Should().Throw<ArgumentOutOfRangeException>();
}
