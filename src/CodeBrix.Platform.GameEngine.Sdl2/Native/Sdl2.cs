// Vendored from Veldrid -- https://github.com/veldrid/veldrid  (src/Veldrid.SDL2/Sdl2.cs)
// The MIT License (MIT). Copyright (c) 2017 Eric Mellino and Veldrid contributors.
// See THIRD-PARTY-NOTICES.txt in the repository root for the full license text.
//
// Changes from upstream, all marked inline with "for CodeBrix":
//   * namespace changed, block namespace converted to file-scoped
//   * the NativeLibraryLoader package dependency removed; library loading and function
//     resolution now go through Sdl2Library, which never throws
//   * GetErrorString() added (a safe wrapper over the byte* SDL_GetError)

using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Native; //was previously: Veldrid.Sdl2;

/// <summary>
/// Direct bindings to the SDL2 native library.
/// </summary>
/// <remarks>
/// Only the joystick and game controller subsystems are bound. Windowing, rendering, audio,
/// keyboard and mouse entry points are deliberately absent -- CodeBrix.Platform owns all of those,
/// and SDL2 is present here purely as a headless gamepad backend.
/// </remarks>
public static unsafe partial class Sdl2Native
{
    /// <summary>
    /// Loads an SDL2 function by the given name.
    /// </summary>
    /// <typeparam name="T">The delegate type of the function to load.</typeparam>
    /// <param name="name">The name of the exported native function.</param>
    /// <returns>
    /// A delegate which can be used to invoke the native function, or <see langword="null"/> if
    /// SDL2 is unavailable or exports no such function.
    /// </returns>
    /// <remarks>
    /// Upstream logged a debug message and returned <c>default</c> when an individual function was
    /// missing, but threw from the type initializer when the library itself was missing. For
    /// CodeBrix both cases return <see langword="null"/> instead, so that an absent SDL2 can be
    /// reported as "no gamepad support" rather than taking down the game. Callers must therefore
    /// check <see cref="Sdl2Library.IsLoaded"/> before invoking any binding.
    /// </remarks>
    public static T? LoadFunction<T>(string name) => Sdl2Library.GetFunction<T>(name); //for CodeBrix: was s_sdl2Lib.LoadFunction<T>(name)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte* SDL_GetError_t();
    private static readonly SDL_GetError_t? s_sdl_getError = LoadFunction<SDL_GetError_t>("SDL_GetError");

    /// <summary>
    /// Retrieves a message about the last error that occurred on the current thread.
    /// </summary>
    /// <returns>A pointer to a null-terminated UTF-8 string, or <see langword="null"/>.</returns>
    public static byte* SDL_GetError() => s_sdl_getError is null ? null : s_sdl_getError();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_ClearError_t();
    private static readonly SDL_ClearError_t? s_sdl_clearError = LoadFunction<SDL_ClearError_t>("SDL_ClearError");

    /// <summary>
    /// Clears any previous error message for the current thread.
    /// </summary>
    /// <returns>Always <see langword="null"/>; the return value exists only to match upstream.</returns>
    public static byte* SDL_ClearError() { s_sdl_clearError?.Invoke(); return null; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_free_t(void* ptr);
    private static readonly SDL_free_t? s_sdl_free = LoadFunction<SDL_free_t>("SDL_free");

    /// <summary>
    /// Frees memory that was allocated by SDL2.
    /// </summary>
    /// <param name="ptr">The pointer to free.</param>
    public static void SDL_free(void* ptr) { s_sdl_free?.Invoke(ptr); }

    /// <summary>
    /// Gets the last SDL2 error as a managed string. //for CodeBrix: not in upstream
    /// </summary>
    /// <returns>
    /// The error text, or an empty string when SDL2 is unavailable or has recorded no error.
    /// </returns>
    /// <remarks>
    /// A convenience wrapper so that callers outside this file do not need an unsafe context
    /// just to report a failure.
    /// </remarks>
    public static string GetErrorString()
    {
        byte* error = SDL_GetError();
        return error is null ? string.Empty : (Marshal.PtrToStringUTF8((IntPtr)error) ?? string.Empty);
    }
}
