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
/// <para>
/// The channel count passes through unchanged; only the rate (and effective playback speed,
/// via <see cref="Pitch"/>) is converted. <see cref="Read"/> is driven from the audio
/// callback thread and performs no allocations after construction.
/// </para>
/// <para>
/// At unity — the source already at the output rate with <see cref="Pitch"/> 1.0 — the stage
/// costs nothing: the source writes straight into the caller's buffer, so the samples are
/// bit-identical, no frame is read before it is needed, and the final frame is handed over like
/// any other. That is what lets a voice carry this stage permanently and vary its speed at any
/// time, instead of being rebuilt when the speed changes.
/// </para>
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

    /// <summary>
    /// Discards the frames this stage has buffered and clears its end-of-source latch, so the
    /// next <see cref="Read"/> starts again from wherever the source now is.
    /// </summary>
    /// <remarks>
    /// Call this whenever the SOURCE is repositioned — a rewind, a seek, or a loop restart.
    /// Without it the stage keeps serving the frames it read before the reposition and, once the
    /// source has run dry, never asks it for another sample: a looping voice would then stop and
    /// restart forever without producing sound. Call it while the voice is stopped or paused; it
    /// is not safe against a concurrent <see cref="Read"/> on the audio thread.
    /// </remarks>
    internal void Reset()
    {
        _sourceFramesValid = 0;
        _sourceFrameIndex = 0;
        _sourceEnded = false;
    }

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        var framesRequested = buffer.Length / _channels;
        if (framesRequested <= 0)
        {
            return 0;
        }

        var step = _source.WaveFormat.SampleRate * (double)_pitch / WaveFormat.SampleRate;

        return step == 1.0
            ? ReadAtUnity(buffer, framesRequested) * _channels
            : ReadResampled(buffer, framesRequested, step) * _channels;
    }

    /// <summary>
    /// The unity path: the source already runs at the output rate and <see cref="Pitch"/> is 1.0,
    /// so nothing needs converting. Anything the resampling path left buffered is handed over
    /// first, and after that the source fills the caller's buffer directly — which keeps the
    /// samples bit-identical, reads no further ahead than the caller asked for, and hands over the
    /// final frame like any other.
    /// </summary>
    private int ReadAtUnity(Span<float> buffer, int framesRequested)
    {
        var framesWritten = DrainBufferedFrames(buffer, framesRequested);

        // Only once the buffer is empty can the source write straight into the caller's span;
        // until then the frames already read from it have to come first, in order.
        while (framesWritten < framesRequested && _sourceFramesValid == 0 && !_sourceEnded)
        {
            var samplesRead = _source.Read(
                buffer.Slice(framesWritten * _channels, (framesRequested - framesWritten) * _channels));
            if (samplesRead <= 0)
            {
                _sourceEnded = true;
                break;
            }

            var framesRead = samplesRead / _channels;
            if (framesRead == 0)
            {
                break; // a partial frame: this source is not frame aligned, so stop here
            }

            framesWritten += framesRead;
        }

        return framesWritten;
    }

    /// <summary>
    /// Hands back the frames the resampling path left in the buffer, one output frame per source
    /// frame, so a switch to the unity path neither skips nor repeats a frame. Once the buffer is
    /// empty it is released to the unity path along with any sub-frame phase the previous rate
    /// left behind — a fraction of one frame, not a frame.
    /// </summary>
    private int DrainBufferedFrames(Span<float> buffer, int framesRequested)
    {
        var framesWritten = 0;

        while (framesWritten < framesRequested)
        {
            var frame = (int)_sourceFrameIndex;
            if (frame >= _sourceFramesValid)
            {
                break;
            }

            WriteInterpolatedFrame(buffer, framesWritten, frame);
            framesWritten++;
            _sourceFrameIndex += 1.0;
        }

        if ((int)_sourceFrameIndex >= _sourceFramesValid)
        {
            _sourceFramesValid = 0;
            _sourceFrameIndex = 0;
        }

        return framesWritten;
    }

    /// <summary>
    /// The resampling path: the source rate, the output rate and <see cref="Pitch"/> do not
    /// cancel out, so each output frame is interpolated at its own fractional source position.
    /// </summary>
    private int ReadResampled(Span<float> buffer, int framesRequested, double step)
    {
        var framesWritten = 0;

        while (framesWritten < framesRequested)
        {
            var frame = (int)_sourceFrameIndex;
            if (frame + 1 >= _sourceFramesValid && !TryRefill(ref frame) && frame >= _sourceFramesValid)
            {
                break;
            }

            // Past the end of the source the current frame has no partner to interpolate
            // towards, so it holds its own value rather than being dropped.
            WriteInterpolatedFrame(buffer, framesWritten, (int)_sourceFrameIndex);
            framesWritten++;
            _sourceFrameIndex += step;
        }

        return framesWritten;
    }

    private void WriteInterpolatedFrame(Span<float> buffer, int outputFrame, int sourceFrame)
    {
        var fraction = (float)(_sourceFrameIndex - sourceFrame);
        var sourceOffset = sourceFrame * _channels;
        var outputOffset = outputFrame * _channels;
        var hasPartner = sourceFrame + 1 < _sourceFramesValid;

        for (var channel = 0; channel < _channels; channel++)
        {
            var sample0 = _sourceBuffer[sourceOffset + channel];
            var sample1 = hasPartner ? _sourceBuffer[sourceOffset + _channels + channel] : sample0;
            buffer[outputOffset + channel] = sample0 + (sample1 - sample0) * fraction;
        }
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
