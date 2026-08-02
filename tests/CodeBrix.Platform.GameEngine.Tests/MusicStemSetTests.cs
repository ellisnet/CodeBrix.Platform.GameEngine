using System;
using System.Collections.Generic;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MusicStemSet"/> and the summing mixer under it. Nothing here opens an audio
/// device: stems are built from a synthetic sample source, and the mix is read straight out of the
/// provider, so what is asserted is the actual audio rather than a proxy for it.
/// </summary>
public class MusicStemSetTests : IDisposable
{
    private const int SampleRate = 44100;

    private readonly MusicManager _manager = MusicManager.Instance;

    /// <summary>Takes manual control of the fade clock so stem fades advance exactly.</summary>
    public MusicStemSetTests()
    {
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
    }

    /// <summary>Leaves no fades behind for the next test.</summary>
    public void Dispose()
    {
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
    }

    // ----- format and length rules -----

    [Fact]
    public void Stems_of_different_sample_rates_are_rejected_and_both_formats_are_named()
    {
        //Arrange
        var stems = new[] { Sound(0.5f, frames: 100), Sound(0.5f, frames: 100, sampleRate: 22050) };

        //Act
        var act = () => new MusicStemSet("set", new[] { "explore", "combat" }, stems);

        //Assert - mixing these anyway would play one layer at the wrong speed, which reads as an
        //engine bug rather than an asset problem, so the message has to name the culprit.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*combat*22050*explore*44100*");
    }

    [Fact]
    public void Stems_of_different_channel_counts_are_rejected()
    {
        //Arrange
        var stems = new[] { Sound(0.5f, frames: 100, channels: 2), Sound(0.5f, frames: 100, channels: 1) };

        //Act
        var act = () => new MusicStemSet("set", new[] { "a", "b" }, stems);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*ch*");
    }

    [Fact]
    public void A_set_needs_at_least_one_stem()
    {
        //Arrange & Act
        var act = () => new MusicStemSet("set", Array.Empty<string>(), Array.Empty<CachedSound>());

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Names_and_stems_must_correspond()
    {
        //Arrange & Act
        var act = () => new MusicStemSet("set", new[] { "only-one" }, new[] { Sound(0.5f, 10), Sound(0.5f, 10) });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    // ----- the mix itself -----

    [Fact]
    public void A_stem_set_sums_its_stems_sample_for_sample()
    {
        //Arrange - two constant stems, both fully in.
        var set = NewSet(out var provider, (0.25f, 200), (0.5f, 200));
        set["combat"].Gain = 1f;

        //Act - the first block ramps up from silence, so settle it and read the second.
        Read(provider, frames: 64);
        var block = Read(provider, frames: 64);

        //Assert
        block[0].Should().BeApproximately(0.75f, 0.0001f);
        block[63].Should().BeApproximately(0.75f, 0.0001f);
    }

    [Fact]
    public void A_stem_at_zero_gain_contributes_nothing()
    {
        //Arrange
        var set = NewSet(out var provider, (0.25f, 200), (0.5f, 200));
        set["combat"].Gain = 0f;

        //Act
        Read(provider, frames: 64);
        var block = Read(provider, frames: 64);

        //Assert - only the explore layer is heard.
        block[0].Should().BeApproximately(0.25f, 0.0001f);
    }

    [Fact]
    public void Only_the_first_stem_starts_audible()
    {
        //Arrange & Act - a set that came up with every layer at full would be the loudest possible
        //first impression of the feature, so layers are brought in deliberately.
        var set = NewSet(out _, (0.25f, 200), (0.5f, 200));

        //Assert
        set["explore"].Gain.Should().Be(1f);
        set["combat"].Gain.Should().Be(0f);
    }

    [Fact]
    public void A_gain_change_ramps_across_the_block_rather_than_stepping()
    {
        //Arrange - a step change in gain is a click, so a new target is approached across the block.
        var set = NewSet(out var provider, (1.0f, 500));
        Read(provider, frames: 64);          // settle stem 0 at full
        set["explore"].Gain = 0f;

        //Act
        var block = Read(provider, frames: 64);

        //Assert - it starts where the last block ended and arrives at the target, monotonically.
        block[0].Should().BeApproximately(1f, 0.0001f);
        block[63].Should().BeApproximately(0f, 0.02f);

        for (var i = 1; i < block.Length; i++)
        {
            block[i].Should().BeLessThanOrEqualTo(block[i - 1]);
        }
    }

    // ----- looping and ends -----

    [Fact]
    public void The_set_loops_at_the_longest_stem()
    {
        //Arrange - 100 frames long, looping.
        var set = NewSet(out var provider, (0.5f, 100));
        set.IsLooping = true;

        //Act - read past the end.
        Read(provider, frames: 250);

        //Assert - 250 frames over a 100-frame loop leaves the position at 50.
        provider.FramePosition.Should().Be(50);
        set.Duration.Should().Be(TimeSpan.FromSeconds(100 / (double)SampleRate));
    }

    [Fact]
    public void A_shorter_stem_falls_silent_until_the_common_loop_point()
    {
        //Arrange - a 50-frame stem inside a 100-frame set. It must NOT wrap early on its own, which
        //would break the very lock this type exists to guarantee.
        var set = NewSet(out var provider, (0.25f, 100), (0.5f, 50));
        set["combat"].Gain = 1f;
        Read(provider, frames: 8); // settle the gains

        //Act
        var block = Read(provider, frames: 80);

        //Assert - both layers up to frame 50, then the long one alone.
        block[0].Should().BeApproximately(0.75f, 0.0001f);
        block[79].Should().BeApproximately(0.25f, 0.0001f);
    }

    [Fact]
    public void A_non_looping_set_reports_its_end_once_and_then_produces_silence()
    {
        //Arrange
        var set = NewSet(out var provider, (0.5f, 100));
        set.IsLooping = false;

        var ended = 0;
        set.Ended += (_, _) => ended++;

        //Act - read well past the end, twice.
        var first = Read(provider, frames: 200);
        var second = Read(provider, frames: 200);

        //Assert
        ended.Should().Be(1);
        first[150].Should().Be(0f);
        second[0].Should().Be(0f);
    }

    [Fact]
    public void Seeking_moves_the_read_position()
    {
        //Arrange
        var set = NewSet(out var provider, (0.5f, SampleRate));

        //Act
        set.Seek(TimeSpan.FromSeconds(0.5));

        //Assert
        provider.FramePosition.Should().Be(SampleRate / 2);
        set.Position.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    // ----- addressing stems -----

    [Fact]
    public void Stems_are_addressable_by_name_ignoring_case()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));

        //Act & Assert
        set["COMBAT"].Should().BeSameAs(set["combat"]);
        set[1].Should().BeSameAs(set["combat"]);
        set.Count.Should().Be(2);
    }

