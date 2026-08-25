================================================================================
AGENT-README: CodeBrix.Platform.GameEngine.Sdl2
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.GameEngine.Sdl2 adds GAME CONTROLLER (gamepad) support to
CodeBrix.Platform.GameEngine. Target: .NET 10 or later.

The engine package has NO gamepad backend of its own. It defines the seam —
IGamepadManager<T>, IGamepadAdapter, GamepadStickState and GamepadEventPoller —
and this package fills it with an SDL2-backed implementation. One call attaches
it; from then on the engine refreshes controller state every cycle (Mode A) or
every tic (Mode B) and raises the engine's own gamepad events. A game never
calls Update() itself.

ONE IMPLEMENTATION COVERS ALL SIX CodeBrix.Platform HEADS (Win32-Skia, WPF-Skia,
X11, Wayland, Frame Buffer, macOS). SDL2 is initialized with the game controller
subsystem ONLY, which starts no video subsystem: it never creates a window,
never opens an X11 or Wayland display, and never touches Win32 or AppKit. It is
a headless joystick backend over evdev (Linux), XInput/RawInput (Windows) and
IOKit (macOS), so there is no contention with CodeBrix.Platform for the display
connection and the Frame Buffer head gets controllers on the same terms as the
desktop heads.

NOTHING HERE THROWS when SDL2 or a controller is missing. Gamepad support is an
enhancement to a game that is already playable with keyboard and mouse, so its
absence is REPORTED (inspectable properties plus one log line) rather than
raised.

PROVENANCE: the P/Invoke binding files under the Native folder are a vendored
port of the Veldrid project's Veldrid.SDL2 bindings (MIT, (c) 2017 Eric Mellino
and Veldrid contributors), reduced to the joystick and game controller entry
points and re-rooted at CodeBrix.Platform.GameEngine.Sdl2.Native; do not use the
upstream namespaces. The SDL2 native binaries themselves are zlib-licensed
((c) Sam Lantinga). See THIRD-PARTY-NOTICES.txt, which ships inside the package.

OTHER PACKAGES FROM THE SAME REPOSITORY
---------------------------------------
  CodeBrix.Platform.GameEngine.MitLicenseForever — the game engine itself
  (engine core + CodeBrix.Platform host layer). License: MIT. It is a hard
  dependency of this package; see AGENT-README.txt in the repository root for
  everything about scenes, sprites, rendering, audio, the hosting modes and the
  engine lifecycle. THIS file covers gamepads only.

INSTALLATION
============
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever

The assembly and namespaces are CodeBrix.Platform.GameEngine.Sdl2[.*] (WITHOUT
the license suffix).

License: MIT AND Zlib. The managed binding code is MIT (vendored from Veldrid);
the SDL2 native binaries are zlib. The suffix names the more notice-demanding of
the two.

NuGet dependencies (pulled in automatically, listed by id):
    CodeBrix.Platform.GameEngine.MitLicenseForever   -- the engine + host

That dependency is an ORDINARY PackageReference on a published version, not a
lock-step pairing: this package is versioned and published INDEPENDENTLY of the
engine package and the two do NOT share a version number. That is deliberate —
this package uses only PUBLIC engine API and has no InternalsVisibleTo seam into
the engine core. As a consumer you simply take the latest of each; there is no
"matching versions" rule to observe.

RUNTIME REQUIREMENT — ONE PREREQUISITE, LINUX ONLY
--------------------------------------------------
The package CARRIES SDL2 native binaries for Windows (x64, x86, ARM64) and for
macOS (one universal x86_64 + arm64 binary, packed under both macOS RIDs), laid
out as runtimes/<rid>/native/. Nothing is downloaded at build time and nothing
extra has to be installed on those platforms.

NO LINUX BINARY IS SHIPPED, deliberately. SDL2's Linux gamepad support is a thin
layer over evdev plus udev for hotplug, and a distribution's own build is matched
to that host's udev; a binary built elsewhere against a newer glibc would fail on
older systems. So on Linux the SYSTEM SDL2 is used, and that is this package's
one prerequisite:

    sudo apt install libsdl2-2.0-0

That is the RUNTIME package. The -dev package is NOT needed: it only adds headers
and the unversioned symlink, and the loader deliberately probes the versioned
soname (libSDL2-2.0.so.0) FIRST. When SDL2 is absent, gamepad support reports
itself unavailable with exactly that apt command in the message text, and the
game keeps running.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.GameEngine.Sdl2;           // the ONE entry point:
                                                       // InitializeSdlGamepadManager
    using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;   // SdlGamepadManager,
                                                       // SdlGamepadAdapter,
                                                       // SdlGamepadButtons,
                                                       // SdlGamepadUnavailableCause
    using CodeBrix.Platform.GameEngine.Sdl2.Native;    // raw SDL2 P/Invoke;
                                                       // NOT for app code

From the engine package (this is where the value types a game reads live):

    using CodeBrix.Platform.GameEngine;                // Engine
    using CodeBrix.Platform.GameEngine.Input.Gamepad;  // GamepadStickState,
                                                       // StickDirection,
                                                       // GamepadEventPoller,
                                                       // GamepadButtonDownEventArgs

A game that only wants controllers to work needs the first namespace and, for
the button-name constants and stick helpers, the Gamepad namespaces. The Native
namespace can be ignored entirely.

