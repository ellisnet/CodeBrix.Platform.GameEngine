using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Sdl2.Native;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

/// <summary>
/// An <see cref="IGamepadManager{T}"/> that discovers and polls game controllers through SDL2.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves every CodeBrix.Platform head. SDL2 is initialized with the game controller
/// subsystem only, which starts no video subsystem: no window is created, no X11 or Wayland display
/// is opened, and neither Win32 nor AppKit is touched. SDL2 acts purely as a headless joystick
/// backend over evdev on Linux, XInput and RawInput on Windows, and IOKit on macOS. That is why the
/// Frame Buffer head gets controller support on the same terms as the desktop heads, and why there
/// is no contention with CodeBrix.Platform over the display connection.
/// </para>
/// <para>
/// <b>Starting this never throws.</b> A machine without SDL2 yields an instance whose
/// <see cref="IsAvailable"/> is <see langword="false"/> and whose <see cref="UnavailableReason"/>
/// explains the situation in words a player can act on. A game that is playable with keyboard and
/// mouse keeps working.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="Update"/> is expected to be called from the engine loop, and the
/// adapter collection is mutated only there. Treat the instance as belonging to that thread.
/// </para>
/// </remarks>
public sealed class SdlGamepadManager : IGamepadManager<SdlGamepadAdapter>, IDisposable
{
    private readonly List<SdlGamepadAdapter> _adapters = [];
    private readonly ReadOnlyCollection<SdlGamepadAdapter> _adaptersView;
    private readonly bool _ownsSdlInitialization;

    private bool _disposed;

    private SdlGamepadManager(
        bool isAvailable,
        SdlGamepadUnavailableCause cause,
        string? unavailableReason,
        bool ownsSdlInitialization)
    {
        _adaptersView = new ReadOnlyCollection<SdlGamepadAdapter>(_adapters);
        _ownsSdlInitialization = ownsSdlInitialization;
        IsAvailable = isAvailable;
        UnavailableCause = cause;
        UnavailableReason = unavailableReason;
    }

    /// <summary>
    /// Gets a value indicating whether SDL2 loaded and its game controller subsystem started.
    /// </summary>
    /// <remarks>
    /// This says that gamepad support is <i>functional</i>, not that a controller is plugged in.
    /// Check <see cref="ConnectedAdapters"/> for that.
    /// </remarks>
    public bool IsAvailable { get; }

    /// <summary>
    /// Gets the reason gamepad support is unavailable, phrased for display to a player, or
    /// <see langword="null"/> when <see cref="IsAvailable"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The engine logs this once at startup, and a game can show the same text when the player
    /// turns gamepad support on in a menu and nothing happens. The wording is platform-specific
    /// because the appropriate response is: on Linux the player can install SDL2, whereas on
    /// Windows and macOS SDL2 ships inside the application, so its absence is a bug to report
    /// rather than anything the player can act on.
    /// </remarks>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Gets the machine-readable reason gamepad support is unavailable.
    /// </summary>
    public SdlGamepadUnavailableCause UnavailableCause { get; }

    /// <summary>
    /// Gets the currently connected gamepad adapters.
    /// </summary>
    /// <remarks>
    /// This is a live view. The same collection instance is returned every time and its contents
    /// change as controllers are connected and disconnected, which is what allows the engine's
    /// gamepad event poller - which captures this collection once, when the manager is assigned -
    /// to see controllers that appear later.
    /// </remarks>
    public IReadOnlyCollection<SdlGamepadAdapter> ConnectedAdapters => _adaptersView;

    /// <summary>
    /// Starts SDL2 gamepad support.
    /// </summary>
    /// <param name="manager">
    /// Always receives a usable manager instance, even on failure. When this method returns
    /// <see langword="false"/> the instance reports <see cref="IsAvailable"/> as
    /// <see langword="false"/> and carries the explanation in <see cref="UnavailableReason"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if SDL2 gamepad support is available; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// A manager is handed back even in the failure case on purpose. It can still be assigned to
    /// the engine, where it simply reports no controllers, and it remains the single place to ask
    /// why gamepad support is missing - so the startup log and a settings screen can consult one
    /// object instead of each working out the answer for themselves.
    /// </remarks>
    public static bool TryStart(out SdlGamepadManager manager)
    {
        if (!Sdl2Library.IsLoaded)
        {
            manager = new SdlGamepadManager(
                isAvailable: false,
                SdlGamepadUnavailableCause.NativeLibraryMissing,
                BuildMissingLibraryReason(),
                ownsSdlInitialization: false);
            return false;
        }

        if (Sdl2Native.SDL_Init(SDLInitFlags.GameController) != 0)
        {
            string detail = Sdl2Native.GetErrorString();
            manager = new SdlGamepadManager(
                isAvailable: false,
                SdlGamepadUnavailableCause.SubsystemInitializationFailed,
                "SDL2 was found, but its game controller subsystem could not be started, so gamepad "
                    + "support is unavailable."
                    + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" SDL2 reported: {detail}"),
                ownsSdlInitialization: false);
            return false;
        }

