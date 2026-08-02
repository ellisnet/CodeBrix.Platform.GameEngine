using System;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Sums a set of decoded stems into ONE voice, with a per-stem gain that can move while it plays.
/// This is what makes layered adaptive music stay in phase.
/// </summary>
/// <remarks>
/// <para>
/// WHY ONE VOICE AND NOT N. Independent voices in a shared mixer are started at slightly different
/// times and drift apart from there, and layers that drift phase against each other — a flam on
/// every downbeat, then a slow flange. Summing into one provider makes the lock structural rather
/// than something the engine has to keep re-establishing: there is one read position, one clock, and
/// every layer is read at the same frame index in the same call.
/// </para>
/// <para>
/// THE STEMS ARE DECODED, NOT STREAMED. Every stem is a <see cref="CachedSound"/> — raw float PCM in
/// memory. That is a deliberate trade: it costs memory (roughly 10 MB per stereo minute at 44.1 kHz)
/// and buys exact looping, seeking that is an index assignment, and a <see cref="Read"/> that
/// allocates nothing and touches no decoder on the audio thread. Adaptive stems are normally short
/// loops layered many times, which is the case this suits; a full-length linear track is a
/// <see cref="FileMusicTrack"/>, which streams.
/// </para>
/// <para>
/// GAIN CHANGES RAMP ACROSS A BLOCK rather than landing on one sample, because a step change in gain
/// is a click. <see cref="SetGain"/> sets a target; each <see cref="Read"/> interpolates from where
/// the previous block ended to that target across the whole buffer.
/// </para>
/// <para>
/// SUMMING IS NOT LIMITED. N stems at full gain sum to N times one stem, and anything past 1.0 clips
/// at the device. Stems are expected to be mixed so that the combinations the game actually uses sum
/// without clipping — the same assumption every stem-based soundtrack makes. Adding a limiter here
/// would quietly change the balance a composer set.
/// </para>
/// </remarks>
internal sealed class StemMixSampleProvider : ISampleProvider
{
    private readonly CachedSound[] _stems;
    private readonly int[] _stemFrames;
    private readonly float[] _targetGain;
    private readonly float[] _currentGain;
    private readonly float[] _blockStartGain;
    private readonly float[] _blockEndGain;
    private readonly int _channels;
    private readonly object _gate = new();

    private long _framePosition;
    private bool _isLooping = true;
    private bool _endRaised;

    /// <summary>
    /// Creates a mixer over already-decoded stems. Every stem must share a sample rate and channel
    /// count; see <see cref="MusicStemSet"/>, which validates that and reports the offender.
    /// </summary>
    /// <param name="stems">The decoded stems, in the order their gains are indexed.</param>
    internal StemMixSampleProvider(CachedSound[] stems)
    {
        _stems = stems;
        _channels = stems[0].Channels;

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(stems[0].SampleRate, _channels);

        _stemFrames = new int[stems.Length];
        _targetGain = new float[stems.Length];
        _currentGain = new float[stems.Length];
        _blockStartGain = new float[stems.Length];
        _blockEndGain = new float[stems.Length];

        var longest = 0;
        for (var i = 0; i < stems.Length; i++)
        {
            _stemFrames[i] = stems[i].AudioData.Length / _channels;
            if (_stemFrames[i] > longest)
            {
                longest = _stemFrames[i];
            }
        }

        // The loop point is the LONGEST stem, so a set whose stems are not quite the same length
        // still loops as one thing. A short stem falls silent until the common loop point rather
        // than wrapping early on its own, which would break exactly the lock this type exists for.
        LoopFrames = longest;
    }

    /// <inheritdoc/>
    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// Called once when a non-looping set plays past its end. Raised from the AUDIO THREAD; it must
    /// not block or touch the device.
    /// </summary>
    internal Action? EndReached;

    /// <summary>The length of the set in frames — the longest stem.</summary>
    internal int LoopFrames { get; }

    /// <summary>Whether the set wraps at <see cref="LoopFrames"/> or falls silent and reports its end.</summary>
    internal bool IsLooping
    {
        get { lock (_gate) { return _isLooping; } }
        set { lock (_gate) { _isLooping = value; } }
    }