CORE API REFERENCE
==================

EngineGamepadExtensions (namespace CodeBrix.Platform.GameEngine.Sdl2)
---------------------------------------------------------------------
The entry point for the whole package. Everything else can be ignored by a game
that just wants controllers to work.

    public static class EngineGamepadExtensions
    {
        public static SdlGamepadManager InitializeSdlGamepadManager(
            this Engine engine, bool logStatus = true);
    }

  * Starts SDL2 gamepad support and assigns the resulting manager to
    engine.Input.GamepadManager. Nothing else has to be plumbed.
  * ALWAYS returns a usable manager, including when SDL2 or a controller is
    missing — in that case IsAvailable is false and UnavailableReason explains
    why. Never returns null.
  * Throws ArgumentNullException only if engine is null.
  * logStatus: true (default) writes the outcome to the engine log ONCE — a
    warning carrying UnavailableCause and UnavailableReason when unavailable, an
    information line carrying GetNoControllersHint() when available with nothing
    connected, or one line per connected pad carrying its Name, GamepadId and
    SDL2 mapping string. Pass false to stay silent and inspect the returned
    manager instead.
  * Call it ONCE, after the engine has been started.

SdlGamepadManager (namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad)
-----------------------------------------------------------------------
    public sealed class SdlGamepadManager
        : IGamepadManager<SdlGamepadAdapter>, IDisposable
    {
        public static bool TryStart(out SdlGamepadManager manager);

        public bool IsAvailable { get; }
        public string? UnavailableReason { get; }
        public SdlGamepadUnavailableCause UnavailableCause { get; }
        public IReadOnlyCollection<SdlGamepadAdapter> ConnectedAdapters { get; }

        public void Update();
        public string? GetNoControllersHint();
        public void Dispose();
    }

  * There is NO public constructor. TryStart is the only way to make one, and
    InitializeSdlGamepadManager is the only call most games need — it calls
    TryStart for you and hands back the manager.
  * TryStart returns true when SDL2 loaded AND its game controller subsystem
    started. It ALWAYS assigns a usable manager to the out parameter, even when
    it returns false, so the failure case can still be assigned to the engine
    (it simply reports no controllers) and remains the single place to ask why
    gamepad support is missing.
  * TryStart also disables SDL2's own controller EVENTS and performs one initial
    Update() before returning, so ConnectedAdapters is populated on return.
  * IsAvailable says gamepad support is FUNCTIONAL, not that a controller is
    plugged in. Check ConnectedAdapters.Count for that.
  * UnavailableReason is player-facing text, null when IsAvailable is true. It
    is platform-specific because the right response is: on Linux the player can
    install SDL2, whereas on Windows and macOS SDL2 ships inside the application
    so its absence is a packaging bug to report.
  * ConnectedAdapters is a LIVE VIEW — the same collection instance every time,
    with contents changing as controllers come and go. That is what lets the
    engine's GamepadEventPoller, which captures the collection once when the
    manager is assigned, see controllers that appear later. Do not copy it and
    expect the copy to track hotplug.
  * Update() refreshes the device list and every connected controller's state.
    THE ENGINE CALLS THIS; a game does not. Calling it yourself defeats the
    engine's throttle (EngineConfiguration.TimeBetweenGamepadStateUpdates).
  * GetNoControllersHint() returns advice only when support IS available and
    NOTHING is connected; it returns null when a controller is present and null
    when support is unavailable outright (UnavailableReason covers that case).
  * Dispose() closes every open controller and shuts the SDL2 subsystem down.

SdlGamepadAdapter (namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad)
-----------------------------------------------------------------------
One per connected controller. Created and owned by the manager; there is no
public constructor and no reason to make one.

    public sealed class SdlGamepadAdapter : IGamepadAdapter, IDisposable
    {
        public string GamepadId { get; }                       // "sdl:<instanceId>"
        public int InstanceId { get; }
        public string Name { get; }            // e.g. "Xbox One S Controller"
        public IReadOnlyCollection<string> PressedButtons { get; }
        public GamepadStickState? LeftStick { get; }
        public GamepadStickState? RightStick { get; }
        public float LeftTrigger { get; }                      // 0.0 .. 1.0
        public float RightTrigger { get; }                     // 0.0 .. 1.0
        public bool IsConnected { get; }
        public string GetMappingString();
        public void Dispose();
    }

  * Every property is a SNAPSHOT of the most recent refresh, not a live native
    query, so reading several of them within one frame gives a consistent view
    of the controller.
  * PressedButtons holds values from SdlGamepadButtons and is a live view of the
    adapter's internal set, refreshed IN PLACE each frame rather than
    reallocated. Read it within the frame; never cache the collection.
  * LeftStick / RightStick are null only before the first refresh and after
    Dispose.
  * GetMappingString() returns the SDL2 mapping string for this device, or an
    empty string if unavailable. The mapping reconciles the device's raw button
    and axis numbering with the standard layout and differs by transport — the
    same pad reports a different raw layout over Bluetooth than over USB — so it
    is the FIRST thing to log when a controller behaves oddly. (Passing
    logStatus: true to InitializeSdlGamepadManager already logs it.)
  * Dispose() closes the native controller handle. The manager disposes its
    adapters; a game does not call this.

