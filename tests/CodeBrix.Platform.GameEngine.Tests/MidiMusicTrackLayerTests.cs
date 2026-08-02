using System;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MidiMusicTrack"/>'s layering controls — the MIDI counterpart of
/// <see cref="MusicStemSet"/> — and the timeline it derives from the file it was loaded from.
/// The instrument, the sample it references and the MIDI file are all built in code, so this runs
/// against a genuinely loaded track with no committed fixture.
/// </summary>
/// <remarks>
/// NOTHING HERE IS PLAYED, but loading a track still builds a synthesizer and its output voice, and
/// that makes the process-wide shared output ADOPT A SAMPLE RATE. Left alone, that rate outlives
/// these tests and fails every later test whose source has a different one — which showed up as a
/// varying number of unrelated failures depending on the order the suite happened to run in. So this
/// class shuts the audio system down after each test, returning the shared output to the unclaimed
/// state it was found in.
/// </remarks>
public class MidiMusicTrackLayerTests : IDisposable
{
    private const int TicksPerQuarter = 96;

    private readonly MusicManager _manager = MusicManager.Instance;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "codebrix-gameengine-midi-" + Guid.NewGuid().ToString("N"));

    /// <summary>Builds the instrument and sequence fixtures, and takes manual control of the fade clock.</summary>
    public MidiMusicTrackLayerTests()
    {
        Directory.CreateDirectory(_directory);

        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();

        InstrumentPath = WriteSfzInstrument();
        MidiPath = WriteMidi();
    }

    private string InstrumentPath { get; }

    private string MidiPath { get; }

    /// <summary>
    /// Removes the fixtures and any fades in flight, and un-claims the shared audio output so the
    /// sample rate this test's synthesizer adopted does not leak into the rest of the suite.
    /// </summary>
    public void Dispose()
    {
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
        AudioSystem.Shutdown();

        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public void A_midi_track_loaded_from_a_path_derives_its_timeline()
    {
        //Arrange & Act - this is the whole point of the path overload: the sequence that plays has
        //already discarded the tempo map, so the grid has to come from a second read of the file.
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Assert
        track.Timeline.Should().NotBeNull();
        track.Timeline!.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        track.Timeline.BeatsPerBar.Should().BeApproximately(4, 0.001);

        track.Timeline.TryGetMarker("chorus", out var chorus).Should().BeTrue();
        chorus.TotalSeconds.Should().BeApproximately(4.0, 0.01);
    }

    [Fact]
    public void Layer_volumes_start_at_full()
    {
        //Arrange & Act - a channel nobody has touched is playing at whatever the arrangement says,
        //so reporting it as silent would be a lie.
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Assert
        track.GetLayerVolume(0).Should().Be(1f);
        track.GetLayerVolume(15).Should().Be(1f);
    }

    [Fact]
    public void SetLayerVolume_records_what_was_asked_for_and_clamps_it()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Act
        track.SetLayerVolume(3, 0.25f);
        track.SetLayerVolume(4, 5f);
        track.SetLayerVolume(5, -1f);

        //Assert
        track.GetLayerVolume(3).Should().Be(0.25f);
        track.GetLayerVolume(4).Should().Be(1f);
        track.GetLayerVolume(5).Should().Be(0f);
    }

    [Fact]
    public void A_channel_outside_the_midi_range_is_rejected()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Act & Assert - MIDI has 16 channels; anything else is a bug in the caller, not a clamp.
        ((Action)(() => track.SetLayerVolume(16, 1f))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => track.SetLayerVolume(-1, 1f))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => track.GetLayerVolume(16))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => track.FadeLayerTo(99, 1f))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => track.SetLayerPan(16, 0f))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FadeLayerTo_moves_a_layer_on_the_music_fade_clock()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);
        track.SetLayerVolume(2, 0f);

        //Act
        track.FadeLayerTo(2, 1f, TimeSpan.FromSeconds(2));
        _manager.Ticker.Tick(1.0);

        //Assert
        track.GetLayerVolume(2).Should().BeApproximately(0.5f, 0.01f);

        //Act
        _manager.Ticker.Tick(1.0);

        //Assert
        track.GetLayerVolume(2).Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void A_zero_length_layer_fade_applies_at_once()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Act
        track.FadeLayerTo(7, 0.5f);

        //Assert
        track.GetLayerVolume(7).Should().Be(0.5f);
    }

    [Fact]
    public void A_second_fade_on_a_layer_replaces_the_first()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);
        track.SetLayerVolume(1, 0f);
        track.FadeLayerTo(1, 1f, TimeSpan.FromSeconds(10));

        //Act
        track.FadeLayerTo(1, 0.25f, TimeSpan.FromSeconds(1));
        _manager.Ticker.Tick(1.0);

        //Assert
        _manager.Ticker.ActiveFadeCount.Should().Be(0);
        track.GetLayerVolume(1).Should().BeApproximately(0.25f, 0.001f);
    }

    [Fact]
    public void Disposing_a_track_cancels_its_layer_fades()
    {
        //Arrange - a fade left running would keep writing into a disposed player.
        var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);
        track.FadeLayerTo(1, 0f, TimeSpan.FromSeconds(10));

        //Act
        track.Dispose();

        //Assert
        _manager.Ticker.ActiveFadeCount.Should().Be(0);
    }

    [Fact]
    public void Speed_round_trips_and_defaults_to_the_written_tempo()
    {
        //Arrange
        using var track = new MidiMusicTrack("theme", InstrumentPath, MidiPath);

        //Assert
        track.Speed.Should().Be(1f);

        //Act - this is what a file track cannot do: slow the arrangement without dropping its pitch.
        track.Speed = 0.5f;

        //Assert
        track.Speed.Should().Be(0.5f);
    }

    // ----- fixtures -----

    // A one-region SFZ over a short generated tone. Enough to be a real, loadable instrument.
    private string WriteSfzInstrument()
    {
        var samplePath = Path.Combine(_directory, "tone.wav");
        WaveFileWriter.CreateWaveFile16(samplePath, new ToneSampleProvider(seconds: 0.25));

        var sfzPath = Path.Combine(_directory, "test.sfz");
        File.WriteAllText(sfzPath,
            "<region> sample=tone.wav lokey=0 hikey=127 pitch_keycenter=60 loop_mode=no_loop" + Environment.NewLine);

        return sfzPath;
    }

    // 120 BPM, 4/4, with a marker two bars in and a note on two different channels so there is
    // something for the layer controls to be about.
    private string WriteMidi()
    {
        var events = new MidiEventCollection(1, TicksPerQuarter);

        var tempoTrack = events.AddTrack();
        tempoTrack.Add(new TempoEvent(500_000, 0)); // 120 BPM
        tempoTrack.Add(new TimeSignatureEvent(0, 4, 2, 24, 8));
        tempoTrack.Add(new TextEvent("chorus", MetaEventType.Marker, 2 * 4 * TicksPerQuarter));
        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        var noteTrack = events.AddTrack();
        noteTrack.Add(new NoteOnEvent(0, 1, 60, 100, TicksPerQuarter));
        noteTrack.Add(new NoteOnEvent(2 * TicksPerQuarter, 3, 64, 100, TicksPerQuarter));
        noteTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        events.PrepareForExport();

        var path = Path.Combine(_directory, "theme.mid");
        MidiFile.Export(path, events);
        return path;
    }

    /// <summary>A short finite sine tone, so the SFZ region has a real sample behind it.</summary>
    private sealed class ToneSampleProvider : ISampleProvider
    {
        private const int Rate = 44100;

        private int _framesLeft;
        private int _frame;

        internal ToneSampleProvider(double seconds)
        {
            _framesLeft = (int)(Rate * seconds);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(_framesLeft, buffer.Length);

            for (var i = 0; i < count; i++)
            {
                buffer[i] = 0.25f * MathF.Sin(2f * MathF.PI * 261.63f * _frame++ / Rate);
            }

            _framesLeft -= count;
            return count;
        }
    }
}
