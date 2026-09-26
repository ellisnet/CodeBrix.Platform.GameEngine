using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the optional per-frame durations on <see cref="FrameSequence"/>: a sequence starts with
/// none, a duration can be set and cleared per frame, bad values are rejected, removing a frame
/// keeps the other durations on their frames, and a copied sequence keeps its own timing.
/// </summary>
public class FrameSequenceTests
{
    private static FrameSequence CreateSequence(int frameCount)
    {
        var frames = new List<Frame>();
        for (var i = 0; i < frameCount; i++)
            frames.Add(default);

        return new FrameSequence(frames);
    }

    [Fact]
    public void a_new_sequence_has_no_frame_durations()
    {
        //Arrange
        var sequence = CreateSequence(3);

        //Assert
        sequence.HasFrameDurations.Should().BeFalse();
        sequence.GetDurationSeconds(0).Should().BeNull();
        sequence.GetDurationSeconds(2).Should().BeNull();
    }

    [Fact]
    public void SetDurationSeconds_sets_one_frame_and_leaves_the_others_on_the_cycle_default()
    {
        //Arrange
        var sequence = CreateSequence(3);

        //Act
        sequence.SetDurationSeconds(1, 0.5);

        //Assert
        sequence.HasFrameDurations.Should().BeTrue();
        sequence.GetDurationSeconds(0).Should().BeNull();
        sequence.GetDurationSeconds(1).Should().Be(0.5);
        sequence.GetDurationSeconds(2).Should().BeNull();
    }

    [Fact]
    public void clearing_the_last_duration_leaves_a_sequence_without_durations()
    {
        //Arrange
        var sequence = CreateSequence(2);
        sequence.SetDurationSeconds(0, 0.2);

        //Act
        sequence.SetDurationSeconds(0, null);

        //Assert
        sequence.HasFrameDurations.Should().BeFalse();
        sequence.GetDurationSeconds(0).Should().BeNull();
    }

    [Fact]
    public void ClearFrameDurations_removes_every_duration()
    {
        //Arrange
        var sequence = CreateSequence(2);
        sequence.SetDurationSeconds(0, 0.2);
        sequence.SetDurationSeconds(1, 0.3);

        //Act
        sequence.ClearFrameDurations();

        //Assert
        sequence.HasFrameDurations.Should().BeFalse();
        sequence.GetDurationSeconds(1).Should().BeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SetDurationSeconds_rejects_a_value_that_is_not_positive_and_finite(double seconds)
    {
        //Arrange
        var sequence = CreateSequence(2);

        //Act
        var act = () => sequence.SetDurationSeconds(0, seconds);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        sequence.HasFrameDurations.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void SetDurationSeconds_rejects_an_index_outside_the_sequence(int index)
    {
        //Arrange
        var sequence = CreateSequence(2);

        //Act
        var act = () => sequence.SetDurationSeconds(index, 0.1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void GetDurationSeconds_returns_null_for_an_index_outside_the_sequence(int index)
    {
        //Arrange
        var sequence = CreateSequence(2);
        sequence.SetDurationSeconds(1, 0.4);

        //Assert
        sequence.GetDurationSeconds(index).Should().BeNull();
    }

    [Fact]
    public void AddFrame_with_a_duration_times_only_the_added_frame()
    {
        //Arrange
        var sequence = CreateSequence(2);

        //Act
        sequence.AddFrame(default, 0.75);

        //Assert
        sequence.FrameCount.Should().Be(3);
        sequence.GetDurationSeconds(0).Should().BeNull();
        sequence.GetDurationSeconds(1).Should().BeNull();
        sequence.GetDurationSeconds(2).Should().Be(0.75);
    }

    [Fact]
    public void AddFrame_with_a_bad_duration_adds_nothing()
    {
        //Arrange
        var sequence = CreateSequence(2);

        //Act
        var act = () => sequence.AddFrame(default, -1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        sequence.FrameCount.Should().Be(2);
    }

    [Fact]
    public void RemoveFrame_keeps_the_remaining_durations_on_their_frames()
    {
        //Arrange
        var sequence = CreateSequence(3);
        sequence.SetDurationSeconds(0, 0.1);
        sequence.SetDurationSeconds(2, 0.3);

        //Act
        sequence.RemoveFrame(1);

        //Assert
        sequence.FrameCount.Should().Be(2);
        sequence.GetDurationSeconds(0).Should().Be(0.1);
        sequence.GetDurationSeconds(1).Should().Be(0.3);
    }

    [Fact]
    public void RemoveFrame_of_the_only_timed_frame_leaves_a_sequence_without_durations()
    {
        //Arrange
        var sequence = CreateSequence(3);
        sequence.SetDurationSeconds(1, 0.2);

        //Act
        sequence.RemoveFrame(1);

        //Assert
        sequence.HasFrameDurations.Should().BeFalse();
    }

    [Fact]
    public void a_copied_sequence_keeps_its_own_timing()
    {
        //Arrange
        var original = CreateSequence(2);
        original.SetDurationSeconds(0, 0.1);
        var copy = original;

        //Act
        copy.SetDurationSeconds(0, 0.9);
        copy.SetDurationSeconds(1, 0.8);

        //Assert
        original.GetDurationSeconds(0).Should().Be(0.1);
        original.GetDurationSeconds(1).Should().BeNull();
        copy.GetDurationSeconds(0).Should().Be(0.9);
    }
}
