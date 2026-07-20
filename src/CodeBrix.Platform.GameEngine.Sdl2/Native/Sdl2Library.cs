using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CodeBrix.Platform.GameEngine.Sdl2.Native;

/// <summary>
/// Locates and opens the SDL2 native library, and resolves exported functions from it.
/// </summary>
/// <remarks>
/// <para>
/// This type replaces the third-party <c>NativeLibraryLoader</c> package that the upstream
/// Veldrid.SDL2 bindings used, so that the binding layer carries no NuGet dependencies at all.
/// It is built entirely on <see cref="NativeLibrary"/>, which has been part of the shared
/// framework since .NET Core 3.0.
/// </para>
/// <para>
/// <b>This type never throws.</b> That is a deliberate divergence from upstream. Veldrid assigned
/// its library handle from a static field initializer that threw when SDL2 could not be found,
/// which meant a missing SDL2 surfaced as a <see cref="TypeInitializationException"/> from the
/// first unrelated member touched on the bindings class. For a game that is perfectly playable
/// with keyboard and mouse, a missing optional gamepad library must not be fatal. Here a failed
/// load is recorded and reported through <see cref="IsLoaded"/> and <see cref="LoadFailureDetail"/>
/// instead, and every resolved function simply comes back <see langword="null"/>.
/// </para>
/// <para>
/// Loading is attempted once and the outcome cached, including failure. Probing a missing library
/// on every call would be wasteful, and a native library does not appear part-way through a
/// process's lifetime.
/// </para>
/// </remarks>
public static class Sdl2Library
{
    private static readonly object Gate = new();

    private static bool _loadAttempted;
    private static IntPtr _handle;
    private static string? _loadedLibraryName;
    private static string? _loadFailureDetail;

    /// <summary>
    /// Gets a value indicating whether the SDL2 native library was found and opened successfully.
    /// </summary>
    /// <remarks>
    /// Accessing this property triggers the one-time load attempt if it has not happened yet.
    /// When this is <see langword="false"/>, <see cref="LoadFailureDetail"/> explains what was tried.
    /// </remarks>
    public static bool IsLoaded
    {
        get
        {
            EnsureLoadAttempted();
            return _handle != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets the file name that SDL2 was actually loaded from, or <see langword="null"/> if the
    /// library could not be loaded.
    /// </summary>
    public static string? LoadedLibraryName
    {
        get
        {
            EnsureLoadAttempted();
            return _loadedLibraryName;
        }
    }

    /// <summary>
    /// Gets a diagnostic description of a failed load - the candidate file names that were probed -
    /// or <see langword="null"/> when the library loaded successfully.
    /// </summary>
    /// <remarks>
    /// This is raw diagnostic detail, not a message to show a player. The user-facing text lives on
    /// the gamepad manager, which pairs this with platform-appropriate advice.
    /// </remarks>
    public static string? LoadFailureDetail
    {
        get
        {
            EnsureLoadAttempted();
            return _loadFailureDetail;
        }
    }

    /// <summary>
    /// Gets the candidate SDL2 file names probed on the current operating system, in probe order.
    /// </summary>
    public static IReadOnlyList<string> GetProbeCandidates() => CandidateNames();

    /// <summary>
    /// Resolves an exported SDL2 function as a delegate of the requested type.
    /// </summary>
    /// <typeparam name="T">
    /// The delegate type describing the native function's signature. This is intentionally
    /// unconstrained so that the vendored binding files can call it exactly as they call the
    /// upstream loader.
    /// </typeparam>
    /// <param name="name">The name of the exported native function, for example <c>SDL_Init</c>.</param>
    /// <returns>
    /// A callable delegate, or <see langword="null"/> when SDL2 is not loaded or does not export
    /// a function with that name.
    /// </returns>
    public static T? GetFunction<T>(string name)
    {
        EnsureLoadAttempted();

        if (_handle == IntPtr.Zero) { return default; }

        if (!NativeLibrary.TryGetExport(_handle, name, out IntPtr address) || address == IntPtr.Zero)
        {
            return default;
        }

        // The non-generic Marshal overload is used on purpose: the generic form requires a
        // "where T : Delegate" constraint, and adding that constraint would mean editing every
        // call site in the vendored binding files.
        return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
    }

    private static void EnsureLoadAttempted()
    {
        if (_loadAttempted) { return; }

        lock (Gate)
        {
            if (_loadAttempted) { return; }

            try
            {
                Load();
            }
            catch (Exception ex)
            {
                // Belt and braces. NativeLibrary.TryLoad is documented not to throw for a missing
                // library, but this type's contract is that it never throws, so nothing escapes.
                _handle = IntPtr.Zero;
                _loadedLibraryName = null;
                _loadFailureDetail = $"Unexpected error while loading SDL2: {ex.Message}";
            }
            finally
            {
                _loadAttempted = true;
            }
        }
    }

    private static void Load()
    {
        IReadOnlyList<string> candidates = CandidateNames();
        Assembly assembly = typeof(Sdl2Library).Assembly;

        foreach (string candidate in candidates)
        {
            // Resolving relative to this assembly is what lets the natives we ship in the package
            // under runtimes/<rid>/native/ be found: that probing path comes from the consuming
            // application's deps.json, keyed by the assembly requesting the load.
            if (NativeLibrary.TryLoad(candidate, assembly, DllImportSearchPath.SafeDirectories, out IntPtr handle)
                && handle != IntPtr.Zero)
            {
                _handle = handle;
                _loadedLibraryName = candidate;
                _loadFailureDetail = null;
                return;
            }
        }

        _handle = IntPtr.Zero;
        _loadedLibraryName = null;
        _loadFailureDetail = $"None of the following could be loaded: {string.Join(", ", candidates)}";
    }

    private static string[] CandidateNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ["SDL2.dll"];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                // First the name we ship in the package, then the casing upstream Veldrid probed,
                // then the versioned and framework layouts a system-installed SDL2 may use.
                "libSDL2.dylib",
                "libsdl2.dylib",
                "libSDL2-2.0.0.dylib",
                "SDL2.framework/Versions/A/SDL2",
            ];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return
            [
                // The versioned soname comes first deliberately. It is what the SDL2 runtime
                // package installs; the unversioned "libSDL2-2.0.so" symlink only exists once the
                // development package is installed, which we do not want to require.
                "libSDL2-2.0.so.0",
                "libSDL2-2.0.so",
                "libSDL2-2.0.so.1",
                "libSDL2.so",
            ];
        }

        return ["SDL2.dll", "libSDL2-2.0.so.0", "libSDL2.dylib"];
    }
}