SdlGamepadButtons (namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad)
-----------------------------------------------------------------------
    public static class SdlGamepadButtons
    {
        public const string A;              // bottom face button
        public const string B;              // right face button
        public const string X;              // left face button
        public const string Y;              // top face button
        public const string Back;           // Back / View / Select
        public const string Guide;          // the illuminated logo button
        public const string Start;          // Start / Menu
        public const string LeftStick;      // left thumbstick pressed inward
        public const string RightStick;     // right thumbstick pressed inward
        public const string LeftShoulder;
        public const string RightShoulder;
        public const string DPadUp;
        public const string DPadDown;
        public const string DPadLeft;
        public const string DPadRight;

        public static IReadOnlyList<string> All { get; }   // all 15, SDL2 order
    }

USE THESE CONSTANTS RATHER THAN STRING LITERALS. The engine's gamepad seam
identifies buttons by string, so a misspelled name registers happily with the
event poller and then simply never fires.

The names follow the SDL2 standard (Xbox-style) layout. On a controller with
different face-button labels the PHYSICAL positions still map through SDL2's
controller database, so A is always the bottom face button whatever is printed
on it.

SdlGamepadUnavailableCause (namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad)
--------------------------------------------------------------------------------
The machine-readable counterpart to UnavailableReason. Branch on this; show the
reason text to the player.

    public enum SdlGamepadUnavailableCause
    {
        None                        = 0,  // started successfully; no failure
        NativeLibraryMissing        = 1,  // SDL2 not found / could not be loaded
        SubsystemInitializationFailed = 2 // SDL2 loaded, subsystem would not start
    }

  * NativeLibraryMissing on LINUX normally means the SDL2 runtime package is not
    installed — something the player can fix. On Windows and macOS SDL2 ships
    inside the application package, so the same value there indicates a
    PACKAGING problem worth reporting as a bug.
  * SubsystemInitializationFailed means SDL2 itself was found. UnavailableReason
    carries SDL2's own error text when it supplied one.

THE ENGINE-SIDE TYPES YOU ACTUALLY READ
---------------------------------------
These come from CodeBrix.Platform.GameEngine.MitLicenseForever, namespace
CodeBrix.Platform.GameEngine.Input.Gamepad. They are listed here because they
are what a game touches every frame once this package is wired up.

    public interface IGamepadAdapter
    {
        string GamepadId { get; }
        IReadOnlyCollection<string> PressedButtons { get; }
        GamepadStickState? LeftStick { get; }
        GamepadStickState? RightStick { get; }
        float LeftTrigger { get; }
        float RightTrigger { get; }
    }

    public interface IGamepadManager<out T> where T : IGamepadAdapter
    {
        IReadOnlyCollection<T> ConnectedAdapters { get; }
        void Update();      // *** the ENGINE calls this, not the game ***
    }

    public readonly struct GamepadStickState
    {
        public float X { get; }        // -1 = full left, +1 = full right
        public float Y { get; }        // -1 = full down, +1 = full UP
        public int RawX { get; }
        public int RawY { get; }

        public GamepadStickState(float x, float y, int rawX = 0, int rawY = 0);
        public static GamepadStickState FromRaw16(int rawX, int rawY);
        public static GamepadStickState FromRawUnsigned16(ushort rawX, ushort rawY);

        public float Magnitude { get; }                              // sqrt(X*X + Y*Y)
        public float Angle { get; }                        // radians, from (1, 0)
        public bool IsEngaged(float threshold = 0.15f);
        public StickDirection Direction(float threshold = 0.15f);
        public GamepadStickState WithDeadzone(float threshold = 0.15f);
        public override string ToString();                           // "(0.00, 0.00)"
    }

    [Flags]
    public enum StickDirection { None = 0, Up = 1, Down = 2, Left = 4, Right = 8 }

  * WithDeadzone(t) returns the state unchanged when IsEngaged(t), and a zeroed
    state (raw values preserved) otherwise. It is the right way to ignore a
    resting stick — see COMMON PITFALLS.
  * Direction(t) returns None below the threshold, and otherwise ORs together
    every axis past it, so a diagonal reads as e.g. Up | Right.
  * FromRaw16 / FromRawUnsigned16 exist for backends; this package's adapter
    already calls FromRaw16 for you.

For event-driven reading:

    public sealed class GamepadEventPoller
    {
        public static GamepadEventPoller? Instance { get; }
        public static void Initialize(IEnumerable<IGamepadAdapter>? adapters);
        public IEnumerable<IGamepadAdapter>? Adapters { get; }
        public bool PauseAllInput { get; set; }
        public event Action<GamepadButtonDownEventArgs>? ButtonDown;

        public void StartMonitoringButton(string gamepadId, string button,
                                          double timeBetweenEvents = -1,
                                          bool isPaused = false);
        public void StopMonitoringButton(string gamepadId, string button);
        public void StopMonitoringAllButtons(string gamepadId);
        public IReadOnlyDictionary<string,
            IReadOnlyDictionary<string, GamepadButtonEventConfiguration>>
                AllButtonConfigsByGamepadId { get; }
    }

    public sealed class GamepadButtonDownEventArgs : EventArgs
    {
        public GamepadButtonEventConfiguration Config { get; }   // .Button is the name
        public IGamepadAdapter Adapter { get; }
    }