    /// <summary>The current read position in frames.</summary>
    internal long FramePosition
    {
        get { lock (_gate) { return _framePosition; } }
    }

    /// <summary>Sets one stem's target gain. The next block ramps to it.</summary>
    /// <param name="index">The stem's index.</param>
    /// <param name="gain">The gain, 0.0 to 1.0.</param>
    internal void SetGain(int index, float gain)
    {
        lock (_gate)
        {
            _targetGain[index] = Math.Clamp(gain, 0f, 1f);
        }
    }

    /// <summary>Gets one stem's target gain.</summary>
    /// <param name="index">The stem's index.</param>
    /// <returns>The gain, 0.0 to 1.0.</returns>
    internal float GetGain(int index)
    {
        lock (_gate)
        {
            return _targetGain[index];
        }
    }

    /// <summary>Moves the read position, re-arming the end report.</summary>
    /// <param name="frame">The frame to move to; clamped into the set.</param>
    internal void SeekFrames(long frame)
    {
        lock (_gate)
        {
            _framePosition = Math.Clamp(frame, 0, LoopFrames);
            _endRaised = false;
        }
    }

    /// <inheritdoc/>
    public int Read(Span<float> buffer)
    {
        var totalFrames = buffer.Length / _channels;
        if (totalFrames <= 0)
        {
            return 0;
        }

        var span = buffer.Slice(0, totalFrames * _channels);
        span.Clear(); // every stem sums INTO silence

        bool reachedEnd;

        lock (_gate)
        {
            // The block ramps from wherever the last one finished to the current target, so a fade
            // arriving between blocks is heard as a slope rather than a step.
            Array.Copy(_currentGain, _blockStartGain, _stems.Length);
            Array.Copy(_targetGain, _blockEndGain, _stems.Length);
            Array.Copy(_targetGain, _currentGain, _stems.Length);

            reachedEnd = MixLocked(span, totalFrames);
        }

        if (reachedEnd)
        {
            EndReached?.Invoke();
        }

        // Always a full buffer: past the end this is silence, because the voice hosting this is an
        // endless one and a short read would stop it from the audio thread.
        return span.Length;
    }

    // Callers hold _gate.
    private bool MixLocked(Span<float> buffer, int totalFrames)
    {
        var framesDone = 0;

        while (framesDone < totalFrames)
        {
            if (_framePosition >= LoopFrames)
            {
                if (!_isLooping)
                {
                    // The rest of the buffer stays silent. Report the end once.
                    if (_endRaised)
                    {
                        return false;
                    }

                    _endRaised = true;
                    return true;
                }

                _framePosition = 0;
            }

            var chunk = (int)Math.Min(totalFrames - framesDone, LoopFrames - _framePosition);

            for (var stem = 0; stem < _stems.Length; stem++)
            {
                MixStem(stem, _framePosition, framesDone, chunk, totalFrames, buffer);
            }

            framesDone += chunk;
            _framePosition += chunk;
        }

        return false;
    }

    private void MixStem(int stem, long readFrame, int destFrameOffset, int chunkFrames, int totalFrames, Span<float> buffer)
    {
        var startGain = _blockStartGain[stem];
        var endGain = _blockEndGain[stem];

        if (startGain <= 0f && endGain <= 0f)
        {
            return; // a silent layer costs nothing, which is what makes a large set affordable
        }

        var data = _stems[stem].AudioData;
        var stemFrames = _stemFrames[stem];
        var denominator = totalFrames > 1 ? totalFrames - 1 : 1;

        for (var frame = 0; frame < chunkFrames; frame++)
        {
            var source = readFrame + frame;
            if (source >= stemFrames)
            {
                return; // a stem shorter than the set: silent until the common loop point
            }

            var destinationFrame = destFrameOffset + frame;
            var t = (float)destinationFrame / denominator;
            var gain = startGain + ((endGain - startGain) * t);

            if (gain <= 0f)
            {
                continue;
            }

            var sourceIndex = (int)(source * _channels);
            var destinationIndex = destinationFrame * _channels;

            for (var channel = 0; channel < _channels; channel++)
            {
                buffer[destinationIndex + channel] += data[sourceIndex + channel] * gain;
            }
        }
    }
}
