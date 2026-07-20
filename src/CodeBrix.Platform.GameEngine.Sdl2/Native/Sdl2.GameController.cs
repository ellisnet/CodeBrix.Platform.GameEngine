// Vendored from Veldrid -- https://github.com/veldrid/veldrid  (src/Veldrid.SDL2/Sdl2.GameController.cs)
// The MIT License (MIT). Copyright (c) 2017 Eric Mellino and Veldrid contributors.
// See THIRD-PARTY-NOTICES.txt in the repository root for the full license text.
//
// Changes from upstream, all marked inline with "for CodeBrix":
//   * namespace changed, block namespace converted to file-scoped
//   * loaded delegates are nullable and null-checked, because our loader returns null rather
//     than throwing when SDL2 is absent
//   * XML doc comments completed (this package builds with GenerateDocumentationFile)
//   * SDL_GameControllerMapping added, for diagnostics
//   * the SDL_ControllerAxisEvent / SDL_ControllerButtonEvent / SDL_ControllerDeviceEvent structs
//     are NOT vendored. They only carry meaning when reading the SDL2 event queue, and this
//     package polls controller state directly instead of pumping SDL2 events -- pumping them
//     would mean running an SDL2 event loop alongside the CodeBrix.Platform one.

using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Native; //was previously: Veldrid.Sdl2;

/// <summary>
/// A transparent wrapper over a pointer to a native SDL_GameController.
/// </summary>
public struct SDL_GameController
{
    /// <summary>
    /// The native SDL_GameController pointer.
    /// </summary>
    public readonly IntPtr NativePointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SDL_GameController"/> struct.
    /// </summary>
    /// <param name="pointer">The native SDL_GameController pointer.</param>
    public SDL_GameController(IntPtr pointer)
    {
        NativePointer = pointer;
    }

    /// <summary>
    /// Converts an <see cref="SDL_GameController"/> to its underlying native pointer.
    /// </summary>
    /// <param name="controller">The controller to convert.</param>
    public static implicit operator IntPtr(SDL_GameController controller) => controller.NativePointer;

    /// <summary>
    /// Wraps a native pointer as an <see cref="SDL_GameController"/>.
    /// </summary>
    /// <param name="pointer">The native SDL_GameController pointer.</param>
    public static implicit operator SDL_GameController(IntPtr pointer) => new SDL_GameController(pointer);
}

/// <summary>
/// The list of axes available from a controller.
/// </summary>
/// <remarks>
/// Thumbstick axis values range from -32768 to 32767 and are centered within roughly 8000 of zero,
/// though the exact resting value varies between controllers and warrants a deadzone. Trigger axis
/// values range from 0 to 32767.
/// <para>
/// Note that the Y axes are negative when the stick is pushed UP, which is the opposite of the
/// convention used by <c>GamepadStickState</c>; the adapter in this package inverts them.
/// </para>
/// </remarks>
public enum SDL_GameControllerAxis : byte
{
    /// <summary>An unrecognized or unmapped axis.</summary>
    Invalid = unchecked((byte)-1),

    /// <summary>The horizontal axis of the left thumbstick.</summary>
    LeftX = 0,

    /// <summary>The vertical axis of the left thumbstick. Negative is up.</summary>
    LeftY,

    /// <summary>The horizontal axis of the right thumbstick.</summary>
    RightX,

    /// <summary>The vertical axis of the right thumbstick. Negative is up.</summary>
    RightY,

    /// <summary>The left trigger, ranging from 0 to 32767.</summary>
    TriggerLeft,

    /// <summary>The right trigger, ranging from 0 to 32767.</summary>
    TriggerRight,

    /// <summary>The number of axes. Not a real axis.</summary>
    Max,
}

/// <summary>
/// The list of buttons available from a controller.
/// </summary>
public enum SDL_GameControllerButton : byte
{
    /// <summary>An unrecognized or unmapped button.</summary>
    Invalid = unchecked((byte)-1),

    /// <summary>The A button (bottom of the face-button diamond).</summary>
    A = 0,