Reach it through Engine.Instance.Input.GamepadEventPoller, which is NULLABLE —
use ?. — and note that assigning a manager to Engine.Instance.Input.GamepadManager
(which InitializeSdlGamepadManager does) calls GamepadEventPoller.Initialize for
you. A timeBetweenEvents of -1 means "use
EngineConfiguration.TimeBetweenGamepadEvents", which throttles how often a HELD
button re-raises.

THE PUBLIC NATIVE LAYER — PUBLIC FOR INTEROP; NOT INTENDED FOR APP CODE
------------------------------------------------------------------------
Namespace CodeBrix.Platform.GameEngine.Sdl2.Native. These types are public only
because the vendored binding files and the gamepad layer are in different
namespaces and the bindings had to stay callable in the shape upstream used.
THEY ARE NOT AN APPLICATION-FACING API: they are raw P/Invoke, most of them
unsafe (byte* strings), they return sentinel values instead of throwing when
SDL2 is absent, and calling them behind the manager's back can desynchronise its
device list or shut the subsystem down under it. Use SdlGamepadManager. This
section exists so an agent can recognise these types, not use them.

    public static class Sdl2Library            // finds and opens libSDL2; never throws
        public static bool IsLoaded { get; }         // triggers the one-time load
        public static string? LoadedLibraryName { get; }   // name actually loaded
        public static string? LoadFailureDetail { get; }   // diagnostic only
        public static IReadOnlyList<string> GetProbeCandidates();   // in probe order
        public static T? GetFunction<T>(string name);         // null when unresolved

      Loading is attempted once and the outcome cached, failure included. The
      Linux probe order puts the versioned soname first on purpose.

    public static unsafe partial class Sdl2Native   // the SDL_* bindings
      Only the joystick and game controller subsystems are bound; windowing,
      rendering, audio, keyboard and mouse entry points are deliberately absent.
      Every binding is null-safe: with SDL2 unloaded they return -1 / 0 / null
      rather than throwing.

        public static T? LoadFunction<T>(string name);
        public static byte* SDL_GetError();
        public static byte* SDL_ClearError();
        public static void SDL_free(void* ptr);
        public static string GetErrorString();        // safe managed wrapper
        public static int SDL_Init(SDLInitFlags flags);
        public static void SDL_Quit();
        public static uint SDL_WasInit(SDLInitFlags flags);
        public static int SDL_NumJoysticks();
        public static int SDL_JoystickInstanceID(SDL_Joystick joystick);
        public static int SDL_JoystickGetDeviceInstanceID(int device_index);
        public static SDL_GameController SDL_GameControllerOpen(int joystick_index);
        public static void SDL_GameControllerClose(SDL_GameController gamecontroller);
        public static bool SDL_IsGameController(int joystick_index);
        public static byte* SDL_GameControllerNameForIndex(int joystick_index);
        public static SDL_GameController SDL_GameControllerFromInstanceID(int joyid);
        public static byte* SDL_GameControllerName(SDL_GameController gamecontroller);
        public static byte* SDL_GameControllerMapping(SDL_GameController gamecontroller);
        public static ushort SDL_GameControllerGetVendor(SDL_GameController gamecontroller);
        public static ushort SDL_GameControllerGetProduct(SDL_GameController gamecontroller);
        public static ushort SDL_GameControllerGetProductVersion(SDL_GameController gamecontroller);
        public static int SDL_GameControllerGetAttached(SDL_GameController gamecontroller);
        public static SDL_Joystick SDL_GameControllerGetJoystick(SDL_GameController gamecontroller);
        public static int SDL_GameControllerEventState(int state);
        public static void SDL_GameControllerUpdate();
        public static short SDL_GameControllerGetAxis(SDL_GameController gamecontroller,
                                                      SDL_GameControllerAxis axis);
        public static byte SDL_GameControllerGetButton(SDL_GameController gamecontroller,
                                                       SDL_GameControllerButton button);

    public struct SDL_GameController     // transparent wrapper over a native pointer
        public readonly IntPtr NativePointer;
        public SDL_GameController(IntPtr pointer);
        public static implicit operator IntPtr(SDL_GameController controller);
        public static implicit operator SDL_GameController(IntPtr pointer);

    public struct SDL_Joystick           // same shape, for SDL_Joystick*
        public readonly IntPtr NativePointer;
        public SDL_Joystick(IntPtr pointer);
        public static implicit operator IntPtr(SDL_Joystick joystick);
        public static implicit operator SDL_Joystick(IntPtr pointer);

    [Flags] public enum SDLInitFlags : uint
        None = 0, Timer = 0x1, Audio = 0x10, Video = 0x20, Joystick = 0x200,
        Haptic = 0x1000, GameController = 0x2000
      Only GameController is used by this package; it implies Joystick and pulls
      in the event subsystem, and initializes NO video subsystem.

    public enum SDL_GameControllerAxis : byte
        Invalid, LeftX, LeftY, RightX, RightY, TriggerLeft, TriggerRight, Max
      Y axes are NEGATIVE when the stick is pushed up — the opposite of
      GamepadStickState. The adapter inverts them.

    public enum SDL_GameControllerButton : byte
        Invalid, A, B, X, Y, Back, Guide, Start, LeftStick, RightStick,
        LeftShoulder, RightShoulder, DPadUp, DPadDown, DPadLeft, DPadRight, Max