        // Controller events are ignored because this manager polls state directly. Leaving them
        // enabled would have SDL2 queue an event for every button and axis change into a queue
        // nothing ever drains - the engine runs the CodeBrix.Platform event loop, not SDL2's.
        // Polled reads are unaffected: SDL_GameControllerUpdate refreshes the internal state that
        // SDL_GameControllerGetButton and SDL_GameControllerGetAxis read, regardless of this setting.
        const int sdlIgnore = 0;
        Sdl2Native.SDL_GameControllerEventState(sdlIgnore);

        manager = new SdlGamepadManager(
            isAvailable: true,
            SdlGamepadUnavailableCause.None,
            unavailableReason: null,
            ownsSdlInitialization: true);

        manager.Update();
        return true;
    }

    /// <summary>
    /// Refreshes the connected controller list and the state of every connected controller.
    /// </summary>
    /// <remarks>
    /// Called by the engine once per frame. Disconnected controllers are dropped and newly attached
    /// ones picked up, so a controller that goes to sleep and wakes again - which Bluetooth
    /// controllers do readily - is handled without any action from the game beyond re-registering
    /// button monitoring against the new <see cref="SdlGamepadAdapter.GamepadId"/>.
    /// </remarks>
    public void Update()
    {
        if (_disposed || !IsAvailable) { return; }

        // Refreshes controller state and, on every platform, the attached-device list. On macOS
        // this call is also what pumps the IOKit run loop that reports hotplug, so it has to happen
        // even while no controller is connected.
        Sdl2Native.SDL_GameControllerUpdate();

        RemoveDisconnectedAdapters();
        AddNewlyConnectedAdapters();

        for (int i = 0; i < _adapters.Count; i++)
        {
            _adapters[i].Refresh();
        }
    }

    /// <summary>
    /// Returns advice about why no controllers are being detected, or <see langword="null"/> when
    /// at least one is connected or when gamepad support is unavailable outright.
    /// </summary>
    /// <returns>A player-facing hint, or <see langword="null"/> if there is nothing useful to add.</returns>
    /// <remarks>
    /// Distinct from <see cref="UnavailableReason"/>: here SDL2 is working fine and simply sees no
    /// devices. The usual explanation is that the controller is off or asleep, but on a system
    /// running without a desktop login session - a kiosk or Frame Buffer deployment, say - the
    /// account may also lack read access to the input devices, which produces exactly the same
    /// silence. This checks for that case specifically so it does not have to be guessed at.
    /// </remarks>
    public string? GetNoControllersHint()
    {
        if (!IsAvailable || _adapters.Count > 0) { return null; }

        const string generalHint =
            "No game controller detected. Make sure it is switched on and paired - wireless "
            + "controllers power down on their own after a period of inactivity.";

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return generalHint; }

        return HasUnreadableJoystickDevices()
            ? generalHint
                + " A joystick device IS attached to this system but this account cannot read it, "
                + "which prevents it from being detected; adding the account to the 'input' group "
                + "and signing in again resolves that."
            : generalHint;
    }

    /// <summary>
    /// Extracts the evdev node names of devices the kernel reports as joysticks.
    /// </summary>
    /// <param name="procBusInputDevices">The contents of /proc/bus/input/devices.</param>
    /// <returns>Node names such as "event19", empty if none are joysticks.</returns>
    /// <remarks>
    /// Each device is described by a block of lines, one of which lists the handlers bound to it,
    /// for example: <c>H: Handlers=kbd event19 js0</c>. A joystick is identified by a <c>js</c>
    /// handler, and the <c>event</c> handler in the same block names the node SDL2 actually reads.
    /// </remarks>
    internal static IReadOnlyList<string> ParseJoystickEventNodes(string procBusInputDevices)
    {
        var nodes = new List<string>();

        foreach (string rawLine in procBusInputDevices.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("H: Handlers=", StringComparison.Ordinal)) { continue; }

            string[] handlers = line["H: Handlers=".Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            bool isJoystick = false;
            string? eventNode = null;

            foreach (string handler in handlers)
            {
                // "js0", "js1"... but NOT something merely starting with those letters.
                if (handler.Length > 2 && handler.StartsWith("js", StringComparison.Ordinal)
                    && char.IsAsciiDigit(handler[2]))
                {
                    isJoystick = true;
                }
                else if (handler.StartsWith("event", StringComparison.Ordinal))
                {
                    eventNode = handler;
                }
            }

            if (isJoystick && eventNode is not null) { nodes.Add(eventNode); }
        }

        return nodes;
    }

    private static bool HasUnreadableJoystickDevices()
    {
        try
        {
            // Only a JOYSTICK device that cannot be read indicates a permissions problem worth
            // reporting. An earlier version of this check asked whether ANY /dev/input/event* node
            // was readable, which is wrong on an ordinary desktop: those nodes are mode 660 and
            // owned by group 'input', and a login session is granted access to individual devices
            // through a per-device ACL rather than through group membership. With no controller
            // attached, typically NOTHING under /dev/input is readable - which is the normal,
            // healthy state, and is exactly the state this method is called in. Reporting that as a
            // permissions fault sent the user chasing a problem they did not have.
            IReadOnlyList<string> joystickNodes = ParseJoystickEventNodes(
                File.ReadAllText("/proc/bus/input/devices"));

            if (joystickNodes.Count == 0)
            {
                // No joystick is attached at all, so this is simply a controller that is off or
                // out of range. Nothing to say about permissions.
                return false;
            }

            foreach (string node in joystickNodes)
            {
                try
                {
                    // Opening for read is the only reliable test, since access may come from an ACL
                    // that the file mode bits do not reflect.
                    using FileStream stream = File.OpenRead($"/dev/input/{node}");
                    return false;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            return true;
        }
        catch (Exception)
        {
            // Diagnostics must never be the thing that breaks. If the input devices cannot be
            // inspected, simply decline to offer the extra advice.
            return false;
        }
    }

    private void RemoveDisconnectedAdapters()
    {
        for (int i = _adapters.Count - 1; i >= 0; i--)
        {
            if (!_adapters[i].IsConnected)
            {
                _adapters[i].Dispose();
                _adapters.RemoveAt(i);
            }
        }
    }

    private void AddNewlyConnectedAdapters()
    {
        // The full device list is rescanned every frame. With the handful of devices a real machine
        // has, this is a few cheap native calls and is far simpler to reason about than tracking
        // add and remove events - and it self-corrects if a device swap is ever missed.
        int joystickCount = Sdl2Native.SDL_NumJoysticks();

        for (int deviceIndex = 0; deviceIndex < joystickCount; deviceIndex++)
        {
            if (!Sdl2Native.SDL_IsGameController(deviceIndex)) { continue; }

            int instanceId = Sdl2Native.SDL_JoystickGetDeviceInstanceID(deviceIndex);
            if (instanceId < 0 || IsTracked(instanceId)) { continue; }

            SDL_GameController controller = Sdl2Native.SDL_GameControllerOpen(deviceIndex);
            if (controller.NativePointer == IntPtr.Zero) { continue; }

            _adapters.Add(new SdlGamepadAdapter(controller, instanceId, ReadControllerName(controller)));
        }
    }

    private bool IsTracked(int instanceId)
    {
        for (int i = 0; i < _adapters.Count; i++)
        {
            if (_adapters[i].InstanceId == instanceId) { return true; }
        }

        return false;
    }

    private static unsafe string ReadControllerName(SDL_GameController controller)
    {
        byte* name = Sdl2Native.SDL_GameControllerName(controller);
        if (name is null) { return "Unknown controller"; }

        // Not freed: unlike the mapping string, SDL2 owns this buffer and returns a borrowed pointer.
        return Marshal.PtrToStringUTF8((IntPtr)name) ?? "Unknown controller";
    }

    private static string BuildMissingLibraryReason()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Gamepad support is unavailable because the SDL2 library is not installed. "
                + "Install it with:  sudo apt install libsdl2-2.0-0";
        }

        // On Windows and macOS SDL2 is shipped inside the application package, so a missing library
        // is not something the person running the game can install their way out of.
        return "Gamepad support is unavailable because the SDL2 native library is missing from the "
            + "application package. This is an application packaging problem; please report it.";
    }

    /// <summary>
    /// Closes every open controller and shuts down the SDL2 subsystem this manager started.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        for (int i = 0; i < _adapters.Count; i++)
        {
            _adapters[i].Dispose();
        }

        _adapters.Clear();

        if (_ownsSdlInitialization)
        {
            // Safe because this package is the only thing in the process using SDL2 - it is present
            // solely as a gamepad backend, and CodeBrix.Platform does its own windowing and input.
            Sdl2Native.SDL_Quit();
        }
    }
}