    [Fact]
    public void An_unknown_stem_name_lists_the_names_that_do_exist()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));

        //Act
        var act = () => set["boss"];

        //Assert - a typo in a stem name should not take a hunt through the asset folder to find.
        act.Should().Throw<KeyNotFoundException>().WithMessage("*explore*combat*");
    }

    [Fact]
    public void An_explicit_name_map_names_the_stems()
    {
        //Arrange
        var stems = new[] { Sound(0.25f, 100), Sound(0.5f, 100) };

        //Act
        var set = new MusicStemSet("set", new[] { "quiet", "loud" }, stems);

        //Assert
        set["quiet"].Name.Should().Be("quiet");
        set["loud"].Name.Should().Be("loud");
    }

    // ----- fades -----

    [Fact]
    public void FadeTo_moves_a_stems_gain_on_the_music_fade_clock()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));

        //Act
        set["combat"].FadeTo(1f, TimeSpan.FromSeconds(2));
        _manager.Ticker.Tick(1.0);

        //Assert - halfway.
        set["combat"].Gain.Should().BeApproximately(0.5f, 0.01f);

        //Act
        _manager.Ticker.Tick(1.0);

        //Assert
        set["combat"].Gain.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void A_zero_length_fade_applies_the_target_at_once()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));

        //Act
        set["combat"].FadeTo(1f);

        //Assert
        set["combat"].Gain.Should().Be(1f);
    }

    [Fact]
    public void Setting_a_gain_directly_cancels_the_fade_that_was_moving_it()
    {
        //Arrange - otherwise the fade would keep overwriting what the game just set.
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));
        set["combat"].FadeTo(1f, TimeSpan.FromSeconds(2));

        //Act
        set["combat"].Gain = 0.25f;
        _manager.Ticker.Tick(2.0);

        //Assert
        set["combat"].Gain.Should().Be(0.25f);
        _manager.Ticker.ActiveFadeCount.Should().Be(0);
    }

    [Fact]
    public void A_second_fade_on_a_stem_replaces_the_first()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));
        set["combat"].FadeTo(1f, TimeSpan.FromSeconds(10));

        //Act
        set["combat"].FadeTo(0.5f, TimeSpan.FromSeconds(1));
        _manager.Ticker.Tick(1.0);

        //Assert
        _manager.Ticker.ActiveFadeCount.Should().Be(0);
        set["combat"].Gain.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void Disposing_a_set_cancels_its_stem_fades()
    {
        //Arrange
        var set = NewSet(out _, (0.25f, 100), (0.5f, 100));
        set["combat"].FadeTo(1f, TimeSpan.FromSeconds(10));

        //Act
        set.Dispose();

        //Assert - a fade left running would keep writing into a disposed set's provider.
        _manager.Ticker.ActiveFadeCount.Should().Be(0);
    }

    // ----- helpers -----

    // Builds a two-stem set named explore/combat (or as many as given), and hands back the mixer
    // under it so a test can read the actual audio.
    private static MusicStemSet NewSet(out StemMixSampleProvider provider, params (float Value, int Frames)[] stems)
    {
        var names = new[] { "explore", "combat", "boss" };
        var sounds = new CachedSound[stems.Length];
        var stemNames = new string[stems.Length];

        for (var i = 0; i < stems.Length; i++)
        {
            sounds[i] = Sound(stems[i].Value, stems[i].Frames);
            stemNames[i] = names[i];
        }

        var set = new MusicStemSet("test-set", stemNames, sounds);
        provider = ProviderOf(set);
        return set;
    }

    private static StemMixSampleProvider ProviderOf(MusicStemSet set)
        => (StemMixSampleProvider)typeof(MusicStemSet)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(set)!;

    private static float[] Read(StemMixSampleProvider provider, int frames)
    {
        var buffer = new float[frames];
        provider.Read(buffer);
        return buffer;
    }

    private static CachedSound Sound(float value, int frames, int sampleRate = SampleRate, int channels = 1)
        => new(new ConstantSampleProvider(value, frames, sampleRate, channels));

    /// <summary>A finite source of one repeated sample value, so a mix has a predictable answer.</summary>
    private sealed class ConstantSampleProvider : ISampleProvider
    {
        private readonly float _value;
        private readonly int _channels;
        private int _framesLeft;

        internal ConstantSampleProvider(float value, int frames, int sampleRate, int channels)
        {
            _value = value;
            _framesLeft = frames;
            _channels = channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var frames = Math.Min(_framesLeft, buffer.Length / _channels);
            var samples = frames * _channels;

            buffer.Slice(0, samples).Fill(_value);
            _framesLeft -= frames;
            return samples;
        }
    }
}
