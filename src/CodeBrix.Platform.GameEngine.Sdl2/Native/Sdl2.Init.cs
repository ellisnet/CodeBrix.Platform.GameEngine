// Vendored from Veldrid -- https://github.com/veldrid/veldrid  (src/Veldrid.SDL2/Sdl2.Init.cs)
// The MIT License (MIT). Copyright (c) 2017 Eric Mellino and Veldrid contributors.
// See THIRD-PARTY-NOTICES.txt in the repository root for the full license text.
//
// Changes from upstream, all marked inline with "for CodeBrix":
//   * namespace changed, block namespace converted to file-scoped
//   * the loaded delegate is nullable and null-checked, because our loader returns null rather
//     than throwing when SDL2 is absent
//   * XML doc comments added (this package builds with GenerateDocumentationFile)
//   * SDL_Quit and SDL_WasInit added -- upstream never shut SDL down, but a gamepad manager that
//     can be disposed and restarted needs both

using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Native; //was previously: Veldrid.Sdl2;

public static unsafe partial class Sdl2Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_Init_t(SDLInitFlags flags);
    private static readonly SDL_Init_t? s_sdl_init = LoadFunction<SDL_Init_t>("SDL_Init");

    /// <summary>
    /// Initializes the SDL2 subsystems identified by <paramref name="flags"/>.
    /// </summary>
    /// <param name="flags">The subsystems to initialize.</param>
    /// <returns>0 on success, or a negative error code on failure. -1 if SDL2 is unavailable.</returns>
    public static int SDL_Init(SDLInitFlags flags) => s_sdl_init is null ? -1 : s_sdl_init(flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_Quit_t();
    private static readonly SDL_Quit_t? s_sdl_quit = LoadFunction<SDL_Quit_t>("SDL_Quit"); //for CodeBrix: not in upstream

    /// <summary>
    /// Shuts down all initialized SDL2 subsystems. //for CodeBrix: not in upstream
    /// </summary>
    public static void SDL_Quit() { s_sdl_quit?.Invoke(); }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SDL_WasInit_t(SDLInitFlags flags);
    private static readonly SDL_WasInit_t? s_sdl_wasInit = LoadFunction<SDL_WasInit_t>("SDL_WasInit"); //for CodeBrix: not in upstream

    /// <summary>
    /// Reports which of the requested SDL2 subsystems are currently initialized. //for CodeBrix: not in upstream
    /// </summary>
    /// <param name="flags">The subsystems to query, or 0 to query all of them.</param>
    /// <returns>A mask of the initialized subsystems, or 0 if SDL2 is unavailable.</returns>
    public static uint SDL_WasInit(SDLInitFlags flags) => s_sdl_wasInit is null ? 0u : s_sdl_wasInit(flags);
}

/// <summary>
/// Identifies the SDL2 subsystems that can be initialized.
/// </summary>
/// <remarks>
/// Only <see cref="GameController"/> is used by this package. It pulls in the joystick and event
/// subsystems on its own and, importantly, initializes no video subsystem: SDL2 never creates a
/// window, opens an X11 or Wayland display, or touches Win32 or AppKit. That is what allows one
/// implementation to serve every CodeBrix.Platform head without contending with the platform for
/// the display connection.
/// </remarks>
[Flags] //for CodeBrix: upstream declared this as a plain enum despite the values being flags
public enum SDLInitFlags : uint
{
    /// <summary>No subsystems.</summary>
    None = 0x00000000u, //for CodeBrix: not in upstream

    /// <summary>The timer subsystem.</summary>
    Timer = 0x00000001u,

    /// <summary>The audio subsystem.</summary>
    Audio = 0x00000010u,

    /// <summary>The video subsystem. Not used by this package.</summary>
    Video = 0x00000020u,

    /// <summary>The joystick subsystem.</summary>
    Joystick = 0x00000200u,

    /// <summary>The haptic (force feedback) subsystem.</summary>
    Haptic = 0x00001000u,

    /// <summary>The game controller subsystem. Implies <see cref="Joystick"/>.</summary>
    GameController = 0x00002000u,
}
