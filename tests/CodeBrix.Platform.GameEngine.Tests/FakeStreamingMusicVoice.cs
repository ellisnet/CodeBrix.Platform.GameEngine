using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// An <see cref="IStreamingMusicVoice"/> with no device behind it: it records what the track asks of
/// it, and <see cref="Pump"/> pulls the track's stream the way the audio thread would — only while
/// the voice is playing.
/// </summary>
internal sealed class FakeStreamingMusicVoice : IStreamingMusicVoice
{
    internal FakeStreamingMusicVoice(ISampleProvider stream) => Stream = stream;

    internal ISampleProvider Stream { get; }

    public float Volume { get; set; } = 1f;

    public bool? SuspendOnEnginePause { get; set; }

    public bool IsPlaying { get; private set; }

    internal int StartCalls { get; private set; }

    internal int StopCalls { get; private set; }

    internal bool Disposed { get; private set; }

    public void Start()
    {
        StartCalls++;
        IsPlaying = !Disposed;
    }

    public void Stop()
    {
        StopCalls++;
        IsPlaying = false;
    }

    public void Dispose()
    {
        Disposed = true;
        IsPlaying = false;
    }

    /// <summary>
    /// Pulls <paramref name="frames"/> frames through the stream, as the device would. A voice that is
    /// not playing is not pulled, so the result is empty.
    /// </summary>
    internal float[] Pump(int frames)
    {
        if (!IsPlaying)
        {
            return Array.Empty<float>();
        }

        var buffer = new float[frames * Stream.WaveFormat.Channels];
        Array.Fill(buffer, float.NaN); // anything the track fails to write shows up
        var read = Stream.Read(buffer);
        read.Should().Be(buffer.Length, "a stream always fills the whole buffer");
        return buffer;
    }
}
