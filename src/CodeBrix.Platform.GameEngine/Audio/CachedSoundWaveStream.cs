using System;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A seekable <see cref="WaveStream"/> view over a <see cref="CachedSound"/>'s decoded float
/// PCM, so preloaded sounds can flow through the engine's existing
/// <see cref="AudioResource"/> graph (position, seek, duration) unchanged. Many streams can
/// share one <see cref="CachedSound"/> — each keeps only its own position.
/// </summary>
internal sealed class CachedSoundWaveStream : WaveStream
{
    private readonly CachedSound _sound;
    private long _positionBytes;

    internal CachedSoundWaveStream(CachedSound sound)
    {
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
    }

    /// <summary>The cached sound this stream reads from.</summary>
    internal CachedSound Sound => _sound;

    /// <inheritdoc />
    public override WaveFormat WaveFormat => _sound.WaveFormat;

    /// <inheritdoc />
    public override long Length => _sound.AudioData.Length * 4L;

    /// <inheritdoc />
    public override long Position
    {
        get => _positionBytes;
        set
        {
            long clamped = Math.Clamp(value, 0L, Length);
            _positionBytes = clamped - (clamped % BlockAlign);
        }
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        long availableBytes = Length - _positionBytes;
        int count = (int)Math.Min(availableBytes, buffer.Length);
        count -= count % 4;
        if (count <= 0)
        {
            return 0;
        }

        var floats = _sound.AudioData.AsSpan((int)(_positionBytes / 4), count / 4);
        MemoryMarshal.AsBytes(floats).CopyTo(buffer.Slice(0, count));
        _positionBytes += count;
        return count;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));
}
