using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A short sound effect decoded ONCE into raw float PCM in memory, so it can be played many
/// times (and by many overlapping voices) with zero per-play decode, file, or allocation cost
/// on the real-time audio thread. This is the classic preload-to-PCM pattern for rapid-fire
/// game sound effects.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="CachedSound"/> holds the decoded samples; any number of
/// <see cref="CachedSoundSampleProvider"/> readers (one per play) share that single array.
/// <see cref="AudioResourceManager"/> preloads qualifying short effects automatically (see
/// <see cref="AudioResourceManager.PreloadShortSoundEffectMaxSeconds"/>) and
/// <see cref="SfxVoicePool"/> plays them; these constructors exist for games that manage
/// cached sounds directly.
/// </para>
/// <para>
/// When the app has pinned the device format (<see cref="AudioSystem.Initialize"/>) and the
/// source's sample rate differs, decoding rate-converts to the device rate up front — so
/// playback never needs a conversion stage. Reserve streaming readers for long,
/// single-instance material (music, ambience); preloading those would hold minutes of raw
/// PCM in memory.
/// </para>
/// </remarks>
public sealed class CachedSound
{
    /// <summary>
    /// The decoded samples: 32-bit float PCM, interleaved when multi-channel.
    /// Treat as read-only — every reader over this sound shares this one array.
    /// </summary>
    public float[] AudioData { get; }

    /// <summary>The format of <see cref="AudioData"/> (IEEE float, decoded rate and channels).</summary>
    public WaveFormat WaveFormat { get; }

    /// <summary>The sample rate of the decoded data in Hz.</summary>
    public int SampleRate => WaveFormat.SampleRate;

    /// <summary>The channel count of the decoded data.</summary>
    public int Channels => WaveFormat.Channels;

    /// <summary>The total duration of the decoded sound.</summary>
    public TimeSpan Duration =>
        TimeSpan.FromSeconds(AudioData.Length / (double)(SampleRate * Channels));

    /// <summary>
    /// Decodes the given sample source to the end into memory. The caller keeps ownership of
    /// the source (dispose it afterwards if it is disposable).
    /// </summary>
    /// <param name="source">The sample source to decode; it is read to the end.</param>
    public CachedSound(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var provider = source;
        if (AudioSystem.IsInitialized && provider.WaveFormat.SampleRate != AudioSystem.DeviceSampleRate)
        {
            // The app pinned the device rate and this source differs — convert once, at
            // decode time, so no per-play conversion stage is ever needed (CodeBrix.Audio
            // has no resampler; an odd-rate voice would be rejected at Init).
            provider = new VariableRateSampleProvider(provider, AudioSystem.DeviceSampleRate);
        }

        var samples = new List<float>();
        var chunk = new float[provider.WaveFormat.SampleRate * provider.WaveFormat.Channels];
        int read;
        while ((read = provider.Read(chunk)) > 0)
        {
            samples.AddRange(chunk.AsSpan(0, read));
        }

        AudioData = samples.ToArray();
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            provider.WaveFormat.SampleRate, provider.WaveFormat.Channels);
    }

    /// <summary>
    /// Decodes the given wave stream to the end into memory. The caller keeps ownership of
    /// the stream (dispose it afterwards).
    /// </summary>
    /// <param name="source">The wave stream to decode; it is read to the end.</param>
    public CachedSound(WaveStream source)
        : this((source ?? throw new ArgumentNullException(nameof(source))).ToSampleProvider())
    { }

    /// <summary>
    /// Loads and decodes an audio file into memory in one step, using the same format
    /// registry as <see cref="AudioResourceManager"/> (<see cref="PlatformAudioFactory"/>).
    /// </summary>
    /// <param name="filePath">The path to the audio file on disk.</param>
    /// <returns>The decoded sound.</returns>
    public static CachedSound FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var (readerFactory, _) = PlatformAudioFactory.GetReaderFactory(filePath);
        using var fileStream = File.OpenRead(filePath);
        using var reader = readerFactory(fileStream);
        return new CachedSound(reader);
    }
}
