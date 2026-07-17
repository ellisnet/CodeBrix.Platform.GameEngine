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

        //Assert - the final frame has no interpolation partner, so at most the 10 source
        // frames (implementations may hold back the very last one).
        (first >= 9 && first <= 10).Should().BeTrue($"expected 9 or 10 samples but got {first}");
        second.Should().Be(0);
    }
}
