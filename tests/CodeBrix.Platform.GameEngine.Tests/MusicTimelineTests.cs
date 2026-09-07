using System;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MusicTimeline"/> — the beat grid, and reading one out of a MIDI file. The MIDI
/// files here are built in code and written to a temp path, so the tempo map, the time signature and
/// the markers are all genuinely round-tripped through the standard file format rather than asserted
/// against a hand-made object. No committed binary fixture, and no audio device.
/// </summary>
public class MusicTimelineTests : IDisposable
{
    private const int TicksPerQuarter = 96;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "codebrix-gameengine-timeline-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates a scratch directory for the MIDI files a test writes.</summary>
    public MusicTimelineTests() => Directory.CreateDirectory(_directory);

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    // ----- the grid arithmetic -----

    [Fact]
    public void A_timeline_reports_its_beat_and_bar_lengths()
    {
        //Arrange & Act
        var timeline = new MusicTimeline(beatsPerMinute: 120, beatsPerBar: 4);

        //Assert
        timeline.SecondsPerBeat.Should().BeApproximately(0.5, 0.0001);
        timeline.SecondsPerBar.Should().BeApproximately(2.0, 0.0001);
    }

    [Fact]
    public void Immediate_never_waits()
    {
        //Arrange
        var timeline = new MusicTimeline(120, 4);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.3), MusicTransitionQuantize.Immediate);

        //Assert
        wait.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_wait_for_the_next_beat_is_the_remainder_of_the_current_one()
    {
        //Arrange - 120 BPM, so a beat is half a second.
        var timeline = new MusicTimeline(120, 4);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.3), MusicTransitionQuantize.Beat);

        //Assert
        wait.TotalSeconds.Should().BeApproximately(0.2, 0.0001);
    }

    [Fact]
    public void The_wait_for_the_next_bar_is_the_remainder_of_the_current_one()
    {
        //Arrange - a bar is two seconds.
        var timeline = new MusicTimeline(120, 4);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(2.5), MusicTransitionQuantize.Bar);

        //Assert
        wait.TotalSeconds.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void Landing_exactly_on_a_boundary_goes_now_rather_than_waiting_a_whole_bar()
    {
        //Arrange
        var timeline = new MusicTimeline(120, 4);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(4.0), MusicTransitionQuantize.Bar);

        //Assert
        wait.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void An_offset_shifts_the_whole_grid()
    {
        //Arrange - a recording with a quarter-second of silence before the first beat.
        var timeline = new MusicTimeline(120, 4, offsetSeconds: 0.25);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.25), MusicTransitionQuantize.Beat);
        var midBeat = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.5), MusicTransitionQuantize.Beat);

        //Assert
        wait.Should().Be(TimeSpan.Zero);
        midBeat.TotalSeconds.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void Inside_the_lead_in_the_next_boundary_is_the_first_beat_itself()
    {
        //Arrange
        var timeline = new MusicTimeline(120, 4, offsetSeconds: 1.0);

        //Act
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.4), MusicTransitionQuantize.Bar);

        //Assert
        wait.TotalSeconds.Should().BeApproximately(0.6, 0.0001);
    }

    [Fact]
    public void A_non_positive_tempo_is_rejected()
    {
        //Arrange & Act
        var act = () => new MusicTimeline(0, 4);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- reading a MIDI file -----

    [Fact]
    public void A_timeline_read_from_a_midi_file_carries_its_tempo_and_time_signature()
    {
        //Arrange
        var path = WriteMidi("basic.mid", bpm: 120, numerator: 4, denominatorExponent: 2);

        //Act
        var timeline = MusicTimeline.FromMidiFile(path);

        //Assert
        timeline.Should().NotBeNull();
        timeline!.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        timeline.BeatsPerBar.Should().BeApproximately(4, 0.001);
        timeline.HasTempoChanges.Should().BeFalse();
    }

    [Fact]
    public void A_six_eight_bar_is_three_quarter_note_beats_long()
    {
        //Arrange - the tempo's unit is a quarter note, so 6/8 is three beats to the bar, not six.
        //Getting this wrong would put every quantised transition in 6/8 at double the bar length.
        var path = WriteMidi("six-eight.mid", bpm: 120, numerator: 6, denominatorExponent: 3);

        //Act
        var timeline = MusicTimeline.FromMidiFile(path);

        //Assert
        timeline.Should().NotBeNull();
        timeline!.BeatsPerBar.Should().BeApproximately(3, 0.001);
        timeline.SecondsPerBar.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void Markers_become_jump_points_at_the_right_times()
    {
        //Arrange - a bar is 4 quarters at 96 ticks each; at 120 BPM that is 2 seconds.
        var path = WriteMidi("markers.mid", bpm: 120, numerator: 4, denominatorExponent: 2,
            markers: new[] { ("chorus", 2 * 4 * TicksPerQuarter), ("bridge", 4 * 4 * TicksPerQuarter) });

        //Act
        var timeline = MusicTimeline.FromMidiFile(path);

        //Assert
        timeline.Should().NotBeNull();
        timeline!.Markers.Count.Should().Be(2);

        timeline.TryGetMarker("chorus", out var chorus).Should().BeTrue();
        chorus.TotalSeconds.Should().BeApproximately(4.0, 0.001);

        timeline.TryGetMarker("BRIDGE", out var bridge).Should().BeTrue();
        bridge.TotalSeconds.Should().BeApproximately(8.0, 0.001);

        timeline.TryGetMarker("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void A_tempo_change_is_reported_and_markers_after_it_are_still_placed_correctly()
    {
        //Arrange - 120 BPM for two bars (8 quarters at 0.5s = 4.0s), then 240 BPM for two more
        //(8 quarters at 0.25s = 2.0s). The marker therefore belongs at 6.0s, which only comes out
        //right if the tempo MAP is walked; using the first tempo throughout would put it at 8.0s.
        var path = WriteMidi("tempo-change.mid", bpm: 120, numerator: 4, denominatorExponent: 2,
            markers: new[] { ("after", 4 * 4 * TicksPerQuarter) },
            secondTempo: (240, 2 * 4 * TicksPerQuarter));

        //Act
        var timeline = MusicTimeline.FromMidiFile(path);

        //Assert
        timeline.Should().NotBeNull();
        timeline!.HasTempoChanges.Should().BeTrue();
        timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);

        timeline.TryGetMarker("after", out var after).Should().BeTrue();
        after.TotalSeconds.Should().BeApproximately(6.0, 0.01);
    }

    [Fact]
    public void An_unreadable_file_yields_null_rather_than_throwing()
    {
        //Arrange - a game must not fail to start because a music asset is missing or malformed.
        var path = Path.Combine(_directory, "not-a-midi-file.mid");
        File.WriteAllText(path, "this is not a MIDI file");

        //Act
        var timeline = MusicTimeline.FromMidiFile(path);

        //Assert
        timeline.Should().BeNull();
    }

    [Fact]
    public void A_missing_file_yields_null()
    {
        //Arrange & Act
        var timeline = MusicTimeline.FromMidiFile(Path.Combine(_directory, "absent.mid"));

        //Assert
        timeline.Should().BeNull();
    }

    // ----- the grid through a tempo change -----

    [Fact]
    public void A_tempo_map_timeline_reports_the_initial_tempo_and_that_the_tempo_varies()
    {
        //Arrange & Act
        var timeline = new MusicTimeline(TwoTempoMap(), beatsPerBar: 4);

        //Assert
        timeline.TempoMap.Should().NotBeNull();
        timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        timeline.HasTempoChanges.Should().BeTrue();
    }

    [Fact]
    public void A_constant_tempo_map_is_not_reported_as_changing_tempo()
    {
        //Arrange
        var map = new MidiTempoMap(new[] { new MidiTempoChange(TimeSpan.Zero, 0, 100) });

        //Act
        var timeline = new MusicTimeline(map, beatsPerBar: 4);

        //Assert
        map.IsConstant.Should().BeTrue();
        timeline.HasTempoChanges.Should().BeFalse();
        timeline.BeatsPerMinute.Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void The_next_bar_across_a_tempo_change_is_where_the_map_puts_it()
    {
        //Arrange - 120 BPM to bar 2 (beat 4, at 2.0s), then 90 BPM. Bar 3 therefore begins at beat
        //8 and bar 4 at beat 12, both later than a constant 120 BPM grid would put them.
        var map = TwoTempoMap();
        var timeline = new MusicTimeline(map, beatsPerBar: 4);
        var position = TimeSpan.FromSeconds(5.0);

        //Act
        var wait = timeline.TimeToNextBoundary(position, MusicTransitionQuantize.Bar);

        //Assert - the expectation is the map's own arithmetic, not a number typed in here.
        var expected = map.TimeAt(12).TotalSeconds - position.TotalSeconds;
        wait.TotalSeconds.Should().BeApproximately(expected, 0.0005);

        //Assert - and it is genuinely a different answer from quantising against the first tempo,
        //which is the whole reason the map is threaded through.
        var constant = new MusicTimeline(120, 4).TimeToNextBoundary(position, MusicTransitionQuantize.Bar);
        Math.Abs(wait.TotalSeconds - constant.TotalSeconds).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void The_next_beat_inside_the_second_tempo_region_follows_the_new_tempo()
    {
        //Arrange
        var map = TwoTempoMap();
        var timeline = new MusicTimeline(map, beatsPerBar: 4);
        var position = TimeSpan.FromSeconds(5.0);

        //Act
        var wait = timeline.TimeToNextBoundary(position, MusicTransitionQuantize.Beat);

        //Assert - beat 8.5 at 90 BPM, so the next beat is 9.
        var expected = map.TimeAt(9).TotalSeconds - position.TotalSeconds;
        wait.TotalSeconds.Should().BeApproximately(expected, 0.0005);
        wait.TotalSeconds.Should().BeLessThan(60.0 / 90.0);
    }

    [Fact]
    public void Landing_exactly_on_a_boundary_of_a_tempo_map_grid_goes_now()
    {
        //Arrange - the start of bar 3, which the map alone knows the time of.
        var map = TwoTempoMap();
        var timeline = new MusicTimeline(map, beatsPerBar: 4);
        var barThree = map.TimeAt(8);

        //Act
        var bar = timeline.TimeToNextBoundary(barThree, MusicTransitionQuantize.Bar);
        var beat = timeline.TimeToNextBoundary(barThree, MusicTransitionQuantize.Beat);

        //Assert
        bar.Should().Be(TimeSpan.Zero);
        beat.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void An_offset_still_shifts_a_tempo_map_grid()
    {
        //Arrange - a recording with a half-second lead-in before the first beat.
        var map = TwoTempoMap();
        var timeline = new MusicTimeline(map, beatsPerBar: 4, offsetSeconds: 0.5);

        //Act
        var leadIn = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(0.2), MusicTransitionQuantize.Bar);
        var onTheBar = timeline.TimeToNextBoundary(map.TimeAt(8) + TimeSpan.FromSeconds(0.5), MusicTransitionQuantize.Bar);

        //Assert
        leadIn.TotalSeconds.Should().BeApproximately(0.3, 0.0001);
        onTheBar.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_null_tempo_map_is_rejected()
    {
        //Arrange & Act
        var act = () => new MusicTimeline((MidiTempoMap)null!, 4);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- the tempo map coming out of a file -----

    [Fact]
    public void FromMidiEvents_carries_the_tempo_map_and_still_places_its_markers()
    {
        //Arrange - the same file the marker test uses: 120 BPM for two bars, then 240.
        var path = WriteMidi("events-tempo-change.mid", bpm: 120, numerator: 4, denominatorExponent: 2,
            markers: new[] { ("after", 4 * 4 * TicksPerQuarter) },
            secondTempo: (240, 2 * 4 * TicksPerQuarter));

        var file = new MidiFile(path, false);

        //Act
        var timeline = MusicTimeline.FromMidiEvents(file.Events);

        //Assert - the map is there, and the markers are exactly where they always were.
        timeline.Should().NotBeNull();
        timeline!.TempoMap.Should().NotBeNull();
        timeline.HasTempoChanges.Should().BeTrue();
        timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);
        timeline.BeatsPerBar.Should().BeApproximately(4, 0.001);

        timeline.TryGetMarker("after", out var after).Should().BeTrue();
        after.TotalSeconds.Should().BeApproximately(6.0, 0.01);

        //Assert - and the grid now agrees with the markers instead of drifting away from them. The
        //change falls on beat 8, the start of bar 3, at 4.0s; bar 4 is four beats of 240 BPM later,
        //at 5.0s, where a grid quantised against the first tempo alone would have said 6.0s.
        timeline.TempoMap!.TimeAt(8).TotalSeconds.Should().BeApproximately(4.0, 0.01);
        timeline.TempoMap.TimeAt(12).TotalSeconds.Should().BeApproximately(5.0, 0.01);

        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(4.5), MusicTransitionQuantize.Bar);
        wait.TotalSeconds.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void FromMidiSequence_carries_the_tempo_map()
    {
        //Arrange
        var path = WriteMidi("sequence-tempo-change.mid", bpm: 120, numerator: 4, denominatorExponent: 2,
            secondTempo: (240, 2 * 4 * TicksPerQuarter));

        //Act
        var timeline = MusicTimeline.FromMidiSequence(new MidiSequence(path));

        //Assert
        timeline.Should().NotBeNull();
        timeline!.TempoMap.Should().NotBeNull();
        timeline.HasTempoChanges.Should().BeTrue();
        timeline.BeatsPerMinute.Should().BeApproximately(120, 0.001);

        //Assert - four beats to the bar is assumed, and there are no markers to be had.
        timeline.BeatsPerBar.Should().BeApproximately(4, 0.001);
        timeline.Markers.Should().BeEmpty();

        //Assert - the grid follows the change: bar 4 begins at 5.0s, not at the 6.0s a constant
        //120 BPM grid would say.
        var wait = timeline.TimeToNextBoundary(TimeSpan.FromSeconds(4.5), MusicTransitionQuantize.Bar);
        wait.TotalSeconds.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void FromMidiSequence_on_a_constant_tempo_file_reports_no_tempo_changes()
    {
        //Arrange
        var path = WriteMidi("sequence-constant.mid", bpm: 90, numerator: 4, denominatorExponent: 2);

        //Act
        var timeline = MusicTimeline.FromMidiSequence(new MidiSequence(path));

        //Assert
        timeline.Should().NotBeNull();
        timeline!.TempoMap.Should().NotBeNull();
        timeline.TempoMap!.IsConstant.Should().BeTrue();
        timeline.HasTempoChanges.Should().BeFalse();
        timeline.BeatsPerMinute.Should().BeApproximately(90, 0.001);
    }

    [Fact]
    public void FromMidiSequence_takes_a_meter_when_the_game_knows_one()
    {
        //Arrange - a sequence carries no time signature, so 6/8 has to be said out loud.
        var path = WriteMidi("sequence-six-eight.mid", bpm: 120, numerator: 6, denominatorExponent: 3);

        //Act
        var timeline = MusicTimeline.FromMidiSequence(new MidiSequence(path), beatsPerBar: 3);

        //Assert
        timeline.Should().NotBeNull();
        timeline!.BeatsPerBar.Should().BeApproximately(3, 0.001);
        timeline.SecondsPerBar.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void FromMidiSequence_yields_null_rather_than_throwing_when_there_is_no_sequence()
    {
        //Arrange & Act - the same rule the other factories follow: a missing music asset must not
        //stop a game from starting.
        var timeline = MusicTimeline.FromMidiSequence(null!);

        //Assert
        timeline.Should().BeNull();
    }

    // ----- helpers -----

    // 120 BPM to the start of bar 2 (beat 4, at 2.0 seconds), then 90 BPM.
    private static MidiTempoMap TwoTempoMap() =>
        new MidiTempoMap(new[]
        {
            new MidiTempoChange(TimeSpan.Zero, 0, 120),
            new MidiTempoChange(TimeSpan.FromSeconds(2), 4, 90),
        });

    private string WriteMidi(
        string fileName,
        double bpm,
        int numerator,
        int denominatorExponent,
        (string Name, int Tick)[]? markers = null,
        (double Bpm, int Tick)? secondTempo = null)
    {
        var events = new MidiEventCollection(1, TicksPerQuarter);

        var tempoTrack = events.AddTrack();
        tempoTrack.Add(new TempoEvent(MicrosecondsPerQuarter(bpm), 0));
        tempoTrack.Add(new TimeSignatureEvent(0, numerator, denominatorExponent, 24, 8));

        if (secondTempo is not null)
        {
            tempoTrack.Add(new TempoEvent(MicrosecondsPerQuarter(secondTempo.Value.Bpm), secondTempo.Value.Tick));
        }

        if (markers is not null)
        {
            foreach (var (name, tick) in markers)
            {
                tempoTrack.Add(new TextEvent(name, MetaEventType.Marker, tick));
            }
        }

        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        // A second track with an actual note, so the file is a plausible piece of music rather than
        // a bare tempo map.
        var noteTrack = events.AddTrack();
        noteTrack.Add(new NoteOnEvent(0, 1, 60, 100, TicksPerQuarter));
        noteTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, 16 * TicksPerQuarter));

        events.PrepareForExport();

        var path = Path.Combine(_directory, fileName);
        MidiFile.Export(path, events);
        return path;
    }

    private static int MicrosecondsPerQuarter(double bpm) => (int)Math.Round(60_000_000.0 / bpm);
}
