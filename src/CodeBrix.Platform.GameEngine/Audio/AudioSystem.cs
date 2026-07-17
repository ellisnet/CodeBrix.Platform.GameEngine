using System;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Engine-level audio device control for games that mix raw-PCM sound effects and streamed
/// music: pins the shared audio output to one fixed device format so every voice can
/// rate-convert to a single known target.
/// </summary>
/// <remarks>
/// <para>
/// Initialization is strictly OPT-IN — the engine never pins the device by itself. Apps that
/// never call <see cref="Initialize"/> keep the default CodeBrix.Audio behavior, where the
/// first sound played sets the device rate.
/// </para>
/// <para>
/// Apps that do pin must route PCM whose rate differs from the device rate through a
/// rate-converting stage such as <see cref="VariableRateSampleProvider"/> — CodeBrix.Audio
/// itself has no resampler, and initializing a voice with an odd-rate source throws.
/// <see cref="SoundChannel"/> and (while <see cref="IsInitialized"/> is true)
/// <see cref="AudioResource"/> insert that stage automatically.
/// </para>
/// </remarks>
public static class AudioSystem
{
    private static readonly object _gate = new();

    /// <summary>True after a successful <see cref="Initialize"/> call.</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>The pinned device sample rate in Hz; 0 until <see cref="Initialize"/> is called.</summary>
    public static int DeviceSampleRate { get; private set; }

    /// <summary>The pinned device channel count; 0 until <see cref="Initialize"/> is called.</summary>
    public static int DeviceChannels { get; private set; }

    /// <summary>
    /// Pins the shared audio output to the given format. Call once at startup, before any
    /// sound plays; throws if the shared output is already running with a different format.
    /// </summary>
    /// <param name="sampleRate">The device sample rate in Hz. Default 44100.</param>
    /// <param name="channels">The device channel count. Default 2 (stereo).</param>
    public static void Initialize(int sampleRate = 44100, int channels = 2)
    {
        lock (_gate)
        {
            SharedAudioOutput.Configure(sampleRate, channels);
            DeviceSampleRate = sampleRate;
            DeviceChannels = channels;
            IsInitialized = true;
        }
    }

    /// <summary>
    /// Shuts the shared audio output down (stopping every voice) and un-pins the device
    /// format, so <see cref="Initialize"/> can be called again with a different format.
    /// </summary>
    public static void Shutdown()
    {
        lock (_gate)
        {
            SharedAudioOutput.Shutdown();
            DeviceSampleRate = 0;
            DeviceChannels = 0;
            IsInitialized = false;
        }
    }
}
