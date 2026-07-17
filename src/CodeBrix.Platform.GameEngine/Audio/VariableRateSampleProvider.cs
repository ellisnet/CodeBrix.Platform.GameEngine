using System;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Converts a sample source of any rate to a fixed output rate by linear interpolation,
/// applying an adjustable <see cref="Pitch"/> multiplier in the same pass. This is the
/// engine's generic rate/pitch stage for classic game audio — e.g. 8-bit 7–11 kHz sound
/// effect lumps played on a 44.1 kHz device with optional random pitch variation.
/// </summary>
/// <remarks>
/// The channel count passes through unchanged; only the rate (and effective playback speed,
/// via <see cref="Pitch"/>) is converted. <see cref="Read"/> is driven from the audio
/// callback thread and performs no allocations after construction.
/// </remarks>
public sealed class VariableRateSampleProvider : ISampleProvider
{
    private const int SourceBufferFrames = 2048;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly float[] _sourceBuffer;
    private int _sourceFramesValid;
    private double _sourceFrameIndex;
    private bool _sourceEnded;
    private float _pitch = 1f;

    /// <summary>
    /// Creates a provider that reads <paramref name="source"/> (at its own rate) and
    /// produces samples at <paramref name="outputSampleRate"/>.
    /// </summary>
    public VariableRateSampleProvider(ISampleProvider source, int outputSampleRate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (outputSampleRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate), outputSampleRate, "The output sample rate must be positive.");
        }

        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, _channels);
        _sourceBuffer = new float[(SourceBufferFrames + 1) * _channels];
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// The pitch multiplier: 1.0 leaves the sound unchanged, 2.0 plays an octave higher and
    /// twice as fast (classic sample-rate pitching, not time-stretching). Clamped to
    /// 0.05–20.0; may be changed while playing.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, 0.05f, 20f);
    }

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        var framesRequested = buffer.Length / _channels;
        var framesWritten = 0;
        var step = _source.WaveFormat.SampleRate * (double)_pitch / WaveFormat.SampleRate;

        while (framesWritten < framesRequested)
        {
            var frame = (int)_sourceFrameIndex;
            if (frame + 1 >= _sourceFramesValid && !TryRefill(ref frame))
            {
                break;
            }

            var fraction = (float)(_sourceFrameIndex - frame);
            var sourceOffset = frame * _channels;
            var outputOffset = framesWritten * _channels;
            for (var channel = 0; channel < _channels; channel++)
            {
                var sample0 = _sourceBuffer[sourceOffset + channel];
                var sample1 = _sourceBuffer[sourceOffset + _channels + channel];
                buffer[outputOffset + channel] = sample0 + (sample1 - sample0) * fraction;
            }

            framesWritten++;
            _sourceFrameIndex += step;
        }

        return framesWritten * _channels;
    }

    private bool TryRefill(ref int frame)
    {
        if (_sourceEnded)
        {
            return false;
        }

        // Discard the fully consumed frames, keeping the current (possibly partial) frame so
        // the interpolation pair stays available across the refill.
        var discardFrames = Math.Min(frame, _sourceFramesValid);
        if (discardFrames > 0)
        {
            var keepSamples = (_sourceFramesValid - discardFrames) * _channels;
            if (keepSamples > 0)
            {
                Array.Copy(_sourceBuffer, discardFrames * _channels, _sourceBuffer, 0, keepSamples);
            }

            _sourceFramesValid -= discardFrames;
            _sourceFrameIndex -= discardFrames;
            frame -= discardFrames;
        }

        var capacityFrames = _sourceBuffer.Length / _channels;
        while (_sourceFramesValid < capacityFrames)
        {
            var read = _source.Read(_sourceBuffer.AsSpan(
                _sourceFramesValid * _channels,
                (capacityFrames - _sourceFramesValid) * _channels));
            if (read == 0)
            {
                _sourceEnded = true;
                break;
            }

            _sourceFramesValid += read / _channels;
        }

        return frame + 1 < _sourceFramesValid;
    }
}
