using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers how a <see cref="Cycle"/> reads per-frame durations: the current frame's duration falls
/// back to <see cref="Cycle.ThrottleTime"/>, <see cref="Cycle.TotalCycleTime"/> is unchanged for a
/// cycle without durations and sums the frames' own times when it has them, and a cloned cycle
/// carries the durations.
/// </summary>
public class CycleTests : IDisposable
{
    /// <summary>Clears the cycle registry the fixture's cycles joined.</summary>
    public void Dispose()
    {
        Cycle.ClearAllAnimationCycles();
        GC.SuppressFinalize(this);
    }

    private static FrameSequence CreateSequence(int frameCount, CycleType cycleType)
    {
        var frames = new List<Frame>();
        for (var i = 0; i < frameCount; i++)
            frames.Add(default);

        return new FrameSequence(frames) { SequenceCycleType = cycleType };
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-9;

    [Theory]
    [InlineData(CycleType.Simple, 0.5)]
    [InlineData(CycleType.Repeating, 0.75)]
    [InlineData(CycleType.PingPong, 1.0)]
    public void TotalCycleTime_without_durations_is_unchanged(CycleType cycleType, double expected)
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(3, cycleType), 0.25, "no_durations");

        //Assert
        Near(cycle.TotalCycleTime, expected).Should().BeTrue();
    }

    [Theory]
    [InlineData(CycleType.Simple)]
    [InlineData(CycleType.Repeating)]
    [InlineData(CycleType.PingPong)]
    public void TotalCycleTime_with_every_duration_equal_to_the_throttle_matches_the_uniform_total(CycleType cycleType)
    {
        //Arrange
        var uniform = new Cycle(CreateSequence(4, cycleType), 0.25, "uniform");
        var timed = new Cycle(CreateSequence(4, cycleType), 0.25, "timed");
        for (var i = 0; i < 4; i++)
            timed.Sequence.SetDurationSeconds(i, 0.25);

        //Assert
        Near(timed.TotalCycleTime, uniform.TotalCycleTime).Should().BeTrue();
    }

    [Theory]
    [InlineData(CycleType.Simple, 0.1 + 0.25)]
    [InlineData(CycleType.Repeating, 0.1 + 0.25 + 0.5)]
    [InlineData(CycleType.PingPong, 0.1 + 0.25 + 0.25 + 0.5)]
    public void TotalCycleTime_with_durations_sums_each_frame_time(CycleType cycleType, double expected)
    {
        //Arrange - frame 0 timed, frame 1 on the 0.25 default, frame 2 timed.
        var cycle = new Cycle(CreateSequence(3, cycleType), 0.25, "mixed");
        cycle.Sequence.SetDurationSeconds(0, 0.1);
        cycle.Sequence.SetDurationSeconds(2, 0.5);

        //Assert
        Near(cycle.TotalCycleTime, expected).Should().BeTrue();
    }

    [Fact]
    public void CurrentFrameDurationSeconds_falls_back_to_the_throttle_time()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "fallback");
        cycle.Sequence.SetDurationSeconds(1, 0.6);

        //Assert
        cycle.CurrentFrameDurationSeconds.Should().Be(0.25);
    }

    [Fact]
    public void CurrentFrameDurationSeconds_follows_the_current_frame()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "follows");
        cycle.Sequence.SetDurationSeconds(1, 0.6);

        //Act
        cycle.Sequence.AdvanceFrame();

        //Assert
        cycle.CurrentFrameDurationSeconds.Should().Be(0.6);
    }

    [Fact]
    public void CurrentFrameThrottle_without_durations_is_the_cycle_throttle()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "plain");

        //Assert
        cycle.CurrentFrameThrottle.Should().Be(cycle._throttle);
    }

    [Fact]
    public void CurrentFrameThrottle_holds_an_enormous_duration_instead_of_overflowing()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "enormous");
        cycle.Sequence.SetDurationSeconds(0, double.MaxValue);

        //Assert
        cycle.CurrentFrameThrottle.Should().BeGreaterThan(0);
        (cycle.CurrentFrameThrottle <= long.MaxValue / 4).Should().BeTrue();
    }

    [Fact]
    public void a_cloned_cycle_carries_the_frame_durations()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "cloned");
        cycle.Sequence.SetDurationSeconds(1, 0.6);

        //Act
        var clone = (Cycle)cycle.Clone();

        //Assert
        clone.Sequence.GetDurationSeconds(1).Should().Be(0.6);
    }

    [Fact]
    public void retiming_a_cloned_cycle_leaves_the_registered_cycle_alone()
    {
        //Arrange
        var cycle = new Cycle(CreateSequence(2, CycleType.Repeating), 0.25, "registered");
        cycle.Sequence.SetDurationSeconds(1, 0.6);
        var clone = Cycle.GetAnimationCycle("registered");

        //Act
        clone.Sequence.SetDurationSeconds(1, 0.1);

        //Assert
        cycle.Sequence.GetDurationSeconds(1).Should().Be(0.6);
    }
}