COMPLETE EXAMPLES
=================

WIRING IT UP — ONE CALL
-----------------------
    using CodeBrix.Platform.GameEngine;
    using CodeBrix.Platform.GameEngine.Sdl2;

    SdlGamepadManager gamepads = Engine.Instance.InitializeSdlGamepadManager();

That assigns the manager to Engine.Instance.Input.GamepadManager, which the
engine refreshes and feeds to GamepadEventPoller every cycle (Mode A) or every
tic (Mode B, from InputPump.PollNow). No other plumbing is needed, and NOTHING
DIFFERS BETWEEN THE TWO HOSTING MODES — the game never calls Update() in either.

WHERE TO PUT THAT CALL:
    Mode A (CodeBrixGameHost)             override OnConfigureGamepads()
    Mode B (SoftwareRenderedGameHostBase) override ConfigureGamepads()

Both hooks run after the input adapters are wired and before the game's content
loads, so a game can read controller availability while loading. Driving Engine
directly with no host base works too: call it any time after the adapters exist.

    // Mode A
    public sealed class MyGameHost : CodeBrixGameHost
    {
        private SdlGamepadManager? _gamepads;

        public MyGameHost(GameSurfaceCanvas renderSurface) : base(renderSurface) { }

        protected override void OnConfigureGamepads()
            => _gamepads = Engine.Instance.InitializeSdlGamepadManager();
    }

    // Mode B
    protected override void ConfigureGamepads()
        => _gamepads = Engine.Instance.InitializeSdlGamepadManager();

READING INPUT — POLLED, PER FRAME
---------------------------------
    using CodeBrix.Platform.GameEngine.Input.Gamepad;
    using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

    private void ReadGamepads(SdlGamepadManager gamepads)
    {
        foreach (SdlGamepadAdapter pad in gamepads.ConnectedAdapters)
        {
            GamepadStickState move = pad.LeftStick?.WithDeadzone() ?? default;

            // Clamp before using Magnitude as a speed scalar -- see PITFALLS.
            float speed = MathF.Min(move.Magnitude, 1f) * MaxSpeed;
            _playerX += move.X * speed * deltaSeconds;
            _playerY += move.Y * speed * deltaSeconds;   // +Y is UP

            bool firing = pad.PressedButtons.Contains(SdlGamepadButtons.A)
                          || pad.RightTrigger > 0.5f;

            if (move.Direction().HasFlag(StickDirection.Up)) { AimUp(); }
        }
    }

Hook it to a per-cycle or per-frame engine event (Mode A) or call it from the
game's own tic (Mode B):

    Engine.Instance.AfterBackgroundTasksExecute += () => ReadGamepads(_gamepads);

READING INPUT — EVENT-DRIVEN, VIA THE ENGINE'S POLLER
-----------------------------------------------------
    GamepadEventPoller? poller = Engine.Instance.Input.GamepadEventPoller;

    foreach (SdlGamepadAdapter pad in gamepads.ConnectedAdapters)
    {
        poller?.StartMonitoringButton(pad.GamepadId, SdlGamepadButtons.Start);
        poller?.StartMonitoringButton(pad.GamepadId, SdlGamepadButtons.A);
    }

    if (poller is not null)
    {
        poller.ButtonDown += args =>
        {
            if (args.Config.Button == SdlGamepadButtons.Start) { TogglePauseMenu(); }
            else if (args.Config.Button == SdlGamepadButtons.A) { Jump(args.Adapter); }
        };
    }

Re-register after a reconnect: a controller that disconnects and comes back gets
a NEW GamepadId (see PITFALLS).

INSPECTING AVAILABILITY — FOR A SETTINGS SCREEN
-----------------------------------------------
    SdlGamepadManager gamepads =
        Engine.Instance.InitializeSdlGamepadManager(logStatus: false);

    string status;
    if (!gamepads.IsAvailable)
    {
        status = gamepads.UnavailableReason ?? "Gamepad support is unavailable.";

        if (gamepads.UnavailableCause == SdlGamepadUnavailableCause.NativeLibraryMissing
            && !OperatingSystem.IsLinux())
        {
            // Ships inside the app on Windows and macOS -- this is a packaging bug.
            ReportPackagingProblem(status);
        }
    }
    else
    {
        status = gamepads.GetNoControllersHint()
                 ?? $"{gamepads.ConnectedAdapters.Count} controller(s) connected.";
    }

    ShowInSettings(status);

KEEP THE MANAGER if the game has a settings screen. When a player enables
gamepad support and nothing happens, UnavailableReason is the text to show, and
the startup log and the settings screen then read the SAME property instead of
each working the answer out for themselves.

