using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
using Microsoft.Extensions.Logging;
using System;

namespace CodeBrix.Platform.GameEngine.Sdl2;

/// <summary>
/// Adds SDL2 gamepad support to a <see cref="Engine"/> instance.
/// </summary>
/// <remarks>
/// This is the entry point for the package. Everything else can be ignored by a game that just
/// wants controllers to work.
/// </remarks>
public static class EngineGamepadExtensions
{
    /// <summary>
    /// Starts SDL2 gamepad support and attaches it to the engine.
    /// </summary>
    /// <param name="engine">The engine to attach gamepad support to.</param>
    /// <param name="logStatus">
    /// <see langword="true"/> to write the outcome to the engine log once, which is usually what is
    /// wanted; <see langword="false"/> to stay silent and inspect the returned manager instead.
    /// </param>
    /// <returns>
    /// The manager that was attached. It is returned even when gamepad support is unavailable, in
    /// which case <see cref="SdlGamepadManager.IsAvailable"/> is <see langword="false"/> and
    /// <see cref="SdlGamepadManager.UnavailableReason"/> explains why.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Call this once, after the engine has been started. From then on the engine polls the manager
    /// every frame; nothing further is required to keep controller state current.
    /// </para>
    /// <para>
    /// This does not throw when SDL2 is missing or no controller is attached. Gamepad support is an
    /// enhancement to a game that is already playable with keyboard and mouse, so its absence is
    /// reported rather than raised.
    /// </para>
    /// <para>
    /// Keep the returned manager if the game has a settings screen: it is what can answer, later
    /// and in words, why enabling gamepad support did not produce a working controller.
    /// </para>
    /// </remarks>
    public static SdlGamepadManager InitializeSdlGamepadManager(this Engine engine, bool logStatus = true)
    {
        ArgumentNullException.ThrowIfNull(engine);

        bool available = SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        engine.Input.GamepadManager = manager;

        if (logStatus)
        {
            LogStatus(manager, available);
        }

        return manager;
    }

    private static void LogStatus(SdlGamepadManager manager, bool available)
    {
        ILogger<Engine> logger = Engine.Logger;

        if (!available)
        {
            logger.LogWarning("SDL2 gamepad support unavailable ({Cause}): {Reason}",
                manager.UnavailableCause, manager.UnavailableReason);
            return;
        }

        if (manager.ConnectedAdapters.Count == 0)
        {
            logger.LogInformation("SDL2 gamepad support started. {Hint}", manager.GetNoControllersHint());
            return;
        }

        foreach (SdlGamepadAdapter adapter in manager.ConnectedAdapters)
        {
            // The mapping string is logged deliberately. It is what reconciles a specific device's
            // raw button and axis numbering with the standard layout, and it varies by transport -
            // the same pad reports a different raw layout over Bluetooth than over USB - so having
            // it in the log turns "the buttons are wrong" into a question that can be answered.
            logger.LogInformation("SDL2 gamepad connected: {Name} (id {GamepadId}); mapping: {Mapping}",
                adapter.Name, adapter.GamepadId, adapter.GetMappingString());
        }
    }
}
