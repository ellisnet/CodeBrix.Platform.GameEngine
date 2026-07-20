using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Input.Touch;
using CodeBrix.Platform.GameEngine.Timers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine; //was previously: Gondwana;
/// <summary>
/// Provides centralized access to all the input systems of the engine, including gamepad, keyboard, mouse, and touch input.
/// </summary>
public sealed class EngineInputSystems
{
    internal EngineInputSystems() { }

    private IGamepadManager<IGamepadAdapter>? _gamepadManager = null;

    private long _lastGamepadStateUpdateTick = 0;

    /// <summary>
    /// Gets or sets the gamepad manager responsible for handling gamepad input.
    /// </summary>
    /// <remarks>
    /// Once assigned, the manager's state is refreshed by whichever loop is driving the game - the
    /// engine cycle in Mode A, or <see cref="CodeBrix.Platform.GameEngine.Input.InputPump.PollNow"/>
    /// in Mode B - immediately before the gamepad event poller runs, so events are always raised
    /// against freshly-read device state. The refresh rate is bounded by
    /// <see cref="Configuration.EngineConfiguration.TimeBetweenGamepadStateUpdates"/>. Games do not
    /// call <see cref="IGamepadManager{T}.Update"/> themselves.
    /// </remarks>
    public IGamepadManager<IGamepadAdapter>? GamepadManager
    {
        get => _gamepadManager;
        set
        {
            GamepadEventPoller.Initialize(value?.ConnectedAdapters);
            _gamepadManager = value;

            // A newly assigned manager has never been read; let the next poll refresh it
            // regardless of when the previous manager was last refreshed.
            _lastGamepadStateUpdateTick = 0;
        }
    }

    /// <summary>
    /// Refreshes the state of the connected gamepads, subject to the
    /// <see cref="Configuration.EngineConfiguration.TimeBetweenGamepadStateUpdates"/> throttle.
    /// </summary>
    /// <param name="tick">The current high-resolution tick, as the calling loop measured it.</param>
    /// <remarks>
    /// Called by the engine cycle and by <see cref="CodeBrix.Platform.GameEngine.Input.InputPump.PollNow"/>,
    /// in both cases immediately before the gamepad event poller runs. Keeping the call in one place is
    /// what makes the two hosting modes behave identically; it is internal because a game driving it
    /// directly would defeat the throttle that <see cref="IGamepadManager{T}.Update"/> requires.
    /// </remarks>
    internal void UpdateGamepadState(long tick)
    {
        var manager = _gamepadManager;
        if (manager is null)
            return;

        // The first refresh after a manager is assigned always runs; after that the configured
        // interval applies. A non-positive interval means "refresh on every poll".
        var minimumInterval = Engine.Instance.Configuration.TimeBetweenGamepadStateUpdates;
        if (minimumInterval > 0
            && _lastGamepadStateUpdateTick != 0
            && HighResTimer.GetDuration(_lastGamepadStateUpdateTick, tick) < minimumInterval)
        {
            return;
        }

        _lastGamepadStateUpdateTick = tick;
        manager.Update();
    }

    /// <summary>
    /// Gets the gamepad event polling subsystem, if initialized.
    /// </summary>
    /// <value>
    /// The <see cref="CodeBrix.Platform.GameEngine.Input.Gamepad.GamepadEventPoller"/> instance if initialized;
    /// otherwise, <c>null</c>.
    /// </value>
    /// <remarks>
    /// This property provides access to the gamepad input subsystem. The poller is
    /// automatically initialized when a <see cref="GamepadManager"/> is assigned.
    /// </remarks>
    public GamepadEventPoller? GamepadEventPoller => GamepadEventPoller.Instance;

    /// <summary>
    /// Gets the keyboard event polling subsystem, if initialized.
    /// </summary>
    /// <value>
    /// The <see cref="CodeBrix.Platform.GameEngine.Input.Keyboard.KeyboardEventPoller"/> instance if initialized;
    /// otherwise, <c>null</c>.
    /// </value>
    /// <remarks>
    /// This property provides access to the keyboard input subsystem. The poller must be
    /// initialized via <see cref="Initialize"/> with a valid <see cref="IKeyboardAdapter"/>
    /// before use.
    /// </remarks>
    public KeyboardEventPoller? KeyboardEventPoller => KeyboardEventPoller.Instance ?? null;

    /// <summary>
    /// Gets the mouse event polling subsystem, if initialized.
    /// </summary>
    /// <value>
    /// The <see cref="CodeBrix.Platform.GameEngine.Input.Mouse.MouseEventPoller"/> instance if initialized;
    /// otherwise, <c>null</c>.
    /// </value>
    /// <remarks>
    /// This property provides access to the mouse input subsystem. The poller must be
    /// initialized via <see cref="Initialize"/> with a valid <see cref="IMouseAdapter"/>
    /// before use.
    /// </remarks>
    public MouseEventPoller? MouseEventPoller => MouseEventPoller.Instance ?? null;

    /// <summary>
    /// Gets or sets the touch adapter responsible for providing raw touch state to the engine.
    /// </summary>
    /// <remarks>
    /// Setting this property disposes the previous adapter (if it implements
    /// <see cref="IDisposable"/>) and initializes a new <see cref="TouchEventPoller"/> instance
    /// backed by the supplied adapter. Pass <see langword="null"/> to clear the current adapter
    /// without replacing it.
    /// </remarks>
    public ITouchAdapter? TouchAdapter
    {
        get => TouchEventPoller.Instance?.Adapter;
        set
        {
            (TouchEventPoller.Instance?.Adapter as IDisposable)?.Dispose();
            if (value != null)
                TouchEventPoller.Initialize(value);
        }
    }

    /// <summary>
    /// Gets the touch event polling subsystem, if initialized.
    /// </summary>
    /// <value>
    /// The <see cref="CodeBrix.Platform.GameEngine.Input.Touch.TouchEventPoller"/> instance if initialized;
    /// otherwise, <c>null</c>.
    /// </value>
    /// <remarks>
    /// This property provides access to the touch input subsystem, which also implements
    /// <see cref="ITouchInput"/> for gesture recognizer consumption. Initialize it by assigning
    /// a platform adapter to <see cref="TouchAdapter"/>, or by calling
    /// <c>engine.InitializeCodeBrixTouchAdapter(element)</c> from the <c>CodeBrix.Platform.GameEngine.Host</c>
    /// package.
    /// </remarks>
    public TouchEventPoller? TouchEventPoller => TouchEventPoller.Instance;
}
