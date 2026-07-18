using System;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A lightweight reader over a <see cref="CachedSound"/>'s shared sample array — create one
/// per play. Its <see cref="Read"/> is real-time-safe: it copies from the preallocated array
/// with no allocation, no lock, and no I/O, so it can be pulled directly on the audio
/// callback thread.
/// </summary>
public sealed class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _sound;
    private int _position;

    /// <summary>Creates a reader positioned at the start of the given sound.</summary>
    /// <param name="sound">The cached sound to read.</param>
    public CachedSoundSampleProvider(CachedSound sound)
    {
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
    }

    /// <summary>The format of the samples this provider produces.</summary>
    public WaveFormat WaveFormat => _sound.WaveFormat;

    /// <summary>The current read position, in samples (not frames), from the start of the sound.</summary>
    public int SamplePosition => _position;

    /// <summary>Rewinds the reader to the start of the sound.</summary>
    public void Reset() => _position = 0;

    /// <summary>
    /// Copies the next samples into <paramref name="buffer"/>; returns 0 at the end of the
    /// sound (which ends the voice and raises its <c>PlaybackStopped</c>).
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <returns>The number of samples written.</returns>
    public int Read(Span<float> buffer)
    {
        int available = _sound.AudioData.Length - _position;
        int count = Math.Min(available, buffer.Length);
        if (count <= 0)
        {
            return 0;
        }

        _sound.AudioData.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }
}