DISPOSAL — WHO OWNS THE MANAGER
-------------------------------
Engine.Dispose() does NOT dispose the gamepad manager. It stops button
monitoring for every connected adapter and clears its own state, but the SDL2
subsystem and the open controller handles belong to the manager, so:

  * A game whose process ends with the engine can simply let the process exit;
    SDL2 is released with it. This is the common case and needs no code.
  * A game that TEARS THE ENGINE DOWN AND KEEPS RUNNING — a shell that returns
    to a launcher menu, a test harness, anything that will build a new Engine
    afterwards — MUST call gamepads.Dispose() itself, after Engine.Dispose(),
    and must not reuse the disposed manager. Call InitializeSdlGamepadManager
    again on the next engine.
  * Dispose() is IDEMPOTENT and Update() after Dispose() does nothing, so a
    defensive extra call is harmless.
  * Do NOT dispose individual SdlGamepadAdapter instances; the manager owns
    them and disposes them with itself.

    public void ShutDown()
    {
        Engine.Instance.Dispose();
        _gamepads?.Dispose();
        _gamepads = null;
    }

MINIMUM VIABLE PROJECT
======================
This package adds ONE PackageReference and ONE override to an existing
CodeBrix.Platform game. Everything else below is the ordinary engine layout,
which the engine's own AGENT-README.txt (repository root) covers in full.
Version attributes are omitted — use the latest of each package.

MyGame.Core/MyGame.Core.csproj  (the shared library — both references live here)

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>MyGame</RootNamespace>
        <Nullable>enable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.ApacheLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.Fonts.OpenSans.ApacheLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.GameEngine.MitLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever" />
      </ItemGroup>
    </Project>

MyGame.Core/MyGameHost.cs  (the whole gamepad contract, in one file)

    using CodeBrix.Platform.GameEngine;
    using CodeBrix.Platform.GameEngine.Host.Hosting;
    using CodeBrix.Platform.GameEngine.Host.Rendering;
    using CodeBrix.Platform.GameEngine.Input.Gamepad;
    using CodeBrix.Platform.GameEngine.Sdl2;
    using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
    using System;

    namespace MyGame;

    public sealed class MyGameHost : CodeBrixGameHost
    {
        private SdlGamepadManager? _gamepads;

        public MyGameHost(GameSurfaceCanvas renderSurface) : base(renderSurface) { }

        protected override void OnConfigureGamepads()
        {
            _gamepads = Engine.Instance.InitializeSdlGamepadManager();

            if (!_gamepads.IsAvailable)
            {
                // Not fatal: keyboard and mouse still work.
                ShowMessage(_gamepads.UnavailableReason ?? "No gamepad support.");
                return;
            }

            Engine.Instance.AfterBackgroundTasksExecute += PollGamepads;
        }

        private void PollGamepads()
        {
            if (_gamepads is null) { return; }

            foreach (SdlGamepadAdapter pad in _gamepads.ConnectedAdapters)
            {
                GamepadStickState move = pad.LeftStick?.WithDeadzone() ?? default;
                if (move.IsEngaged()) { MovePlayer(move.X, move.Y); }

                if (pad.PressedButtons.Contains(SdlGamepadButtons.A)) { Jump(); }
            }
        }

        protected override void UnhookEvents()
        {
            Engine.Instance.AfterBackgroundTasksExecute -= PollGamepads;
        }
    }

On LINUX the target machine also needs "sudo apt install libsdl2-2.0-0"; on
Windows and macOS nothing extra is required. Without SDL2 the code above still
runs — IsAvailable is false and the game plays on keyboard and mouse.

PERFORMANCE TIPS
================
  * The native device read is ALREADY THROTTLED by the engine, at
    EngineConfiguration.TimeBetweenGamepadStateUpdates (seconds). The default
    sits well above every practical tic rate, so a normal game refreshes on
    every tic and the throttle only caps a runaway caller. Do not add throttling
    of your own, and do not call SdlGamepadManager.Update() to "get fresher"
    values — that bypasses the cap the seam explicitly requires.
  * Reading adapter properties is FREE. They are snapshots taken during the
    refresh, not native calls, so reading LeftStick, RightStick, both triggers
    and PressedButtons in the same frame costs nothing beyond field reads.
  * PressedButtons is a HashSet<string> view with ordinal comparison. Contains()
    is O(1); prefer it to LINQ over the collection in a per-frame path.
  * Do not allocate per frame from the collections. ConnectedAdapters and
    PressedButtons are live views reused every refresh — enumerate them, do not
    ToList() them each frame.
  * Prefer the per-FRAME engine events (BeforeFrameRender / AfterFrameRender) or
    a timer over the per-cycle pair for anything but the cheapest gamepad
    reading; the per-cycle events run thousands of times per second.
  * Event-driven reading via GamepadEventPoller costs no extra device polling —
    it consumes the same refresh the engine already performed.
  * SDL2's own event queue is NOT pumped by this package (controller events are
    disabled at start-up), so there is no second event loop and no queue growing
    behind your back.

