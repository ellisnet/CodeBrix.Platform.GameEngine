using System;

namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

/// <summary>
/// Identifies why SDL2 gamepad support could not be started.
/// </summary>
/// <remarks>
/// This is the machine-readable counterpart to <see cref="SdlGamepadManager.UnavailableReason"/>.
/// Branch on this; show the reason text to the player.
/// </remarks>
public enum SdlGamepadUnavailableCause
{
    /// <summary>
    /// Gamepad support started successfully. No failure occurred.
    /// </summary>
    None = 0,

    /// <summary>
    /// The SDL2 native library could not be found or loaded.
    /// </summary>
    /// <remarks>
    /// On Linux this normally means the SDL2 runtime package is not installed, which the player can
    /// fix. On Windows and macOS the library ships inside the application package, so this instead
    /// indicates a packaging problem and is worth reporting as a bug.
    /// </remarks>
    NativeLibraryMissing = 1,

    /// <summary>
    /// SDL2 loaded, but initializing its game controller subsystem failed.
    /// </summary>
    SubsystemInitializationFailed = 2,
}
