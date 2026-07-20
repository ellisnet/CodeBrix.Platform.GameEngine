using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Input;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers gamepad parity between the two hosting modes: a game that owns its loop and drives
/// <see cref="InputPump.PollNow"/> must get the same gamepad behavior as one that runs the engine
/// cycle. The refresh that reads the devices (and performs hotplug detection) used to happen only
/// inside the engine cycle's render phase, which left gamepad state frozen forever in Mode B - and
/// with it the gamepad event poller, which tests that same frozen state.
/// </summary>
public class InputPumpGamepadTests : IDisposable
{
    private readonly double _originalStateUpdateInterval =
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates;

    /// <summary>Restores the process-global engine state this fixture touches.</summary>
    public void Dispose()
    {
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = _originalStateUpdateInterval;
        Engine.Instance.Input.GamepadManager = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PollNow_refreshes_the_gamepad_manager()
    {
        //Arrange
        var manager = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = manager;

        //Act
        InputPump.PollNow();

        //Assert
        manager.UpdateCount.Should().Be(1);
    }

    [Fact]
    public void PollNow_raises_button_events_from_state_read_in_the_same_poll()
    {
        //Arrange - the pad reports nothing pressed until it is refreshed, so an event can only
        //fire if the refresh ran BEFORE the event poll within this one call.
        var adapter = new FakeGamepadAdapter("test:0");
        var manager = new FakeGamepadManager(adapter);
        manager.OnUpdate = () => adapter.SetPressedButtons("A");

        Engine.Instance.Input.GamepadManager = manager;
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = 0;

        var poller = GamepadEventPoller.Instance;
        poller.Should().NotBeNull();

        var pressed = new List<string>();
        void OnButtonDown(GamepadButtonDownEventArgs args) => pressed.Add(args.Config.Button);

        poller!.ButtonDown += OnButtonDown;
        poller.StartMonitoringButton("test:0", "A", timeBetweenEvents: 0);

        try
        {
            //Act
            InputPump.PollNow();

            //Assert
            pressed.Should().ContainSingle();
            pressed[0].Should().Be("A");
        }
        finally
        {
            poller.ButtonDown -= OnButtonDown;
        }
    }

    [Fact]
    public void PollNow_does_not_throw_when_no_gamepad_manager_is_assigned()
    {
        //Arrange
        Engine.Instance.Input.GamepadManager = null;

        //Act
        var poll = () => InputPump.PollNow();

        //Assert
        poll.Should().NotThrow();
    }

    [Fact]
    public void UpdateGamepadState_skips_refresh_inside_the_throttle_interval()
    {
        //Arrange
        var manager = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = manager;
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = 0.05;

        var tick = HighResTimer.GetCurrentTick();

        //Act - the second call lands 10 ms later, well inside the 50 ms interval
        Engine.Instance.Input.UpdateGamepadState(tick);
        Engine.Instance.Input.UpdateGamepadState(tick + TicksForSeconds(0.01));

        //Assert
        manager.UpdateCount.Should().Be(1);
    }

    [Fact]
    public void UpdateGamepadState_refreshes_again_after_the_throttle_interval()
    {
        //Arrange
        var manager = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = manager;
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = 0.05;

        var tick = HighResTimer.GetCurrentTick();

        //Act
        Engine.Instance.Input.UpdateGamepadState(tick);
        Engine.Instance.Input.UpdateGamepadState(tick + TicksForSeconds(0.06));

        //Assert
        manager.UpdateCount.Should().Be(2);
    }

    [Fact]
    public void UpdateGamepadState_refreshes_on_every_call_when_the_interval_is_zero()
    {
        //Arrange
        var manager = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = manager;
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = 0;

        var tick = HighResTimer.GetCurrentTick();

        //Act
        Engine.Instance.Input.UpdateGamepadState(tick);
        Engine.Instance.Input.UpdateGamepadState(tick);
        Engine.Instance.Input.UpdateGamepadState(tick);

        //Assert
        manager.UpdateCount.Should().Be(3);
    }

    [Fact]
    public void Assigning_a_manager_refreshes_it_on_the_next_update_despite_the_throttle()
    {
        //Arrange - a long interval, so only the assignment's throttle reset can let the second
        //manager refresh this soon after the first one did.
        Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates = 30;

        var first = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = first;

        var tick = HighResTimer.GetCurrentTick();
        Engine.Instance.Input.UpdateGamepadState(tick);

        //Act
        var second = new FakeGamepadManager();
        Engine.Instance.Input.GamepadManager = second;
        Engine.Instance.Input.UpdateGamepadState(tick + TicksForSeconds(0.001));

        //Assert
        first.UpdateCount.Should().Be(1);
        second.UpdateCount.Should().Be(1);
    }

    private static long TicksForSeconds(double seconds)
        => (long)(seconds * HighResTimer.TicksPerSecond);

    /// <summary>A gamepad manager that records how many times the engine refreshed it.</summary>
    private sealed class FakeGamepadManager : IGamepadManager<IGamepadAdapter>
    {
        private readonly List<IGamepadAdapter> _adapters;

        public FakeGamepadManager(params IGamepadAdapter[] adapters)
            => _adapters = new List<IGamepadAdapter>(adapters);

        public int UpdateCount { get; private set; }

        public Action? OnUpdate { get; set; }

        public IReadOnlyCollection<IGamepadAdapter> ConnectedAdapters => _adapters;

        public void Update()
        {
            UpdateCount++;
            OnUpdate?.Invoke();
        }
    }

    /// <summary>A gamepad adapter whose pressed-button set the test controls.</summary>
    private sealed class FakeGamepadAdapter : IGamepadAdapter
    {
        private string[] _pressedButtons = Array.Empty<string>();

        public FakeGamepadAdapter(string gamepadId) => GamepadId = gamepadId;

        public string GamepadId { get; }

        public IReadOnlyCollection<string> PressedButtons => _pressedButtons;

        public GamepadStickState? LeftStick => null;

        public GamepadStickState? RightStick => null;

        public float LeftTrigger => 0f;

        public float RightTrigger => 0f;

        public void SetPressedButtons(params string[] buttons) => _pressedButtons = buttons;
    }
}
