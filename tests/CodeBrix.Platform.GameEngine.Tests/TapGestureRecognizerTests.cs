using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Input.Touch.Gestures;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="TapGestureRecognizer"/> tick-based timing, end-position distance checks,
/// multi-touch cancellation and swipe arbitration.
/// </summary>
public class TapGestureRecognizerTests
{
    [Fact]
    public void Tapped_is_raised_for_a_short_fast_contact_that_does_not_qualify_as_a_swipe()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var tap = new TapGestureRecognizer(input);
        using var swipe = new SwipeGestureRecognizer(input);
        tap.CompetingSwipeRecognizer = swipe;
        int taps = 0;
        int swipes = 0;
        tap.Tapped += (_, _) => taps++;
        swipe.Swiped += (_, _) => swipes++;
        long start = 100L;
        long end = start + Math.Max(1, HighResTimer.TicksPerSecond / 100);

        //Act
        input.Begin(1, Point.Empty, start);
        input.End(1, new Point(10, 0), end);

        //Assert
        taps.Should().Be(1);
        swipes.Should().Be(0);
    }

    [Fact]
    public void Tapped_uses_the_final_position_even_without_a_movement_event()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var tap = new TapGestureRecognizer(input);
        int taps = 0;
        tap.Tapped += (_, _) => taps++;

        //Act
        input.Begin(1, Point.Empty, 100);
        input.End(1, new Point(100, 0), 101);

        //Assert
        taps.Should().Be(0);
    }

    [Fact]
    public void Tapped_is_not_raised_when_the_contact_lasts_longer_than_the_maximum_tap_duration()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var tap = new TapGestureRecognizer(input);
        int taps = 0;
        tap.Tapped += (_, _) => taps++;
        long start = HighResTimer.TicksPerSecond;

        //Act
        input.Begin(1, Point.Empty, start);
        input.End(1, Point.Empty, start + HighResTimer.TicksPerSecond);

        //Assert
        taps.Should().Be(0);
    }

    [Fact]
    public void Tapped_and_Swiped_candidates_are_cancelled_by_a_second_contact()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var tap = new TapGestureRecognizer(input);
        using var swipe = new SwipeGestureRecognizer(input);
        int gestures = 0;
        tap.Tapped += (_, _) => gestures++;
        swipe.Swiped += (_, _) => gestures++;

        //Act
        input.Begin(1, Point.Empty, 100);
        input.Begin(2, new Point(10, 0), 101);
        input.End(1, new Point(100, 0), 102);
        input.End(2, new Point(110, 0), 103);

        //Assert
        gestures.Should().Be(0);
    }
}