COMMON PITFALLS TO AVOID
========================
  * A CONTROLLER THAT RECONNECTS GETS A NEW GamepadId. It derives from SDL2's
    joystick instance ID, which is not reused while a device stays connected.
    Anything registered against the old id — button monitoring in particular —
    must be registered again. Bluetooth controllers sleep aggressively, so this
    happens in normal use, not just in edge cases.
  * STICK Y IS INVERTED RELATIVE TO SDL2. SDL2 reports up as NEGATIVE; the
    engine's GamepadStickState defines +1 as UP. The adapter handles it. Raw
    SDL2 values read through the Native layer do not.
  * A RESTING STICK DOES NOT READ ZERO. Observed drift on a real pad is around
    1500-2900 raw units. Use WithDeadzone() / IsEngaged() rather than testing
    != 0.
  * GamepadStickState.Magnitude CAN EXCEED 1.0 — up to sqrt(2) on a diagonal. X
    and Y are each clamped to [-1, 1] INDEPENDENTLY, so a stick held hard into a
    corner legitimately reports e.g. (-0.34, -1.00) with magnitude 1.06; values
    up to 1.25 have been measured on real hardware. This does not affect
    Direction() or IsEngaged(), which is exactly why it goes unnoticed. It DOES
    matter if a game multiplies movement speed by Magnitude: that yields up to
    41% extra speed on diagonals — the classic diagonal-speed-boost bug. Clamp
    the magnitude to 1, or normalize the vector, before using it as a scalar.
  * THE FIRST POLL AFTER A CONTROLLER CONNECTS IS DISCARDED ON PURPOSE. A
    freshly connected Bluetooth pad has been observed reporting every stick axis
    at -32768 — full deflection — until its first HID report arrives. A deadzone
    cannot filter that, because it is a maximum reading, not a small one. Sticks
    and triggers read neutral for one frame after connect; buttons are
    trustworthy immediately.
  * DO NOT Math.Abs() A RAW AXIS VALUE AS A short. -32768 is a real reading and
    negating it overflows a signed 16-bit type. Widen to int first.
  * BUTTON AND AXIS NUMBERING IS DEVICE- AND TRANSPORT-SPECIFIC. The same Xbox
    pad reports a different raw layout over Bluetooth than over USB. Always go
    through the game controller API (SdlGamepadButtons / the adapter), never raw
    joystick indices. The startup log records each pad's SDL2 mapping string,
    which is the first thing to look at when a controller behaves oddly.
  * VERIFY GAMEPAD CHANGES IN BOTH HOSTING MODES. The first release of this
    package was hardware-verified only through a harness that called
    manager.Update() itself, so it could not notice that NOTHING called Update()
    on the InputPump path — gamepads were completely dead in Mode B (frozen
    state, no events, no hotplug) while every hardware check passed. A check
    that supplies its own driver cannot detect a missing driver.
  * USE THE SdlGamepadButtons CONSTANTS, not string literals. The seam is
    string-keyed, so a misspelled button name registers happily and then never
    fires — there is no error to see.
  * DO NOT CACHE ConnectedAdapters OR PressedButtons. Both are live views reused
    across refreshes; a copy stops tracking hotplug, and a retained
    PressedButtons snapshot is simply the current frame's set under a different
    name.
  * A KIOSK OR FRAME BUFFER DEPLOYMENT HAS A SECOND FAILURE MODE. SDL2's evdev
    backend needs read access to /dev/input/event*, which a desktop login
    session grants through a per-device ACL but a bare service account may not
    have. That looks IDENTICAL to "no controller plugged in" —
    GetNoControllersHint() tests for it specifically and, when it applies, says
    to add the account to the 'input' group.
  * Engine.Instance.Input.GamepadEventPoller IS NULLABLE. Use ?. rather than
    assuming it exists.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * NO LINUX SDL2 BINARY IS SHIPPED. The system libSDL2 is used instead; see
    INSTALLATION for the one apt command. Windows and macOS natives ARE in the
    package.
  * IT DOES NOT PUMP THE SDL2 EVENT QUEUE. Controller state is polled directly
    and SDL2's controller events are disabled, so no second event loop runs
    alongside the CodeBrix.Platform one and no SDL2 queue accumulates.
  * IT NEVER THROWS for a missing SDL2 or a missing controller. Every failure
    mode is reported through IsAvailable / UnavailableReason / UnavailableCause
    / GetNoControllersHint(), and the game keeps running.
  * NO WINDOWING, RENDERING, AUDIO, KEYBOARD OR MOUSE. Only SDL2's joystick and
    game controller entry points are bound; CodeBrix.Platform owns everything
    else, which is the whole reason one gamepad implementation can serve every
    head.
  * NO RUMBLE, HAPTICS, LED, GYRO, TOUCHPAD, BATTERY LEVEL OR AUDIO ROUTING. The
    bound surface is buttons, sticks, triggers, identity and hotplug.
  * NO RAW JOYSTICK MODE. Devices SDL2 does not recognise as game controllers
    are not surfaced; there is no fallback to raw axis and button indices, and
    no API for supplying custom mapping strings.
  * NO REBINDING OR PROFILE UI. Button identity is reported; mapping a game
    action to a button is the game's job.
  * IT DOES NOT DRIVE ITSELF. Update() is called by whichever engine loop is
    running. Nothing here starts a thread or a timer of its own.
  * IT DOES NOT DISPOSE ITSELF WITH THE ENGINE. See DISPOSAL above.

