using System;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Input.Touch;
using CodeBrix.Platform.GameEngine.Timers;

namespace CodeBrix.Platform.GameEngine.Input; //CodeBrix (not from Gondwana)

/// <summary>
/// The public input pump for games that own their game loop (software-rendered /
/// framebuffer-style games) instead of running the engine cycle: call <see cref="PollNow"/>
/// at the top of each tic to run every initialized input poller once. Poller events
/// (<see cref="KeyboardEventPoller.KeyDown"/>, mouse/touch/gamepad events) are raised
/// synchronously on the calling thread.
/// </summary>
/// <remarks>
/// Polled state reads (<see cref="IKeyboardAdapter.IsDown"/>) do NOT require the pump — they
/// are lock-free and valid from any thread at any time. The pump exists to drive the pollers'
/// event transitions (pressed/repeated/released).
/// </remarks>
public static class InputPump
{
    private static readonly object _gate = new();

    /// <summary>
    /// Refreshes gamepad state and runs the keyboard, mouse, touch, and gamepad pollers once,
    /// raising their events synchronously on the calling thread.
    /// </summary>
    /// <remarks>
    /// The gamepad refresh is throttled by
    /// <see cref="Configuration.EngineConfiguration.TimeBetweenGamepadStateUpdates"/> and is the same
    /// call the engine cycle makes, so a game that owns its loop gets identical gamepad behavior to
    /// one that runs the engine cycle - including hotplug. Nothing extra is required of the game.
    /// <para>
    /// Mutually exclusive with the engine loop: while <see cref="Engine.IsRunning"/> is true
    /// the engine cycle already pumps input, and pumping the same pollers from a second
    /// thread would corrupt their per-key state — so this method throws instead. Concurrent
    /// <see cref="PollNow"/> calls serialize on an internal lock, though a single game
    /// thread is the intended caller.
    /// </para>
    /// </remarks>
    public static void PollNow()
    {
        if (Engine.Instance.IsRunning)
        {
            throw new InvalidOperationException(
                $"{nameof(InputPump)}.{nameof(PollNow)}() cannot be used while the engine loop is running — " +
                $"the engine cycle already polls input every cycle, and double-pumping would corrupt poller state.");
        }

        lock (_gate)
        {
            var tick = HighResTimer.GetCurrentTick();

            // Refresh gamepad state before the pollers run, exactly as the engine cycle does.
            // Without this the adapters would never be read at all in this mode: the manager's
            // Update() is what pulls sticks, triggers and buttons from the devices AND performs
            // hotplug detection, so the gamepad event poller below would forever test a state
            // frozen at the moment each adapter was created.
            Engine.Instance.Input.UpdateGamepadState(tick);

            KeyboardEventPoller.Instance?.PollForEvents(tick);
            MouseEventPoller.Instance?.PollForEvents(tick);
            TouchEventPoller.Instance?.PollForEvents(tick);
            GamepadEventPoller.Instance?.PollForEvents(tick);
        }
    }
}
