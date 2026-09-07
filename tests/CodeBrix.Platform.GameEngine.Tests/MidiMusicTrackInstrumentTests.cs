using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the instrument formats <see cref="MidiMusicTrack"/> accepts — SoundFont, SFZ and Decent
/// Sampler — the timeline each constructor derives, and what the track reports about a file that
/// does not quite follow the rules. Every instrument and every MIDI file is generated into a temp
/// directory by <see cref="SyntheticInstrumentAssets"/>, so these load real parsed instruments with
/// no committed fixture.
/// </summary>
/// <remarks>
/// Loading a track builds a synthesizer and its output voice, which makes the process-wide shared
/// output ADOPT A SAMPLE RATE. Left alone that rate outlives these tests and fails later ones whose
/// source has a different rate, so the audio system is shut down after each test — the same rule
/// <see cref="MidiMusicTrackLayerTests"/> follows, and for the same reason.
/// </remarks>
public class MidiMusicTrackInstrumentTests : IDisposable
{
    private const int TicksPerQuarter = 96;

    private readonly MusicManager _manager = MusicManager.Instance;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "codebrix-gameengine-instrument-" + Guid.NewGuid().ToString("N"));

    /// <summary>Builds the fixtures and takes manual control of the fade clock.</summary>
    public MidiMusicTrackInstrumentTests()
    {
        Directory.CreateDirectory(_directory);

        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();

        MidiPath = WriteMidi("theme.mid", numerator: 4, denominatorExponent: 2, markerName: "chorus");
    }

    private string MidiPath { get; }

    /// <summary>
    /// Removes the fixtures and any fades in flight, and un-claims the shared audio output so the
    /// sample rate a synthesizer here adopted does not leak into the rest of the suite.
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    // ----- the timeline the instance constructors derive -----

    [Fact]
    public void The_soundfont_constructor_fills_the_timeline_from_the_sequence()
    {
        //Arrange - the sequence keeps its tempo map, which is the part a quantised transition needs.
        var soundFont = new SoundFont(SyntheticInstrumentAssets.WriteSoundFont(Path.Combine(_directory, "tone.sf2")));

        //Act
        using var track = new MidiMusicTrack("theme", soundFont, new MidiSequence(MidiPath));

        //Assert
        track.Timeline.Should().NotBeNull();
        track.Timeline!.TempoMap.Should().NotBeNull();
        track.Timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        track.Timeline.BeatsPerBar.Should().BeApproximately(4, 0.001);
        track.Problems.Should().BeEmpty();
    }

    [Fact]
    public void The_sfz_constructor_fills_the_timeline_from_the_sequence()
    {
        //Arrange
        var instrument = new SfzInstrument(WriteSfzInstrument());

        //Act
        using var track = new MidiMusicTrack("theme", instrument, new MidiSequence(MidiPath));

        //Assert
        track.Timeline.Should().NotBeNull();
        track.Timeline!.TempoMap.Should().NotBeNull();
        track.Timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        track.Problems.Should().BeEmpty();
    }

    [Fact]
    public void A_sequence_derived_timeline_carries_no_markers_because_the_sequence_cannot_time_them()
    {
        //Arrange - the file DOES carry a marker; a MidiSequence keeps the name but not the tick
        //resolution to place it with, so the instance constructors deliberately offer none.
        var instrument = new SfzInstrument(WriteSfzInstrument());

        //Act
        using var track = new MidiMusicTrack("theme", instrument, new MidiSequence(MidiPath));

        //Assert
        track.Timeline.Should().NotBeNull();
        track.Timeline!.Markers.Should().BeEmpty();
        track.Timeline.TryGetMarker("chorus", out _).Should().BeFalse();
    }

    // ----- Decent Sampler -----

    [Fact]
    public void A_decent_sampler_instrument_plays_a_track_and_reports_no_problems()
    {
        //Arrange
        var presetPath = SyntheticInstrumentAssets.WriteDecentSamplerPreset(Path.Combine(_directory, "library"));
        using var instrument = DecentSamplerInstrument.Load(presetPath);

        //Act
        using var track = new MidiMusicTrack("theme", instrument, new MidiSequence(MidiPath));

        //Assert - the knob proves the preset's interface and its binding parsed, not just its XML.
        instrument.Controls.Count.Should().BeGreaterThan(0);
        instrument.Problems.Should().BeEmpty();
        track.Problems.Should().BeEmpty();
        track.Timeline.Should().NotBeNull();
        track.Timeline!.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        track.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void A_decent_sampler_preset_loads_by_path_as_well()
    {
        //Arrange - this is the form a game uses: two file names and nothing else. The engine reads
        //the extension, so a preset joins .sf2 and .sfz with no other change here.
        var presetPath = SyntheticInstrumentAssets.WriteDecentSamplerPreset(Path.Combine(_directory, "library"));

        //Act
        using var track = new MidiMusicTrack("theme", presetPath, MidiPath);

        //Assert
        track.Problems.Should().BeEmpty();
        track.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        track.Timeline.Should().NotBeNull();
        track.Timeline!.TryGetMarker("chorus", out _).Should().BeTrue();
    }

    [Fact]
    public void A_decent_sampler_folder_loads_by_path_too()
    {
        //Arrange - the folder form, which is what an unpacked library and a macOS bundle look like.
        var folder = Path.Combine(_directory, "library");
        SyntheticInstrumentAssets.WriteDecentSamplerPreset(folder);

        //Act
        using var track = new MidiMusicTrack("theme", folder, MidiPath);

        //Assert
        track.Problems.Should().BeEmpty();
        track.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ----- what the track says about a file that breaks the rules -----

    [Fact]
    public void A_midi_file_with_an_out_of_range_key_signature_still_yields_a_playable_track()
    {
        //Arrange - a key signature carrying a sharps/flats byte the specification does not allow,
        //which is what a machine-generated stem export writes. A strict read rejects the file; the
        //lenient read every path here takes carries it, so the game gets its music and its grid.
        var path = SyntheticInstrumentAssets.WriteMidiWithAnOutOfRangeKeySignature(
            Path.Combine(_directory, "lenient.mid"));

        var instrument = new SfzInstrument(WriteSfzInstrument());

        //Act - by instance, and by path, because the two read the file differently.
        using var byInstance = new MidiMusicTrack("odd", instrument, new MidiSequence(path));
        using var byPath = new MidiMusicTrack("odd-path", WriteSfzInstrument(), path);

        //Assert
        byInstance.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        byInstance.Timeline.Should().NotBeNull();
        byInstance.Timeline!.BeatsPerMinute.Should().BeApproximately(120, 0.001);

        byPath.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        byPath.Timeline.Should().NotBeNull();
        byPath.Timeline!.BeatsPerMinute.Should().BeApproximately(120, 0.001);

        //Assert - the sequence reader has no opinion about a key signature: it reads tempo, text and
        //notes, and skips the rest. Nothing is reported for this file, and nothing should be.
        byInstance.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Problems_carries_what_the_midi_reader_objected_to()
    {
        //Arrange - a note left sounding when its track ends. The lenient reader releases it and says
        //so, which is exactly the kind of thing a game shipping a generated file wants told.
        var path = WriteMidi("hanging.mid", numerator: 4, denominatorExponent: 2, markerName: "chorus",
            releaseTheNote: false);

        var sequence = new MidiSequence(path);
        var instrument = new SfzInstrument(WriteSfzInstrument());

        //Act
        using var track = new MidiMusicTrack("odd", instrument, sequence);

        //Assert
        sequence.Problems.Should().NotBeEmpty();
        track.Problems.Should().NotBeEmpty();
        track.Problems.Count.Should().Be(sequence.Problems.Count);
        track.Problems.Any(problem => problem.Contains("sounding", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();

        //Assert - and it is still a playable track with a grid.
        track.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        track.Timeline.Should().NotBeNull();
    }

    [Fact]
    public void A_clean_file_and_a_clean_instrument_report_nothing()
    {
        //Arrange
        var instrument = new SfzInstrument(WriteSfzInstrument());

        //Act
        using var track = new MidiMusicTrack("theme", instrument, new MidiSequence(MidiPath));

        //Assert - both sources are clean, so nothing is reported and nothing is logged.
        instrument.Problems.Should().BeEmpty();
        track.Problems.Should().BeEmpty();
    }

    // ----- the path constructor keeps what only a file can give -----

    [Fact]
    public void The_path_constructor_still_reads_the_time_signature_and_the_markers()
    {
        //Arrange - 6/8 is three quarter-note beats to the bar, and only the FILE says so; the
        //sequence the instance constructors take would have assumed four.
        var path = WriteMidi("six-eight.mid", numerator: 6, denominatorExponent: 3, markerName: "bridge");

        //Act
        using var track = new MidiMusicTrack("theme", WriteSfzInstrument(), path);

        //Assert
        track.Timeline.Should().NotBeNull();
        track.Timeline!.BeatsPerBar.Should().BeApproximately(3, 0.001);
        track.Timeline.TempoMap.Should().NotBeNull();
        track.Timeline.TryGetMarker("bridge", out var bridge).Should().BeTrue();
        bridge.TotalSeconds.Should().BeApproximately(4.0, 0.01);
    }

    // ----- fixtures -----

    // A one-region SFZ over a short generated tone. Enough to be a real, loadable instrument.
    private string WriteSfzInstrument()
    {
        var folder = Path.Combine(_directory, "sfz");
        Directory.CreateDirectory(folder);

        SyntheticInstrumentAssets.WriteMonoWav(Path.Combine(folder, "tone.wav"), seconds: 0.25, frequency: 261.63);

        var sfzPath = Path.Combine(folder, "test.sfz");
        File.WriteAllText(sfzPath,
            "<region> sample=tone.wav lokey=0 hikey=127 pitch_keycenter=60 loop_mode=no_loop" + Environment.NewLine);

        return sfzPath;
    }

    // 120 BPM, one marker two bars in, and a note so the file is a plausible piece of music.
    private string WriteMidi(
        string fileName, int numerator, int denominatorExponent, string markerName, bool releaseTheNote = true)
    {
        var events = new MidiEventCollection(1, TicksPerQuarter);

        var tempoTrack = events.AddTrack();
        tempoTrack.Add(new TempoEvent(500_000, 0)); // 120 BPM
        tempoTrack.Add(new TimeSignatureEvent(0, numerator, denominatorExponent, 24, 8));
        tempoTrack.Add(new TextEvent(markerName, MetaEventType.Marker, 2 * 4 * TicksPerQuarter));
        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        var noteTrack = events.AddTrack();
        noteTrack.Add(new NoteOnEvent(0, 1, 60, 100, TicksPerQuarter));

        // A note nobody releases is a real complaint the reader makes, so it is optional here: a
        // clean fixture must not carry one, and the test about problems needs exactly one.
        if (releaseTheNote)
        {
            noteTrack.Add(new NoteEvent(TicksPerQuarter, 1, MidiCommandCode.NoteOff, 60, 0));
        }

        noteTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        events.PrepareForExport();

        var path = Path.Combine(_directory, fileName);
        MidiFile.Export(path, events);
        return path;
    }
}