WORKING EXAMPLES ON GITHUB
==========================
Repository root: https://github.com/ellisnet/CodeBrix.Platform.GameEngine

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tools/padcheck
      padcheck — an interactive HARDWARE check that drives the real
      SdlGamepadManager (it is not a reimplementation), so what it prints is
      what a game would see. It is the worked example of both drive paths:
      default mode refreshes through manager.Update(), and --pump exercises the
      Mode-B InputPump.PollNow path. Program.cs shows availability inspection,
      hotplug handling, stick/trigger reading and the magnitude-above-1
      diagnostic in one file. Its README lists the physical checks worth running
      (face-button order, stick up = positive Y, left trigger is left, sleep and
      wake).

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.Sdl2.Tests
      SdlGamepadManagerTests.cs — the consumer contract asserted as invariants
          that hold WITH OR WITHOUT SDL2 installed and with or without a
          controller attached: TryStart never throws and always produces a
          manager, its result agrees with IsAvailable, availability and
          UnavailableReason stay consistent, ConnectedAdapters returns the same
          live instance every time, Update() does not throw and does nothing
          after Dispose(), Dispose() is idempotent, GetNoControllersHint() is
          null when support is unavailable, and the manager satisfies the
          engine's IGamepadManager contract.
      Sdl2LibraryTests.cs — the loader: probe candidates are never empty, are
          platform-appropriate, prefer the versioned soname on Linux, are stable
          across calls; the load outcome is self-consistent; GetFunction returns
          null for an export that does not exist; bindings do not throw when
          SDL2 is unavailable.
      SdlAxisConversionTests.cs — the conversions real hardware cannot be made
          to produce on demand: negative raw Y means UP, the -32768 edge does
          not overflow, full deflection maps to 1, a centred stick maps to zero,
          resting drift stays inside the default deadzone, and trigger values
          map 0..1 with negatives treated as released.
      SdlGamepadButtonsTests.cs — SdlGamepadButtons.All is ordered to match the
          SDL2 button enum, covers every button it defines, and has no duplicate
          or blank names.
      JoystickDeviceScanTests.cs — the /dev/input ACL detection behind
          GetNoControllersHint(), driven with fabricated
          /proc/bus/input/devices content.

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.Tests
      InputPumpGamepadTests.cs — the engine-side Mode-B refresh path this
      package plugs into.

QUICK REFERENCE CARD
====================
INSTALL
    dotnet add package CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever
    Linux only:  sudo apt install libsdl2-2.0-0

WIRE UP (once, after the engine is started)
    using CodeBrix.Platform.GameEngine.Sdl2;
    SdlGamepadManager pads = Engine.Instance.InitializeSdlGamepadManager();
        Mode A -> override OnConfigureGamepads()
        Mode B -> override ConfigureGamepads()

MANAGER  (CodeBrix.Platform.GameEngine.Sdl2.Gamepad.SdlGamepadManager)
    static bool TryStart(out SdlGamepadManager manager)
    bool IsAvailable                        // SDL2 loaded + subsystem started
    string? UnavailableReason               // player-facing; null when available
    SdlGamepadUnavailableCause UnavailableCause  // None / NativeLibraryMissing /
                                                 // SubsystemInitializationFailed
    IReadOnlyCollection<SdlGamepadAdapter> ConnectedAdapters   // LIVE view
    string? GetNoControllersHint()          // available but nothing connected
    void Update()                           // *** the ENGINE calls this ***
    void Dispose()                          // Engine.Dispose does NOT

ADAPTER  (SdlGamepadAdapter)
    string GamepadId          // "sdl:<instanceId>"; CHANGES on reconnect
    int InstanceId            string Name
    IReadOnlyCollection<string> PressedButtons     // live view; do not cache
    GamepadStickState? LeftStick / RightStick
    float LeftTrigger / RightTrigger               // 0.0 .. 1.0
    bool IsConnected          string GetMappingString()

BUTTON NAMES  (SdlGamepadButtons — use the constants, never literals)
    A B X Y Back Guide Start LeftStick RightStick LeftShoulder RightShoulder
    DPadUp DPadDown DPadLeft DPadRight        + IReadOnlyList<string> All

STICK  (engine type GamepadStickState; +Y is UP)
    X Y RawX RawY  Magnitude  Angle
    IsEngaged(0.15f)  Direction(0.15f) -> StickDirection  WithDeadzone(0.15f)

POLLED READ
    foreach (var pad in pads.ConnectedAdapters)
    {
        var move = pad.LeftStick?.WithDeadzone() ?? default;
        float speed = MathF.Min(move.Magnitude, 1f);     // clamp! see PITFALLS
        bool fire = pad.PressedButtons.Contains(SdlGamepadButtons.A)
                    || pad.RightTrigger > 0.5f;
    }

EVENT READ
    var poller = Engine.Instance.Input.GamepadEventPoller;   // NULLABLE
    poller?.StartMonitoringButton(pad.GamepadId, SdlGamepadButtons.Start);
    if (poller is not null)
        poller.ButtonDown += args => { /* args.Config.Button, args.Adapter */ };

NATIVE LAYER  (…Sdl2.Native: Sdl2Native, Sdl2Library, SDLInitFlags,
    SDL_GameController, SDL_Joystick, SDL_GameControllerAxis,
    SDL_GameControllerButton) — public for interop; NOT for app code.

TOP FIVE MISTAKES
    1. Using Magnitude as a speed scalar without clamping (diagonal boost).
    2. Not re-registering button monitoring after a reconnect (new GamepadId).
    3. Testing a stick against != 0 instead of WithDeadzone()/IsEngaged().
    4. Calling manager.Update() from the game (defeats the engine throttle).
    5. Verifying a gamepad change in only one hosting mode.

================================================================================
END OF AGENT-README
================================================================================
