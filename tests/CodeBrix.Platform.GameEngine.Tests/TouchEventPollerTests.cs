using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Input.Touch;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="TouchEventPoller"/> lifecycle delivery, movement-only throttling, pause
/// behaviour and singleton lifecycle. The poller is a process-global singleton, so every test
/// resets it afterwards.
/// </summary>
public class TouchEventPollerTests : IDisposable
{
    [Fact]
    public void PollForEvents_preserves_a_contact_that_begins_and_ends_between_polls()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration());
        var poller = TouchEventPoller.Instance!;
        var phases = new List<TouchPhase>();
        poller.TouchBegan += (_, e) => phases.Add(e.Touch.Phase);
        poller.TouchEnded += (_, e) => phases.Add(e.Touch.Phase);

        //Act
        adapter.Begin(1, new Point(10, 20));
        adapter.End(1, new Point(10, 20));
        poller.PollForEvents(100);

        //Assert
        phases.Should().Equal(TouchPhase.Began, TouchPhase.Ended);
        poller.ActiveTouches.Should().BeEmpty();
    }

    [Fact]
    public void PollForEvents_throttles_only_movement()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration(secondsBetweenEvents: 1));
        var poller = TouchEventPoller.Instance!;
        int began = 0;
        int moved = 0;
        int ended = 0;
        poller.TouchBegan += (_, _) => began++;
        poller.TouchMoved += (_, _) => moved++;
        poller.TouchEnded += (_, _) => ended++;

        //Act
        adapter.Begin(1, Point.Empty);
        poller.PollForEvents(1);
        adapter.Move(1, new Point(20, 0));
        poller.PollForEvents(2);
        adapter.End(1, new Point(20, 0));
        poller.PollForEvents(3);

        //Assert
        began.Should().Be(1);
        moved.Should().Be(0);
        ended.Should().Be(1);
    }

    [Fact]
    public void PollForEvents_emits_movement_once_the_throttle_interval_has_elapsed()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration(secondsBetweenEvents: 1));
        var poller = TouchEventPoller.Instance!;
        int moved = 0;
        poller.TouchMoved += (_, _) => moved++;
        long start = HighResTimer.TicksPerSecond;

        //Act
        adapter.Begin(1, Point.Empty);
        poller.PollForEvents(start);
        adapter.Move(1, new Point(20, 0));
        poller.PollForEvents(start + HighResTimer.TicksPerSecond);

        //Assert
        moved.Should().Be(1);
    }

    [Fact]
    public void PollForEvents_pause_suppresses_events_and_resume_starts_a_fresh_contact()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        var config = new TouchEventConfiguration(isPaused: true);
        TouchEventPoller.Initialize(adapter, config);
        var poller = TouchEventPoller.Instance!;
        int began = 0;
        poller.TouchBegan += (_, _) => began++;

        //Act
        adapter.Begin(1, Point.Empty);
        poller.PollForEvents(1);
        int whilePaused = began;

        config.IsPaused = false;
        poller.PollForEvents(2);

        //Assert
        whilePaused.Should().Be(0);
        began.Should().Be(1);
    }

    [Fact]
    public void PollForEvents_normalizes_a_discovered_contact_to_the_Began_phase()
    {
        //Arrange
        var adapter = new SnapshotOnlyTouchAdapter(new TouchPoint(7, new Point(5, 6), TouchPhase.Moved));
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration());
        TouchPhase? phase = null;
        TouchEventPoller.Instance!.TouchBegan += (_, e) => phase = e.Touch.Phase;

        //Act
        TouchEventPoller.Instance.PollForEvents(1);

        //Assert
        phase.Should().Be(TouchPhase.Began);
    }

    [Fact]
    public void PollForEvents_arbitrates_overlapping_tap_and_swipe_thresholds_in_favor_of_swipe()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration());
        var poller = TouchEventPoller.Instance!;
        poller.TapRecognizer.MaxTapMovementPixels = 40;
        poller.SwipeRecognizer.MinimumSwipeDistancePixels = 10;
        poller.SwipeRecognizer.MinimumSwipeSpeedPixelsPerSecond = 100;
        int taps = 0;
        int swipes = 0;
        poller.TapRecognizer.Tapped += (_, _) => taps++;
        poller.SwipeRecognizer.Swiped += (_, _) => swipes++;
        long start = HighResTimer.TicksPerSecond;

        //Act
        adapter.Begin(1, Point.Empty);
        poller.PollForEvents(start);
        adapter.End(1, new Point(20, 0));
        poller.PollForEvents(start + (HighResTimer.TicksPerSecond / 10));

        //Assert
        taps.Should().Be(0);
        swipes.Should().Be(1);
    }

    [Fact]
    public void Initialize_disposes_the_previous_instance()
    {
        //Arrange
        var first = new FakeTouchAdapter();
        TouchEventPoller.Initialize(first, new TouchEventConfiguration());

        //Act
        var second = new FakeTouchAdapter();
        TouchEventPoller.Initialize(second, new TouchEventConfiguration());

        //Assert
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();
        TouchEventPoller.Instance!.Adapter.Should().BeSameAs(second);
    }

    [Fact]
    public void Initialize_throws_when_the_adapter_is_null()
    {
        //Arrange
        ITouchAdapter adapter = null!;

        //Act
        Action act = () => TouchEventPoller.Initialize(adapter);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reset_disposes_the_adapter_and_clears_the_singleton()
    {
        //Arrange
        var adapter = new FakeTouchAdapter();
        TouchEventPoller.Initialize(adapter, new TouchEventConfiguration());

        //Act
        TouchEventPoller.Reset();

        //Assert
        adapter.IsDisposed.Should().BeTrue();
        TouchEventPoller.Instance.Should().BeNull();
    }

    /// <summary>Clears the global poller singleton after every test in this class.</summary>
    public void Dispose()
    {
        TouchEventPoller.Reset();
        GC.SuppressFinalize(this);
    }
}
