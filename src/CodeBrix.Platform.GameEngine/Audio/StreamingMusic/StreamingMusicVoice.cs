using System;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// The output a <see cref="StreamingMusicTrack"/> plays through. In a game it is a
/// <see cref="StreamingAudioSource"/> (<see cref="StreamingAudioSourceVoice"/>); the seam exists so
/// the track's own logic can be exercised without opening an audio device.
/// </summary>
internal interface IStreamingMusicVoice : IDisposable
{
    /// <summary>The track's own level, before the music bus, ducking and master volume.</summary>
    float Volume { get; set; }

    /// <summary>The engine-pause override passed through to the underlying voice.</summary>
    bool? SuspendOnEnginePause { get; set; }

    /// <summary>Whether the output is currently pulling and playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Starts (or resumes) pulling.</summary>
    void Start();

    /// <summary>Stops pulling; <see cref="Start"/> resumes.</summary>
    void Stop();
}

/// <summary>
/// The real <see cref="IStreamingMusicVoice"/>: a <see cref="StreamingAudioSource"/> on
/// <see cref="AudioBus.Music"/>, so the music slider, ducking and the global engine pause all apply
/// exactly as they do to every other music voice.
/// </summary>
internal sealed class StreamingAudioSourceVoice : IStreamingMusicVoice
{
    /// <summary>Creates the voice over the track's sample stream.</summary>
    /// <param name="stream">The track's endless, interleaved stream at the output format.</param>
    internal StreamingAudioSourceVoice(ISampleProvider stream)
    {
        Source = new StreamingAudioSource(stream)
        {
            Bus = AudioBus.Music,
        };
    }

    /// <summary>The underlying source, for tests that check the bus and pause wiring.</summary>
    internal StreamingAudioSource Source { get; }

    /// <inheritdoc/>
    public float Volume
    {
        get => Source.Volume;
        set => Source.Volume = value;
    }

    /// <inheritdoc/>
    public bool? SuspendOnEnginePause
    {
        get => Source.SuspendOnEnginePause;
        set => Source.SuspendOnEnginePause = value;
    }

    /// <inheritdoc/>
    public bool IsPlaying => Source.IsPlaying;

    /// <inheritdoc/>
    public void Start() => Source.Start();

    /// <inheritdoc/>
    public void Stop() => Source.Stop();

    /// <inheritdoc/>
    public void Dispose() => Source.Dispose();
}
