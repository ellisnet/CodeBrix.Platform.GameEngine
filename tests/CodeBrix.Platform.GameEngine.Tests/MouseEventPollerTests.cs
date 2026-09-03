using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="MouseEventPoller"/> scroll-change detection, throttle-stamp advancement and
/// singleton lifecycle. The poller is a process-global singleton, so every test resets it afterwards.
/// </summary>
public class MouseEventPollerTests : IDisposable
{
    [Fact]
    public void PollForEvents_does_not_emit_a_phantom_zero_scroll_after_a_real_scroll()
    {
        //Arrange
        var adapter = new FakeMouseAdapter();
        MouseEventPoller.Initialize(adapter, new MouseEventConfiguration(trackMouseMovement: false));
        var deltas = new List<int>();
        MouseEventPoller.Instance!.MouseEvent += e => deltas.Add(e.ScrollDelta);

        //Act
        adapter.ScrollDelta = 120;
        MouseEventPoller.Instance.PollForEvents(100);
        adapter.ScrollDelta = 0;
        MouseEventPoller.Instance.PollForEvents(101);

        //Assert
        deltas.Should().Equal(120);
    }

    [Fact]
    public void PollForEvents_emits_again_when_a_persistent_delta_changes()
    {
        //Arrange
        var adapter = new FakeMouseAdapter();
        MouseEventPoller.Initialize(adapter, new MouseEventConfiguration(trackMouseMovement: false));
        var deltas = new List<int>();
        MouseEventPoller.Instance!.MouseEvent += e => deltas.Add(e.ScrollDelta);

        //Act
        adapter.ScrollDelta = 120;
        MouseEventPoller.Instance.PollForEvents(100);
        MouseEventPoller.Instance.PollForEvents(101); // same persistent delta: no second event
        adapter.ScrollDelta = -120;
        MouseEventPoller.Instance.PollForEvents(102);

        //Assert
        deltas.Should().Equal(120, -120);
    }

    [Fact]
    public void PollForEvents_advances_the_throttle_timestamp_after_an_event()
    {
        //Arrange
        var adapter = new FakeMouseAdapter();
        MouseEventPoller.Initialize(
            adapter,
            new MouseEventConfiguration(trackMouseMovement: true, secondsBetweenEvents: 1));
        int events = 0;
        MouseEventPoller.Instance!.MouseEvent += _ => events++;
        long start = HighResTimer.TicksPerSecond;

        //Act
        adapter.CurrentPosition = new Point(1, 0);
        MouseEventPoller.Instance.PollForEvents(start);
        adapter.CurrentPosition = new Point(2, 0);
        MouseEventPoller.Instance.PollForEvents(start + 1);

        //Assert
        events.Should().Be(1);
    }

    [Fact]
    public void PollForEvents_emits_again_once_the_throttle_interval_has_elapsed()
    {
        //Arrange
        var adapter = new FakeMouseAdapter();
        MouseEventPoller.Initialize(
            adapter,
            new MouseEventConfiguration(trackMouseMovement: true, secondsBetweenEvents: 1));
        int events = 0;
        MouseEventPoller.Instance!.MouseEvent += _ => events++;
        long start = HighResTimer.TicksPerSecond;

        //Act
        adapter.CurrentPosition = new Point(1, 0);
        MouseEventPoller.Instance.PollForEvents(start);
        adapter.CurrentPosition = new Point(2, 0);
        MouseEventPoller.Instance.PollForEvents(start + HighResTimer.TicksPerSecond);

        //Assert
        events.Should().Be(2);
    }

    [Fact]
    public void Initialize_disposes_the_previous_instance()
    {
        //Arrange
        var first = new FakeMouseAdapter();
        MouseEventPoller.Initialize(first, new MouseEventConfiguration(trackMouseMovement: true));

        //Act
        var second = new FakeMouseAdapter();
        MouseEventPoller.Initialize(second, new MouseEventConfiguration(trackMouseMovement: true));

        //Assert
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();
        MouseEventPoller.Instance!.Adapter.Should().BeSameAs(second);
    }

    [Fact]
    public void Initialize_throws_when_the_adapter_is_null()
    {
        //Arrange
        IMouseAdapter adapter = null!;

        //Act
        Action act = () => MouseEventPoller.Initialize(adapter);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reset_disposes_the_adapter_and_clears_the_singleton()
    {
        //Arrange
        var adapter = new FakeMouseAdapter();
        MouseEventPoller.Initialize(adapter, new MouseEventConfiguration(trackMouseMovement: true));

        //Act
        MouseEventPoller.Reset();

        //Assert
        adapter.IsDisposed.Should().BeTrue();
        MouseEventPoller.Instance.Should().BeNull();
    }

    /// <summary>Clears the global poller singleton after every test in this class.</summary>
    public void Dispose()
    {
        MouseEventPoller.Reset();
        GC.SuppressFinalize(this);
    }

    private sealed class FakeMouseAdapter : IMouseAdapter, IDisposable
    {
        public Point CurrentPosition { get; set; }

        public HashSet<MouseButton> PressedButtons { get; } = new();

        public KeyboardModifierState CurrentKeyboardModifiers => KeyboardModifierState.None;

        public int ScrollDelta { get; set; }

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
