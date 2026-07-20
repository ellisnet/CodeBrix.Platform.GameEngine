// Vendored from Veldrid -- https://github.com/veldrid/veldrid  (src/Veldrid.SDL2/Sdl2.Joystick.cs)
// The MIT License (MIT). Copyright (c) 2017 Eric Mellino and Veldrid contributors.
// See THIRD-PARTY-NOTICES.txt in the repository root for the full license text.
//
// Changes from upstream, all marked inline with "for CodeBrix":
//   * namespace changed, block namespace converted to file-scoped
//   * loaded delegates are nullable and null-checked, because our loader returns null rather
//     than throwing when SDL2 is absent
//   * XML doc comments added (this package builds with GenerateDocumentationFile)
//   * SDL_JoystickGetDeviceInstanceID added -- needed to detect hotplugged devices without
//     having to open every device first

using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Native; //was previously: Veldrid.Sdl2;

/// <summary>
/// A transparent wrapper over a pointer to a native SDL_Joystick.
/// </summary>
public struct SDL_Joystick
{
    /// <summary>
    /// The native SDL_Joystick pointer.
    /// </summary>
    public readonly IntPtr NativePointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SDL_Joystick"/> struct.
    /// </summary>
    /// <param name="pointer">The native SDL_Joystick pointer.</param>
    public SDL_Joystick(IntPtr pointer)
    {
        NativePointer = pointer;
    }

    /// <summary>
    /// Converts an <see cref="SDL_Joystick"/> to its underlying native pointer.
    /// </summary>
    /// <param name="joystick">The joystick to convert.</param>
    public static implicit operator IntPtr(SDL_Joystick joystick) => joystick.NativePointer;

    /// <summary>
    /// Wraps a native pointer as an <see cref="SDL_Joystick"/>.
    /// </summary>
    /// <param name="pointer">The native SDL_Joystick pointer.</param>
    public static implicit operator SDL_Joystick(IntPtr pointer) => new SDL_Joystick(pointer);
}

public static unsafe partial class Sdl2Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_NumJoysticks_t();
    private static readonly SDL_NumJoysticks_t? s_sdl_numJoysticks = LoadFunction<SDL_NumJoysticks_t>("SDL_NumJoysticks");

    /// <summary>
    /// Count the number of joysticks attached to the system right now.
    /// </summary>
    /// <returns>The number of attached joysticks, or 0 if SDL2 is unavailable.</returns>
    public static int SDL_NumJoysticks() => s_sdl_numJoysticks is null ? 0 : s_sdl_numJoysticks();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_JoystickInstanceID_t(SDL_Joystick joystick);
    private static readonly SDL_JoystickInstanceID_t? s_sdl_joystickInstanceID = LoadFunction<SDL_JoystickInstanceID_t>("SDL_JoystickInstanceID");

    /// <summary>
    /// Returns the instance ID of the specified joystick.
    /// </summary>
    /// <param name="joystick">The joystick to query.</param>
    /// <returns>
    /// The instance ID on success, or a negative error code on failure. -1 if SDL2 is unavailable.
    /// </returns>
    /// <remarks>
    /// Unlike a device index, an instance ID is not reused while the device stays connected, which
    /// makes it suitable for identifying a controller across hotplug events.
    /// </remarks>
    public static int SDL_JoystickInstanceID(SDL_Joystick joystick)
        => s_sdl_joystickInstanceID is null ? -1 : s_sdl_joystickInstanceID(joystick);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_JoystickGetDeviceInstanceID_t(int device_index);
    private static readonly SDL_JoystickGetDeviceInstanceID_t? s_sdl_joystickGetDeviceInstanceID = LoadFunction<SDL_JoystickGetDeviceInstanceID_t>("SDL_JoystickGetDeviceInstanceID"); //for CodeBrix: not in upstream

    /// <summary>
    /// Returns the instance ID of a joystick identified by device index, without opening it. //for CodeBrix: not in upstream
    /// </summary>
    /// <param name="device_index">The device index to query.</param>
    /// <returns>
    /// The instance ID on success, or a negative error code on failure. -1 if SDL2 is unavailable.
    /// </returns>
    /// <remarks>
    /// Device indices are positional and shift as devices come and go, so they cannot be used to
    /// recognize a controller across a hotplug. This maps an index to the stable instance ID
    /// without the side effect of opening the device, which is what makes it possible to scan for
    /// newly attached controllers and skip the ones already being tracked.
    /// </remarks>
    public static int SDL_JoystickGetDeviceInstanceID(int device_index)
        => s_sdl_joystickGetDeviceInstanceID is null ? -1 : s_sdl_joystickGetDeviceInstanceID(device_index);
}
