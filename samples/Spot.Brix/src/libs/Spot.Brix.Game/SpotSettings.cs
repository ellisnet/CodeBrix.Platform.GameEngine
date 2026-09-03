using System;
using System.IO;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Configuration;

namespace Spot.Brix;

/// <summary>
/// The player's option choices, persisted in the "spot" section of the engine configuration file
/// (<c>gameengine.json</c>) so they survive a restart.
/// </summary>
/// <remarks>
/// Every setter writes the whole file straight away: there are five toggles and they change at human
/// speed, so nothing is gained by batching the writes and a crash never loses a choice. Values are
/// stored as the strings <c>"true"</c> and <c>"false"</c>, which is what
/// <see cref="EngineConfiguration.GetConfigurationValue"/> deals in.
/// </remarks>
public sealed class SpotSettings
{
    /// <summary>The configuration section this sample keeps its options in.</summary>
    public const string SectionName = "spot";

    /// <summary>
    /// The full path of the engine configuration file the sample reads and writes. It is pinned to the
    /// folder the executable lives in: the engine's default is a relative path, which would put the
    /// options wherever the process happened to be started from.
    /// </summary>
    public static string ConfigFilePath { get; } = Path.Combine(AppContext.BaseDirectory, "gameengine.json");

    private const string KeyMusic = "music";
    private const string KeySoundEffects = "soundEffects";
    private const string KeyJiggle = "jiggle";
    private const string KeyClouds = "clouds";
    private const string KeyGpuAcceleration = "gpuAcceleration";

    private readonly EngineConfigurationFile _configFile;

    private SpotSettings(EngineConfigurationFile configFile)
    {
        _configFile = configFile;
    }

    /// <summary>
    /// Reads the saved options from the engine configuration file. A missing or unreadable file yields
    /// the defaults rather than an error, so a first run behaves like a run with everything switched on.
    /// </summary>
    /// <returns>The loaded settings.</returns>
    public static SpotSettings Load() => new(EngineConfigurationFile.Load(ConfigFilePath));

    /// <summary>Gets or sets whether the background music plays.</summary>
    public bool MusicEnabled
    {
        get => GetBool(KeyMusic, defaultValue: true);
        set => SetBool(KeyMusic, value);
    }

    /// <summary>Gets or sets whether the short sound effects play.</summary>
    public bool SoundEffectsEnabled
    {
        get => GetBool(KeySoundEffects, defaultValue: true);
        set => SetBool(KeySoundEffects, value);
    }

    /// <summary>Gets or sets whether the current player's spots jiggle.</summary>
    public bool JiggleEnabled
    {
        get => GetBool(KeyJiggle, defaultValue: true);
        set => SetBool(KeyJiggle, value);
    }

    /// <summary>Gets or sets whether clouds drift across the background.</summary>
    public bool CloudsEnabled
    {
        get => GetBool(KeyClouds, defaultValue: true);
        set => SetBool(KeyClouds, value);
    }

    /// <summary>
    /// Gets or sets whether the game surface renders on the GPU. The render tier is fixed when the
    /// canvas is first used, so a change only takes effect the next time the sample is started.
    /// </summary>
    public bool GpuAccelerationEnabled
    {
        get => GetBool(KeyGpuAcceleration, defaultValue: false);
        set => SetBool(KeyGpuAcceleration, value);
    }

    private bool GetBool(string key, bool defaultValue)
    {
        var raw = _configFile.EngineConfig.GetConfigurationValue(SectionName, key, defaultValue ? "true" : "false");

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void SetBool(string key, bool value)
    {
        _configFile.EngineConfig.SetConfigurationValue(SectionName, key, value ? "true" : "false");
        _configFile.Save();

        // Keep the running engine's own copy of the configuration in step with the file.
        if (Engine.Instance.IsInitialized)
            Engine.Instance.Configuration.SetConfigurationValue(SectionName, key, value ? "true" : "false");
    }
}
