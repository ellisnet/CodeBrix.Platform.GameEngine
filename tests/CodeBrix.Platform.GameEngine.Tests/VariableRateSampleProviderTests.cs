using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class VariableRateSampleProviderTests
{
    /// <summary>A deterministic mono/stereo float source producing an incrementing ramp per channel.</summary>
    private sealed class RampSource : ISampleProvider
    {
        private readonly int _totalFrames;
        private int _framesRead;

        public RampSource(int sampleRate, int channels, int totalFrames)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _totalFrames = totalFrames;
        }

        public WaveFormat WaveFormat { get; }

        /// <summary>Rewinds to the first frame, the way a wave stream set back to position 0 does.</summary>
        public void Rewind() => _framesRead = 0;

        public int Read(Span<float> buffer)
        {
            var channels = WaveFormat.Channels;
            var frames = Math.Min(buffer.Length / channels, _totalFrames - _framesRead);
            for (var frame = 0; frame < frames; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    // Value = source frame index (per channel offset by 1000*channel).
                    buffer[frame * channels + channel] = _framesRead + frame + 1000f * channel;
                }
            }

            _framesRead += frames;
            return frames * channels;
        }
    }

    /// <summary>A source that hands back exactly the samples it was given, one chunk per read.</summary>
    private sealed class BufferSource : ISampleProvider
    {
        private readonly float[] _samples;
        private readonly int _chunkSamples;
        private int _read;

        public BufferSource(int sampleRate, int channels, float[] samples, int chunkSamples = int.MaxValue)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _samples = samples;
            _chunkSamples = chunkSamples;
        }

        public WaveFormat WaveFormat { get; }

        /// <summary>How many samples this source has handed out so far.</summary>
        public int SamplesTaken => _read;

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, _chunkSamples), _samples.Length - _read);
            _samples.AsSpan(_read, count).CopyTo(buffer);
            _read += count;
            return count;
        }
    }

    private static int SamplesTakenFrom(BufferSource source) => source.SamplesTaken;

    [Fact]
    public void Same_rate_unity_pitch_is_a_bit_exact_pass_through()
    {
        //Arrange - ordinary but awkward sample values, at the output rate and unity pitch.
        var samples = new[]
        {
            0f, 1f, -1f, 0.5f, -0.5f, 0.1f, -0.1f, 1e-7f, -1e-7f, 0.3333333f,
            -0.6666667f, float.Epsilon, -float.Epsilon, 0.9999999f, -0.9999999f, 0.25f,
        };
        var provider = new VariableRateSampleProvider(new BufferSource(44100, 1, samples), 44100);
        var output = new float[samples.Length + 16];

        //Act
        var total = 0;
        int read;
        while ((read = provider.Read(output.AsSpan(total))) > 0)
        {
            total += read;
        }

        //Assert - EVERY source frame comes back, the final one included, and each is the source
        //sample itself with no arithmetic applied. This is what lets every resource carry the
        //stage: at unity it changes nothing at all.
        total.Should().Be(samples.Length);
        for (var i = 0; i < total; i++)
        {
            output[i].Should().Be(samples[i]);
        }
    }

    [Fact]
    public void Unity_takes_exactly_as_many_source_frames_as_it_hands_back()
    {
        //Arrange - source and output at the same rate, pitch 1.0.
        var source = new BufferSource(44100, 1, new float[8192]);
        var provider = new VariableRateSampleProvider(source, 44100);

        //Act - ask for eight frames.
        provider.Read(new float[8]).Should().Be(8);

        //Assert - and exactly eight are taken from the source. No read-ahead means a wave stream
        //behind this stage still reports the position that has actually been handed over.
        SamplesTakenFrom(source).Should().Be(8);
    }

    [Fact]
    public void Resampling_buffers_well_ahead_of_what_it_hands_back()
    {
        //Arrange - a real rate conversion, which needs frames in hand to interpolate between.
        var source = new BufferSource(11025, 1, new float[8192]);
        var provider = new VariableRateSampleProvider(source, 44100);

        //Act
        provider.Read(new float[8]).Should().Be(8);

        //Assert - the resampling path fills its whole buffer, so a stream behind it runs ahead of
        //what has been heard. Only a converting resource pays that.
        SamplesTakenFrom(source).Should().Be(2049);
    }

    [Fact]
    public void Resampling_hands_over_the_final_source_frame()
    {
        //Arrange - 4x upsample of ten source frames.
        var provider = new VariableRateSampleProvider(new RampSource(11025, 1, 10), 44100);
        var output = new float[128];

        //Act
        var total = 0;
        int read;
        while ((read = provider.Read(output.AsSpan(total))) > 0)
        {
            total += read;
        }

        //Assert - forty output frames, i.e. all ten source frames converted; the last one used to
        //be held back for want of an interpolation partner and is now held at its own value.
        total.Should().Be(40);
        output[36].Should().Be(9f);
        output[39].Should().Be(9f);
    }

    [Fact]
    public void Pitch_changes_mid_stream_keep_the_output_continuous()
    {
        //Arrange - a ramp whose sample value IS its source frame index, so the output reads as the
        //position the stage is at.
        var provider = new VariableRateSampleProvider(new RampSource(44100, 1, 4096), 44100);
        var buffer = new float[10];

        //Act + Assert - unity first: ten frames, one per source frame.
        provider.Read(buffer).Should().Be(10);
        buffer[0].Should().Be(0f);
        buffer[9].Should().Be(9f);

        //Act + Assert - leaving unity picks up exactly where unity stopped and strides by 1.5.
        provider.Pitch = 1.5f;
        provider.Read(buffer).Should().Be(10);
        buffer[0].Should().Be(10f);
        (Math.Abs(buffer[1] - 11.5f) < 0.0001f).Should().BeTrue($"expected 11.5 but got {buffer[1]}");
        (Math.Abs(buffer[9] - 23.5f) < 0.0001f).Should().BeTrue($"expected 23.5 but got {buffer[9]}");

        //Act + Assert - returning to unity drains what was buffered, in order and without a gap:
        //the next position after 23.5 at a stride of 1.5 is 25.0, and unity carries on from there.
        provider.Pitch = 1f;
        provider.Read(buffer).Should().Be(10);
        buffer[0].Should().Be(25f);
        buffer[1].Should().Be(26f);
        buffer[9].Should().Be(34f);

        provider.Read(buffer).Should().Be(10);
        buffer[0].Should().Be(35f);
        buffer[9].Should().Be(44f);
    }

    [Fact]
    public void Constructor_rejects_null_source_and_bad_rate()
    {
        //Arrange
        Action nullSource = () => _ = new VariableRateSampleProvider(null!, 44100);
        Action badRate = () => _ = new VariableRateSampleProvider(new RampSource(11025, 1, 10), 0);

        //Act + Assert
        nullSource.Should().Throw<ArgumentNullException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Output_format_is_float_at_the_output_rate_with_source_channels()
    {
        //Arrange
        var provider = new VariableRateSampleProvider(new RampSource(11025, 2, 10), 44100);

        //Act + Assert
        provider.WaveFormat.SampleRate.Should().Be(44100);
        provider.WaveFormat.Channels.Should().Be(2);
    }

    [Fact]
    public void Same_rate_unity_pitch_passes_samples_through()
    {
        //Arrange
        var provider = new VariableRateSampleProvider(new RampSource(44100, 1, 100), 44100);
        var buffer = new float[50];

        //Act
        var read = provider.Read(buffer);

        //Assert - step is exactly 1.0, so output n is source frame n.
        read.Should().Be(50);
        buffer[0].Should().Be(0f);
        buffer[1].Should().Be(1f);
        buffer[49].Should().Be(49f);
    }

    [Fact]
    public void Upsampling_interpolates_linearly_between_source_frames()
    {
        //Arrange - 4x upsample: output n sits at source position n * 0.25.
        var provider = new VariableRateSampleProvider(new RampSource(11025, 1, 100), 44100);
        var buffer = new float[8];

        //Act
        var read = provider.Read(buffer);

        //Assert - a ramp interpolates to the fractional position itself.
        read.Should().Be(8);
        buffer[0].Should().Be(0f);
        (Math.Abs(buffer[1] - 0.25f) < 0.0001f).Should().BeTrue($"expected 0.25 but got {buffer[1]}");
        (Math.Abs(buffer[2] - 0.5f) < 0.0001f).Should().BeTrue($"expected 0.5 but got {buffer[2]}");
        (Math.Abs(buffer[7] - 1.75f) < 0.0001f).Should().BeTrue($"expected 1.75 but got {buffer[7]}");
    }

    [Fact]
    public void Pitch_multiplier_scales_the_source_step()
    {
        //Arrange - same rate but pitch 2.0: output n reads source position 2n.
        var provider = new VariableRateSampleProvider(new RampSource(44100, 1, 100), 44100)
        {
            Pitch = 2f,
        };
        var buffer = new float[10];

        //Act
        var read = provider.Read(buffer);

        //Assert
        read.Should().Be(10);
        buffer[0].Should().Be(0f);
        buffer[1].Should().Be(2f);
        buffer[4].Should().Be(8f);
    }

    [Fact]
    public void Pitch_is_clamped_to_the_documented_range()
    {
        //Arrange
        var provider = new VariableRateSampleProvider(new RampSource(44100, 1, 10), 44100);

        //Act
        provider.Pitch = 0f;
        var low = provider.Pitch;
        provider.Pitch = 100f;
        var high = provider.Pitch;

        //Assert
        low.Should().Be(0.05f);
        high.Should().Be(20f);
    }

    [Fact]
    public void Stereo_channels_are_interpolated_independently()
    {
        //Arrange - 2x upsample of a stereo ramp (right channel offset by 1000).
        var provider = new VariableRateSampleProvider(new RampSource(22050, 2, 100), 44100);
        var buffer = new float[8]; // 4 output frames

        //Act
        var read = provider.Read(buffer);

        //Assert
        read.Should().Be(8);
        buffer[0].Should().Be(0f);       // L at position 0
        buffer[1].Should().Be(1000f);    // R at position 0
        (Math.Abs(buffer[2] - 0.5f) < 0.0001f).Should().BeTrue();     // L at position 0.5
        (Math.Abs(buffer[3] - 1000.5f) < 0.0001f).Should().BeTrue();  // R at position 0.5
    }

    [Fact]
    public void Source_end_yields_a_short_read_then_zero()
    {
        //Arrange - 10 source frames at unity ratio.
        var provider = new VariableRateSampleProvider(new RampSource(44100, 1, 10), 44100);
        var buffer = new float[32];

        //Act
        var first = provider.Read(buffer);
        var second = provider.Read(buffer);

        //Assert - all ten source frames come back, then the source is dry.
        first.Should().Be(10);
        second.Should().Be(0);
    }

    [Fact]
    public void Reset_lets_the_provider_read_a_rewound_source_again()
    {
        //Arrange - drain the source, which latches end-of-source inside the provider.
        var source = new RampSource(44100, 1, 10);
        var provider = new VariableRateSampleProvider(source, 44100);
        var buffer = new float[32];
        provider.Read(buffer);
        provider.Read(buffer).Should().Be(0);

        //Act - rewinding the source is not enough on its own; the stage has to be re-armed too.
        source.Rewind();
        var withoutReset = provider.Read(buffer);
        provider.Reset();
        var afterReset = provider.Read(buffer);

        //Assert - this is what keeps a looping voice from stopping and restarting in silence.
        withoutReset.Should().Be(0);
        (afterReset >= 9).Should().BeTrue($"expected the rewound source to be read again but got {afterReset} samples");
        buffer[0].Should().Be(0f);
        buffer[1].Should().Be(1f);
    }

    [Fact]
    public void Reset_discards_frames_buffered_from_the_old_source_position()
    {
        //Arrange - a converting stage, which reads far ahead of what it hands back.
        var source = new RampSource(11025, 1, 4096);
        var provider = new VariableRateSampleProvider(source, 44100);
        var buffer = new float[8];
        provider.Read(buffer);
        buffer[0].Should().Be(0f);

        //Act - the source jumps back to the start, as a seek to zero does.
        source.Rewind();
        provider.Reset();
        provider.Read(buffer);

        //Assert - the stale frames are gone, so playback follows the new position.
        buffer[0].Should().Be(0f);
        (Math.Abs(buffer[1] - 0.25f) < 0.0001f).Should().BeTrue($"expected 0.25 but got {buffer[1]}");
    }
}
