using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Sdl2.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

/// <summary>
/// An <see cref="IGamepadAdapter"/> backed by a single SDL2 game controller.
/// </summary>
/// <remarks>
/// Instances are created and owned by <see cref="SdlGamepadManager"/>; there is no reason to
/// construct one directly. State is refreshed by the manager once per engine frame, and every
/// property here is a snapshot of the most recent refresh rather than a live query, so reading
/// several properties in one frame is guaranteed to give a consistent view of the controller.
/// </remarks>
public sealed class SdlGamepadAdapter : IGamepadAdapter, IDisposable
{
    private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);

    private SDL_GameController _controller;
    private bool _disposed;

    // The first refresh after opening a controller is discarded. A freshly connected Bluetooth
    // controller has been observed reporting every stick axis at exactly -32768 - full deflection
    // in both directions - until its first HID report arrives. A deadzone cannot filter that,
    // because it is a maximum reading rather than a small one, so a game would take one frame of
    // hard input in a random direction on startup. Skipping one frame costs nothing and removes it.
    private bool _warmUpPending = true;

    internal SdlGamepadAdapter(SDL_GameController controller, int instanceId, string name)
    {
        _controller = controller;
        InstanceId = instanceId;
        Name = name;
        GamepadId = $"sdl:{instanceId}";
    }

    /// <summary>
    /// Gets the unique identifier for the gamepad device.
    /// </summary>
    /// <remarks>
    /// Derived from the SDL2 joystick instance ID, which is not reused while a device stays
    /// connected. A controller that disconnects and reconnects gets a NEW identifier, so anything
    /// registered against the old one - button monitoring in particular - has to be registered
    /// again for the new adapter.
    /// </remarks>
    public string GamepadId { get; }

    /// <summary>
    /// Gets the SDL2 joystick instance ID backing this adapter.
    /// </summary>
    public int InstanceId { get; }

    /// <summary>
    /// Gets the human-readable device name reported by SDL2, for example "Xbox One S Controller".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a read-only collection of button identifiers that are currently pressed on the gamepad.
    /// </summary>
    /// <remarks>
    /// The values come from <see cref="SdlGamepadButtons"/>. This is a live view of the adapter's
    /// internal set: it is refreshed in place each frame rather than reallocated, so it should be
    /// read within the frame and not cached.
    /// </remarks>
    public IReadOnlyCollection<string> PressedButtons => _pressedButtons;

    /// <summary>
    /// Gets the current state of the left analog stick.
    /// </summary>
    public GamepadStickState? LeftStick { get; private set; }

    /// <summary>
    /// Gets the current state of the right analog stick.
    /// </summary>
    public GamepadStickState? RightStick { get; private set; }

    /// <summary>
    /// Gets the current pressure of the left trigger, from 0.0 (released) to 1.0 (fully pressed).
    /// </summary>
    public float LeftTrigger { get; private set; }

    /// <summary>
    /// Gets the current pressure of the right trigger, from 0.0 (released) to 1.0 (fully pressed).
    /// </summary>
    public float RightTrigger { get; private set; }

    /// <summary>
    /// Gets a value indicating whether SDL2 still reports this controller as connected.
    /// </summary>
    public bool IsConnected => !_disposed
        && _controller.NativePointer != IntPtr.Zero
        && Sdl2Native.SDL_GameControllerGetAttached(_controller) != 0;

    /// <summary>
    /// Gets the SDL2 mapping string in use for this controller, for diagnostics.
    /// </summary>
    /// <returns>The mapping string, or an empty string if it is unavailable.</returns>
    /// <remarks>
    /// The mapping is what reconciles a device's raw button and axis numbering with the standard
    /// layout. It differs more than one might expect between transports - the same Xbox pad reports
    /// a different raw layout over Bluetooth than over USB - so this is the first thing worth
    /// logging when a controller behaves oddly.
    /// </remarks>
    public unsafe string GetMappingString()
    {
        if (_disposed || _controller.NativePointer == IntPtr.Zero) { return string.Empty; }

        byte* mapping = Sdl2Native.SDL_GameControllerMapping(_controller);
        if (mapping is null) { return string.Empty; }

        try
        {
            return Marshal.PtrToStringUTF8((IntPtr)mapping) ?? string.Empty;
        }
        finally
        {
            // SDL2 allocates this string for the caller to release.
            Sdl2Native.SDL_free(mapping);
        }
    }

    /// <summary>
    /// Re-reads button and axis state from SDL2.
    /// </summary>
    /// <remarks>
    /// Called by <see cref="SdlGamepadManager.Update"/> once per frame, after a single
    /// <c>SDL_GameControllerUpdate</c> covering every open controller.
    /// </remarks>
    internal void Refresh()
    {
        if (_disposed || _controller.NativePointer == IntPtr.Zero) { return; }

        _pressedButtons.Clear();

        for (int i = 0; i < SdlGamepadButtons.All.Count; i++)
        {
            if (Sdl2Native.SDL_GameControllerGetButton(_controller, (SDL_GameControllerButton)i) != 0)
            {
                _pressedButtons.Add(SdlGamepadButtons.All[i]);
            }
        }

        if (_warmUpPending)
        {
            // Discard this frame's axes; see the field comment. Buttons above are trustworthy
            // immediately, so only the sticks and triggers are held at neutral.
            _warmUpPending = false;
            LeftStick = new GamepadStickState(0f, 0f);
            RightStick = new GamepadStickState(0f, 0f);
            LeftTrigger = 0f;
            RightTrigger = 0f;
            return;
        }

        LeftStick = ReadStick(SDL_GameControllerAxis.LeftX, SDL_GameControllerAxis.LeftY);
        RightStick = ReadStick(SDL_GameControllerAxis.RightX, SDL_GameControllerAxis.RightY);
        LeftTrigger = ReadTrigger(SDL_GameControllerAxis.TriggerLeft);
        RightTrigger = ReadTrigger(SDL_GameControllerAxis.TriggerRight);
    }

    private GamepadStickState ReadStick(SDL_GameControllerAxis xAxis, SDL_GameControllerAxis yAxis)
        => SdlAxisConversion.ToStickState(
            Sdl2Native.SDL_GameControllerGetAxis(_controller, xAxis),
            Sdl2Native.SDL_GameControllerGetAxis(_controller, yAxis));

    private float ReadTrigger(SDL_GameControllerAxis axis)
        => SdlAxisConversion.ToTriggerValue(Sdl2Native.SDL_GameControllerGetAxis(_controller, axis));

    /// <summary>
    /// Closes the underlying SDL2 controller handle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _pressedButtons.Clear();
        LeftStick = null;
        RightStick = null;
        LeftTrigger = 0f;
        RightTrigger = 0f;

        if (_controller.NativePointer != IntPtr.Zero)
        {
            Sdl2Native.SDL_GameControllerClose(_controller);
            _controller = new SDL_GameController(IntPtr.Zero);
        }
    }
}
