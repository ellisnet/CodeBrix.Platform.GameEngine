using System.Drawing;
using CodeBrix.Platform.GameEngine.Input.Touch.Gestures;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="SwipeGestureRecognizer"/> minimum-distance gating and tick-based timing.
/// </summary>
public class SwipeGestureRecognizerTests
{
    [Fact]
    public void Swiped_is_raised_for_a_fast_contact_that_travels_far_enough()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var swipe = new SwipeGestureRecognizer(input);
        SwipedEventArgs? swiped = null;
        swipe.Swiped += (_, e) => swiped = e;
        long start = HighResTimer.TicksPerSecond;

        //Act
        input.Begin(1, Point.Empty, start);
        input.End(1, new Point(200, 0), start + (HighResTimer.TicksPerSecond / 10));

        //Assert
        swiped.Should().NotBeNull();
        swiped!.Direction.Should().Be(SwipeDirection.Right);
    }

    [Fact]
    public void MinimumSwipeDistancePixels_suppresses_a_fast_but_very_short_contact()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var swipe = new SwipeGestureRecognizer(input);
        int swipes = 0;
        swipe.Swiped += (_, _) => swipes++;
        long start = HighResTimer.TicksPerSecond;

        //Act (10 px in 10 ms is 1000 px/s, but below the 30 px minimum distance)
        input.Begin(1, Point.Empty, start);
        input.End(1, new Point(10, 0), start + (HighResTimer.TicksPerSecond / 100));

        //Assert
        swipes.Should().Be(0);
    }

    [Fact]
    public void Swiped_is_not_raised_when_the_contact_is_too_slow()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var swipe = new SwipeGestureRecognizer(input);
        int swipes = 0;
        swipe.Swiped += (_, _) => swipes++;
        long start = HighResTimer.TicksPerSecond;

        //Act (50 px over one full second is 50 px/s, under the 200 px/s minimum)
        input.Begin(1, Point.Empty, start);
        input.End(1, new Point(50, 0), start + HighResTimer.TicksPerSecond);

        //Assert
        swipes.Should().Be(0);
    }
}