    /// <summary>The B button (right of the face-button diamond).</summary>
    B,

    /// <summary>The X button (left of the face-button diamond).</summary>
    X,

    /// <summary>The Y button (top of the face-button diamond).</summary>
    Y,

    /// <summary>The Back / View / Select button.</summary>
    Back,

    /// <summary>The Guide button (the illuminated logo button).</summary>
    Guide,

    /// <summary>The Start / Menu button.</summary>
    Start,

    /// <summary>The left thumbstick pressed inward.</summary>
    LeftStick,

    /// <summary>The right thumbstick pressed inward.</summary>
    RightStick,

    /// <summary>The left shoulder bumper.</summary>
    LeftShoulder,

    /// <summary>The right shoulder bumper.</summary>
    RightShoulder,

    /// <summary>Directional pad up.</summary>
    DPadUp,

    /// <summary>Directional pad down.</summary>
    DPadDown,

    /// <summary>Directional pad left.</summary>
    DPadLeft,

    /// <summary>Directional pad right.</summary>
    DPadRight,

    /// <summary>The number of buttons. Not a real button.</summary>
    Max,
}

public static unsafe partial class Sdl2Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate SDL_GameController SDL_GameControllerOpen_t(int joystick_index);
    private static readonly SDL_GameControllerOpen_t? s_sdl_gameControllerOpen = LoadFunction<SDL_GameControllerOpen_t>("SDL_GameControllerOpen");

    /// <summary>
    /// Open a game controller for use.
    /// </summary>
    /// <param name="joystick_index">
    /// The index of the N'th game controller on the system. This index is not the value that will
    /// identify the controller in future controller events; the joystick's instance ID is used there.
    /// </param>
    /// <returns>A controller identifier, or a null pointer if an error occurred.</returns>
    public static SDL_GameController SDL_GameControllerOpen(int joystick_index)
        => s_sdl_gameControllerOpen is null ? default : s_sdl_gameControllerOpen(joystick_index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_GameControllerClose_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerClose_t? s_sdl_gameControllerClose = LoadFunction<SDL_GameControllerClose_t>("SDL_GameControllerClose");

    /// <summary>
    /// Close a controller previously opened with <see cref="SDL_GameControllerOpen"/>.
    /// </summary>
    /// <param name="gamecontroller">The controller to close.</param>
    public static void SDL_GameControllerClose(SDL_GameController gamecontroller)
        => s_sdl_gameControllerClose?.Invoke(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_IsGameController_t(int joystick_index);
    private static readonly SDL_IsGameController_t? s_sdl_isGameController = LoadFunction<SDL_IsGameController_t>("SDL_IsGameController");

    /// <summary>
    /// Is the joystick on this index supported by the game controller interface?
    /// </summary>
    /// <param name="joystick_index">The device index to test.</param>
    /// <returns><see langword="true"/> if SDL2 has a controller mapping for the device.</returns>
    public static bool SDL_IsGameController(int joystick_index)
        => s_sdl_isGameController is not null && s_sdl_isGameController(joystick_index) != 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte* SDL_GameControllerNameForIndex_t(int joystick_index);
    private static readonly SDL_GameControllerNameForIndex_t? s_sdl_gameControllerNameForIndex = LoadFunction<SDL_GameControllerNameForIndex_t>("SDL_GameControllerNameForIndex");

    /// <summary>
    /// Get the implementation dependent name of a game controller. This can be called before any
    /// controllers are opened.
    /// </summary>
    /// <param name="joystick_index">The device index to query.</param>
    /// <returns>A pointer to a null-terminated UTF-8 name, or null if no name can be found.</returns>
    public static byte* SDL_GameControllerNameForIndex(int joystick_index)
        => s_sdl_gameControllerNameForIndex is null ? null : s_sdl_gameControllerNameForIndex(joystick_index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate SDL_GameController SDL_GameControllerFromInstanceID_t(int joyid);
    private static readonly SDL_GameControllerFromInstanceID_t? s_sdl_gameControllerFromInstanceID = LoadFunction<SDL_GameControllerFromInstanceID_t>("SDL_GameControllerFromInstanceID");

    /// <summary>
    /// Return the game controller associated with an instance ID.
    /// </summary>
    /// <param name="joyid">The joystick instance ID.</param>
    /// <returns>The associated controller, or a null pointer.</returns>
    public static SDL_GameController SDL_GameControllerFromInstanceID(int joyid)
        => s_sdl_gameControllerFromInstanceID is null ? default : s_sdl_gameControllerFromInstanceID(joyid);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte* SDL_GameControllerName_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerName_t? s_sdl_gameControllerName = LoadFunction<SDL_GameControllerName_t>("SDL_GameControllerName");

    /// <summary>
    /// Return the name for a currently opened controller.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>A pointer to a null-terminated UTF-8 name, or null.</returns>
    public static byte* SDL_GameControllerName(SDL_GameController gamecontroller)
        => s_sdl_gameControllerName is null ? null : s_sdl_gameControllerName(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte* SDL_GameControllerMapping_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerMapping_t? s_sdl_gameControllerMapping = LoadFunction<SDL_GameControllerMapping_t>("SDL_GameControllerMapping"); //for CodeBrix: not in upstream

    /// <summary>
    /// Return the SDL2 mapping string in use for an opened controller. //for CodeBrix: not in upstream
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>
    /// A pointer to a null-terminated UTF-8 mapping string that must be released with
    /// <see cref="SDL_free"/>, or null.
    /// </returns>
    /// <remarks>
    /// Purely diagnostic. The mapping string is what reconciles a specific device's raw button and
    /// axis numbering with the standard layout, and logging it makes an unexpectedly behaving
    /// controller far easier to reason about.
    /// </remarks>
    public static byte* SDL_GameControllerMapping(SDL_GameController gamecontroller)
        => s_sdl_gameControllerMapping is null ? null : s_sdl_gameControllerMapping(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SDL_GameControllerGetVendor_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerGetVendor_t? s_sdl_gameControllerGetVendor = LoadFunction<SDL_GameControllerGetVendor_t>("SDL_GameControllerGetVendor");

    /// <summary>
    /// Get the USB vendor ID of an opened controller, if available.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>The vendor ID, or 0 if it is not available.</returns>
    public static ushort SDL_GameControllerGetVendor(SDL_GameController gamecontroller)
        => s_sdl_gameControllerGetVendor is null ? (ushort)0 : s_sdl_gameControllerGetVendor(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SDL_GameControllerGetProduct_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerGetProduct_t? s_sdl_gameControllerGetProduct = LoadFunction<SDL_GameControllerGetProduct_t>("SDL_GameControllerGetProduct");

    /// <summary>
    /// Get the USB product ID of an opened controller, if available.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>The product ID, or 0 if it is not available.</returns>
    public static ushort SDL_GameControllerGetProduct(SDL_GameController gamecontroller)
        => s_sdl_gameControllerGetProduct is null ? (ushort)0 : s_sdl_gameControllerGetProduct(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SDL_GameControllerGetProductVersion_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerGetProductVersion_t? s_sdl_gameControllerGetProductVersion = LoadFunction<SDL_GameControllerGetProductVersion_t>("SDL_GameControllerGetProductVersion");

    /// <summary>
    /// Get the product version of an opened controller, if available.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>The product version, or 0 if it is not available.</returns>
    public static ushort SDL_GameControllerGetProductVersion(SDL_GameController gamecontroller)
        => s_sdl_gameControllerGetProductVersion is null ? (ushort)0 : s_sdl_gameControllerGetProductVersion(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_GameControllerGetAttached_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerGetAttached_t? s_sdl_gameControllerGetAttached = LoadFunction<SDL_GameControllerGetAttached_t>("SDL_GameControllerGetAttached");

    /// <summary>
    /// Reports whether a controller has been opened and is currently connected.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>1 if the controller is attached, otherwise 0.</returns>
    public static int SDL_GameControllerGetAttached(SDL_GameController gamecontroller)
        => s_sdl_gameControllerGetAttached is null ? 0 : s_sdl_gameControllerGetAttached(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate SDL_Joystick SDL_GameControllerGetJoystick_t(SDL_GameController gamecontroller);
    private static readonly SDL_GameControllerGetJoystick_t? s_sdl_gameControllerGetJoystick = LoadFunction<SDL_GameControllerGetJoystick_t>("SDL_GameControllerGetJoystick");

    /// <summary>
    /// Get the underlying joystick object used by a controller.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <returns>The underlying joystick.</returns>
    public static SDL_Joystick SDL_GameControllerGetJoystick(SDL_GameController gamecontroller)
        => s_sdl_gameControllerGetJoystick is null ? default : s_sdl_gameControllerGetJoystick(gamecontroller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_GameControllerEventState_t(int state);
    private static readonly SDL_GameControllerEventState_t? s_sdl_gameControllerEventState = LoadFunction<SDL_GameControllerEventState_t>("SDL_GameControllerEventState");

    /// <summary>
    /// Enable or disable controller event polling.
    /// </summary>
    /// <param name="state">One of SDL_QUERY (-1), SDL_IGNORE (0) or SDL_ENABLE (1).</param>
    /// <returns>The resulting state, or a negative error code.</returns>
    /// <remarks>
    /// If controller events are disabled, <see cref="SDL_GameControllerUpdate"/> must be called
    /// manually before reading controller state. This package always polls explicitly, so it does
    /// not depend on the SDL2 event queue being pumped.
    /// </remarks>
    public static int SDL_GameControllerEventState(int state)
        => s_sdl_gameControllerEventState is null ? -1 : s_sdl_gameControllerEventState(state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_GameControllerUpdate_t();
    private static readonly SDL_GameControllerUpdate_t? s_sdl_gameControllerUpdate = LoadFunction<SDL_GameControllerUpdate_t>("SDL_GameControllerUpdate");

    /// <summary>
    /// Update the current state of the open game controllers.
    /// </summary>
    /// <remarks>
    /// This is called automatically by the SDL2 event loop if controller events are enabled. On
    /// macOS it is also what pumps the IOKit run loop that delivers hotplug notifications, so it
    /// must be called regularly even when no controller is currently connected.
    /// </remarks>
    public static void SDL_GameControllerUpdate() => s_sdl_gameControllerUpdate?.Invoke();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate short SDL_GameControllerGetAxis_t(SDL_GameController gamecontroller, SDL_GameControllerAxis axis);
    private static readonly SDL_GameControllerGetAxis_t? s_sdl_gameControllerGetAxis = LoadFunction<SDL_GameControllerGetAxis_t>("SDL_GameControllerGetAxis");

    /// <summary>
    /// Get the current state of an axis control on a game controller.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <param name="axis">The axis to read.</param>
    /// <returns>
    /// A value ranging from -32768 to 32767, except for the triggers, which range from 0 to 32767.
    /// Returns 0 if SDL2 is unavailable.
    /// </returns>
    public static short SDL_GameControllerGetAxis(SDL_GameController gamecontroller, SDL_GameControllerAxis axis)
        => s_sdl_gameControllerGetAxis is null ? (short)0 : s_sdl_gameControllerGetAxis(gamecontroller, axis);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte SDL_GameControllerGetButton_t(SDL_GameController gamecontroller, SDL_GameControllerButton button);
    private static readonly SDL_GameControllerGetButton_t? s_sdl_gameControllerGetButton = LoadFunction<SDL_GameControllerGetButton_t>("SDL_GameControllerGetButton");

    /// <summary>
    /// Get the current state of a button on a game controller.
    /// </summary>
    /// <param name="gamecontroller">The controller to query.</param>
    /// <param name="button">The button to read.</param>
    /// <returns>1 if the button is pressed, otherwise 0.</returns>
    public static byte SDL_GameControllerGetButton(SDL_GameController gamecontroller, SDL_GameControllerButton button)
        => s_sdl_gameControllerGetButton is null ? (byte)0 : s_sdl_gameControllerGetButton(gamecontroller, button);
}
