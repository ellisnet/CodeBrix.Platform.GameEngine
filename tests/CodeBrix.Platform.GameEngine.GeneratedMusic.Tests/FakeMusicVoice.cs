using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// An engine music voice with no audio device behind it: <see cref="Pump"/> pulls the track's stream
/// the way the audio thread would, and only while the voice is playing. Installed through the
/// engine's internal voice factory, so no test here ever opens a device.
/// </summary>
internal sealed class FakeMusicVoice : IStreamingMusicVoice
{
    internal FakeMusicVoice(ISampleProvider stream) => Stream = stream;

    internal ISampleProvider Stream { get; }

    public float Volume { get; set; } = 1f;

    public bool? SuspendOnEnginePause { get; set; }

    public bool IsPlaying { get; private set; }

    internal bool Disposed { get; private set; }

    public void Start() => IsPlaying = !Disposed;

    public void Stop() => IsPlaying = false;

    public void Dispose()
    {
        Disposed = true;
        IsPlaying = false;
    }

    /// <summary>
    /// Pulls <paramref name="frames"/> frames through the stream, as the device would; a voice that
    /// is not playing is not pulled and yields nothing.
    /// </summary>
    internal float[] Pump(int frames)
    {
        if (!IsPlaying) { return Array.Empty<float>(); }

        var buffer = new float[frames * Stream.WaveFormat.Channels];
        Stream.Read(buffer);
        return buffer;
    }
}
