using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A named point in a piece of music that the game can jump to — a MIDI marker, read straight out of
/// the file.
/// </summary>
/// <param name="Name">The marker's text, as written in the file.</param>
/// <param name="Time">Where it falls, from the start of the piece.</param>
public readonly record struct MusicMarker(string Name, TimeSpan Time)
{
    /// <summary>Describes the marker as <c>name@1.23s</c>.</summary>
    /// <returns>The description.</returns>
    /// <remarks>
    /// A record struct's generated <c>ToString</c> would print
    /// <c>MusicMarker { Name = chorus, Time = 00:00:04 }</c>, which is noise in a log line or an
    /// on-screen readout listing several of them.
    /// </remarks>
    public override string ToString() => $"{Name}@{Time.TotalSeconds:0.##}s";
}

/// <summary>
/// Where the beats and bars of a piece of music are, so a transition can be made to land on one
/// instead of wherever the game happened to ask.
/// </summary>
/// <remarks>
/// <para>
/// FOR MIDI MUSIC THIS IS FREE. <see cref="FromMidiFile"/> reads the tempo, the time signature and
/// the markers out of the file, and <see cref="FromMidiSequence"/> takes the tempo out of a sequence
/// that has already been parsed for playback. <see cref="MidiMusicTrack"/> does one or the other for
/// itself, whichever it was given. Nothing needs to be measured or typed in.
/// </para>
/// <para>
/// FOR A DECODED AUDIO FILE THE GAME MUST SUPPLY IT. There is no way to infer a grid from a decoded
/// stream — beat detection is a guess, and a guess here produces transitions that are subtly and
/// unfixably late, which is worse than not offering the feature. The composer knows the tempo; put
/// it in the constructor.
/// </para>
/// <code>
/// track.Timeline = new MusicTimeline(beatsPerMinute: 128, beatsPerBar: 4);
/// MusicManager.Instance.CrossfadeTo(combat, TimeSpan.FromSeconds(2), MusicTransitionQuantize.Bar);
/// </code>
/// <para>
/// THE GRID FOLLOWS THE TEMPO. A timeline given a tempo in its constructor is a constant grid: one
/// tempo, one time signature, running the length of the piece. A timeline given a
/// <see cref="MidiTempoMap"/> — which is what <see cref="FromMidiFile"/>,
/// <see cref="FromMidiEvents"/> and <see cref="FromMidiSequence"/> produce — quantises THROUGH the
/// map, so a beat or bar boundary is exactly where the file puts it however often the tempo moves.
/// <see cref="HasTempoChanges"/> says whether the source's tempo varies at all, and
/// <see cref="TempoMap"/> is the map itself, or null for a constant grid.
/// </para>
/// <para>
/// That matters because a machine-generated arrangement routinely carries ONE TEMPO EVENT PER BEAT.
/// Quantising such a file against its first tempo alone would put every transition off the beat
/// within a few bars. Markers are placed through the same map, so a jump point and the bar line it
/// sits on agree.
/// </para>
/// </remarks>
public sealed class MusicTimeline
{
    private static readonly MusicMarker[] _noMarkers = Array.Empty<MusicMarker>();

    /// <summary>Creates a timeline for a piece at a fixed tempo.</summary>
    /// <param name="beatsPerMinute">The tempo. Must be positive.</param>
    /// <param name="beatsPerBar">How many beats make a bar — 4 for common time. Must be positive.</param>
    /// <param name="offsetSeconds">
    /// Where the first beat falls, for a recording that does not start exactly on one. Leading
    /// silence or a pickup bar goes here.
    /// </param>
    /// <param name="markers">Named points in the piece, or null for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tempo or the bar length is not positive.</exception>
    public MusicTimeline(double beatsPerMinute, double beatsPerBar = 4, double offsetSeconds = 0, IReadOnlyList<MusicMarker>? markers = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(beatsPerMinute, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(beatsPerBar, 0);

        BeatsPerMinute = beatsPerMinute;
        BeatsPerBar = beatsPerBar;
        OffsetSeconds = offsetSeconds;
        Markers = markers ?? _noMarkers;
    }

    /// <summary>
    /// Creates a timeline whose grid follows a MIDI tempo map, so beats and bars stay exact through
    /// every tempo change in the piece.
    /// </summary>
    /// <param name="tempoMap">
    /// The map to quantise against. <see cref="CodeBrix.Audio.Synth.MidiSequence.TempoMap"/> is one;
    /// so is the map <see cref="FromMidiEvents"/> builds out of a file's tempo events.
    /// </param>
    /// <param name="beatsPerBar">How many beats make a bar — 4 for common time. Must be positive.</param>
    /// <param name="offsetSeconds">
    /// Where the first beat falls, for a recording that does not start exactly on one. Leading
    /// silence or a pickup bar goes here.
    /// </param>
    /// <param name="markers">Named points in the piece, or null for none.</param>
    /// <remarks>
    /// A beat here is a quarter note, which is the unit the map measures in, so a time signature
    /// whose beat unit is not a quarter note is expressed in <paramref name="beatsPerBar"/> — 6/8 is
    /// three beats to the bar, not six, exactly as for the fixed-tempo constructor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tempoMap"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The bar length is not positive, or the map's initial tempo is not positive.
    /// </exception>
    public MusicTimeline(MidiTempoMap tempoMap, double beatsPerBar = 4, double offsetSeconds = 0, IReadOnlyList<MusicMarker>? markers = null)
    {
        ArgumentNullException.ThrowIfNull(tempoMap);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(beatsPerBar, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tempoMap.InitialBeatsPerMinute, 0, nameof(tempoMap));

        TempoMap = tempoMap;
        BeatsPerMinute = tempoMap.InitialBeatsPerMinute;
        BeatsPerBar = beatsPerBar;
        OffsetSeconds = offsetSeconds;
        Markers = markers ?? _noMarkers;
        HasTempoChanges = !tempoMap.IsConstant;
    }

    /// <summary>
    /// The tempo. For a timeline read from MIDI, a beat is a quarter note; where a
    /// <see cref="TempoMap"/> is present this is the tempo the piece STARTS at.
    /// </summary>
    public double BeatsPerMinute { get; }

    /// <summary>
    /// How many beats make a bar. Fractional for a time signature whose beat unit is not the
    /// tempo's: 6/8 read from a MIDI file is 3 quarter-note beats to the bar, not 6.
    /// </summary>
    public double BeatsPerBar { get; }

    /// <summary>Where the first beat falls, in seconds from the start of the piece.</summary>
    public double OffsetSeconds { get; }

    /// <summary>The named points in the piece, in time order. Empty when there are none.</summary>
    public IReadOnlyList<MusicMarker> Markers { get; }

    /// <summary>
    /// The tempo map the grid follows, or <see langword="null"/> when this timeline is a constant
    /// grid at <see cref="BeatsPerMinute"/>.
    /// </summary>
    /// <remarks>
    /// It is the source's own map, so it converts between time and beats in both directions:
    /// <c>BeatPositionAt</c> for where a moment falls musically, <c>TimeAt</c> for when a beat
    /// happens. <see cref="TimeToNextBoundary"/> uses it for exactly that.
    /// </remarks>
    public MidiTempoMap? TempoMap { get; }

    /// <summary>
    /// Whether the source's tempo varies. When true the grid FOLLOWS the tempo exactly, because a
    /// timeline read from MIDI quantises through the whole <see cref="TempoMap"/>.
    /// </summary>
    public bool HasTempoChanges { get; private init; }

    /// <summary>
    /// The length of one beat at <see cref="BeatsPerMinute"/>. Where a <see cref="TempoMap"/> is
    /// present that is the INITIAL tempo, so this describes the opening of the piece rather than the
    /// whole of it; <see cref="TimeToNextBoundary"/> is what knows where the beats actually are.
    /// </summary>
    public double SecondsPerBeat => 60.0 / BeatsPerMinute;

    /// <summary>
    /// The length of one bar at <see cref="BeatsPerMinute"/>, with the same caveat as
    /// <see cref="SecondsPerBeat"/> for a piece whose tempo varies.
    /// </summary>
    public double SecondsPerBar => SecondsPerBeat * BeatsPerBar;

    /// <summary>
    /// How long to wait, from <paramref name="position"/>, before the next boundary of the requested
    /// kind. Zero when already on one, or when no waiting is asked for.
    /// </summary>
    /// <param name="position">The current playback position.</param>
    /// <param name="quantize">The boundary to wait for.</param>
    /// <returns>The wait.</returns>
    /// <remarks>
    /// With a <see cref="TempoMap"/> the answer is worked out in BEAT space and converted back
    /// through the map, so it is exact on both sides of a tempo change. Without one the grid is the
    /// constant <see cref="SecondsPerBeat"/> / <see cref="SecondsPerBar"/>.
    /// </remarks>
    public TimeSpan TimeToNextBoundary(TimeSpan position, MusicTransitionQuantize quantize)
    {
        if (quantize == MusicTransitionQuantize.Immediate)
        {
            return TimeSpan.Zero;
        }

        var elapsed = position.TotalSeconds - OffsetSeconds;

        if (elapsed < 0)
        {
            // Still in the lead-in: the next boundary is the first beat itself.
            return TimeSpan.FromSeconds(-elapsed);
        }

        if (TempoMap is not null)
        {
            return TimeToNextBoundaryOnMap(elapsed, quantize);
        }

        var grid = quantize == MusicTransitionQuantize.Bar ? SecondsPerBar : SecondsPerBeat;
        if (grid <= 0)
        {
            return TimeSpan.Zero;
        }

        var remaining = grid - (elapsed % grid);

        // Landing exactly on a boundary should go now rather than wait a whole bar for the next one.
        return remaining >= grid - 1e-9 ? TimeSpan.Zero : TimeSpan.FromSeconds(remaining);
    }

    // The same question asked in beats rather than seconds, which is the only way to answer it when
    // the tempo moves: the grid is regular in beat space and irregular in time, so the boundary is
    // found there and converted back through the map.
    private TimeSpan TimeToNextBoundaryOnMap(double elapsed, MusicTransitionQuantize quantize)
    {
        var map = TempoMap!;
        var grid = quantize == MusicTransitionQuantize.Bar ? BeatsPerBar : 1.0;

        if (grid <= 0)
        {
            return TimeSpan.Zero;
        }

        var beat = map.BeatPositionAt(TimeSpan.FromSeconds(elapsed));
        var gridsElapsed = beat / grid;
        var fraction = gridsElapsed - Math.Floor(gridsElapsed);

        // Landing exactly on a boundary should go now rather than wait a whole bar for the next one,
        // the same rule the constant grid applies.
        if (fraction <= 1e-9)
        {
            return TimeSpan.Zero;
        }

        var next = (Math.Floor(gridsElapsed) + 1) * grid;
        var wait = map.TimeAt(next).TotalSeconds - elapsed;

        return wait <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(wait);
    }

    /// <summary>Finds a marker by name; the comparison ignores case.</summary>
    /// <param name="name">The marker's name.</param>
    /// <param name="time">The marker's position, when found.</param>
    /// <returns><see langword="true"/> if a marker of that name exists.</returns>
    public bool TryGetMarker(string name, out TimeSpan time)
    {
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var marker in Markers)
            {
                if (string.Equals(marker.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    time = marker.Time;
                    return true;
                }
            }
        }

        time = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Reads the grid out of a Standard MIDI File: its tempo, its time signature and its markers.
    /// </summary>
    /// <param name="midiFilePath">Path to the <c>.mid</c> file.</param>
    /// <returns>The timeline, or <see langword="null"/> if the file could not be read or carries no tempo.</returns>
    /// <remarks>
    /// <para>
    /// This deliberately parses the file a SECOND time, as
    /// <see cref="CodeBrix.Audio.Midi.MidiFile"/> rather than the
    /// <see cref="CodeBrix.Audio.Synth.MidiSequence"/> that plays it. The two are different models on
    /// purpose: the sequence bakes the tempo map into absolute times and keeps no meta events, so the
    /// grid genuinely is not in it any more. A MIDI file is kilobytes, and this happens once at load.
    /// </para>
    /// <para>
    /// It never throws. A file with no tempo event, or one that cannot be parsed, yields
    /// <see langword="null"/> and a logged warning — a game should not fail to start because a music
    /// asset lacks a marker.
    /// </para>
    /// </remarks>
    public static MusicTimeline? FromMidiFile(string midiFilePath)
    {
        if (string.IsNullOrWhiteSpace(midiFilePath))
        {
            return null;
        }

        try
        {
            // Not strict: a file a game shipped and a synthesizer plays happily should not be
            // rejected here over a spec detail that does not affect its tempo map.
            var file = new MidiFile(midiFilePath, false);
            return FromMidiEvents(file.Events);
        }
        catch (Exception ex)
        {
            Engine.Logger.LogWarning(ex,
                "Could not read a music timeline from '{Path}'. Quantised transitions will fall back to immediate.",
                midiFilePath);

            return null;
        }
    }

    /// <summary>
    /// Reads the grid out of an already-parsed MIDI event collection — for music built in code, or
    /// where the file has been read for other reasons already.
    /// </summary>
    /// <param name="events">The event collection to read.</param>
    /// <returns>The timeline, or <see langword="null"/> when the collection carries no tempo.</returns>
    /// <remarks>
    /// The tempo events are walked ONCE and used twice: to place the markers, and to build the
    /// <see cref="TempoMap"/> the grid then follows. Markers and boundaries therefore agree by
    /// construction — a marker on a bar line is on that bar line.
    /// </remarks>
    public static MusicTimeline? FromMidiEvents(MidiEventCollection events)
    {
        if (events is null)
        {
            return null;
        }

        var ticksPerQuarter = events.DeltaTicksPerQuarterNote;
        if (ticksPerQuarter <= 0)
        {
            return null;
        }

        double? firstTempo = null;
        double beatsPerBar = 4;
        var haveTimeSignature = false;

        // The tempo map, for converting marker ticks to time accurately even when the tempo moves.
        var tempoMap = new List<(long Tick, double Bpm)>();
        var markerEvents = new List<(long Tick, string Name)>();

        for (var track = 0; track < events.Tracks; track++)
        {
            foreach (var midiEvent in events[track])
            {
                switch (midiEvent)
                {
                    case TempoEvent tempo:
                        tempoMap.Add((tempo.AbsoluteTime, tempo.Tempo));
                        if (firstTempo is null || tempo.AbsoluteTime == 0)
                        {
                            firstTempo ??= tempo.Tempo;
                        }

                        break;

                    case TimeSignatureEvent signature when !haveTimeSignature:
                        // Denominator is stored as the power-of-two exponent, so 4/4 arrives as
                        // (4, 2). Bars are measured in the tempo's own unit — quarter notes — so a
                        // 6/8 bar is three beats long, not six.
                        var denominator = 1 << signature.Denominator;
                        if (denominator > 0 && signature.Numerator > 0)
                        {
                            beatsPerBar = signature.Numerator * 4.0 / denominator;
                            haveTimeSignature = true;
                        }

                        break;

                    case TextEvent text when text.MetaEventType == MetaEventType.Marker || text.MetaEventType == MetaEventType.CuePoint:
                        if (!string.IsNullOrWhiteSpace(text.Text))
                        {
                            markerEvents.Add((text.AbsoluteTime, text.Text.Trim()));
                        }

                        break;
                }
            }
        }

        if (firstTempo is null || firstTempo <= 0)
        {
            return null;
        }

        tempoMap.Sort((left, right) => left.Tick.CompareTo(right.Tick));
        markerEvents.Sort((left, right) => left.Tick.CompareTo(right.Tick));

        var markers = new MusicMarker[markerEvents.Count];
        for (var i = 0; i < markerEvents.Count; i++)
        {
            markers[i] = new MusicMarker(
                markerEvents[i].Name,
                TimeSpan.FromSeconds(TicksToSeconds(markerEvents[i].Tick, ticksPerQuarter, tempoMap, firstTempo.Value)));
        }

        return new MusicTimeline(BuildTempoMap(ticksPerQuarter, tempoMap, firstTempo.Value), beatsPerBar, 0, markers);
    }

    /// <summary>
    /// Reads the grid out of a MIDI sequence that has already been parsed for playback — its tempo
    /// map, and nothing else.
    /// </summary>
    /// <param name="sequence">The sequence to read.</param>
    /// <param name="beatsPerBar">
    /// How many beats make a bar — 4 for common time, which is what a file carrying no time
    /// signature means. Must be positive.
    /// </param>
    /// <returns>The timeline, or <see langword="null"/> when <paramref name="sequence"/> is null.</returns>
    /// <remarks>
    /// <para>
    /// NO TIME SIGNATURE AND NO MARKERS, unlike <see cref="FromMidiFile"/>. A
    /// <see cref="CodeBrix.Audio.Synth.MidiSequence"/> keeps its tempo map but not its meta events'
    /// timing: its text metas carry TICKS and the sequence does not expose the tick resolution to
    /// convert them with, so a marker could not be placed even though its name is there. Read the
    /// FILE when the game needs markers or a meter other than the one passed here.
    /// </para>
    /// <para>
    /// What it does give is the exact grid, which is the part that matters for a quantised
    /// transition, and it costs no second parse.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The bar length is not positive.</exception>
    public static MusicTimeline? FromMidiSequence(MidiSequence sequence, double beatsPerBar = 4)
    {
        var map = sequence?.TempoMap;

        return map is null ? null : new MusicTimeline(map, beatsPerBar);
    }

    // Turns the tick-and-tempo pairs the event walk collected into the map the grid quantises
    // against. A tick becomes a beat by dividing by the file's resolution, and a time by the same
    // walk the markers use, so the two cannot disagree.
    private static MidiTempoMap BuildTempoMap(int ticksPerQuarter, List<(long Tick, double Bpm)> tempoMap, double initialBpm)
    {
        var changes = new List<MidiTempoChange>(tempoMap.Count + 1);

        // A file whose first tempo event is not at tick 0 runs its lead-in at that tempo, which is
        // what the marker walk assumes too. Saying so explicitly keeps MidiTempoMap from prepending
        // the MIDI default of 120 and changing the tempo this timeline reports.
        if (tempoMap.Count == 0 || tempoMap[0].Tick > 0)
        {
            changes.Add(new MidiTempoChange(TimeSpan.Zero, 0, initialBpm));
        }

        var seconds = 0.0;
        var lastTick = 0L;
        var bpm = initialBpm;

        foreach (var (changeTick, changeBpm) in tempoMap)
        {
            if (changeTick > lastTick)
            {
                seconds += (changeTick - lastTick) / (double)ticksPerQuarter * (60.0 / bpm);
                lastTick = changeTick;
            }

            bpm = changeBpm;

            // A tempo event that restates the tempo already running is not a change: it must not
            // make HasTempoChanges true, and it would move no boundary anyway.
            if (changes.Count > 0 && changes[^1].BeatsPerMinute.Equals(changeBpm))
            {
                continue;
            }

            changes.Add(new MidiTempoChange(
                TimeSpan.FromSeconds(seconds),
                changeTick / (double)ticksPerQuarter,
                changeBpm));
        }

        return new MidiTempoMap(changes);
    }

    // Walks the tempo map so a marker's time is right even in a piece that changes tempo.
    private static double TicksToSeconds(long tick, int ticksPerQuarter, List<(long Tick, double Bpm)> tempoMap, double initialBpm)
    {
        var seconds = 0.0;
        var lastTick = 0L;
        var bpm = initialBpm;

        foreach (var (changeTick, changeBpm) in tempoMap)
        {
            if (changeTick >= tick)
            {
                break;
            }

            if (changeTick > lastTick)
            {
                seconds += (changeTick - lastTick) / (double)ticksPerQuarter * (60.0 / bpm);
                lastTick = changeTick;
            }

            bpm = changeBpm;
        }

        seconds += (tick - lastTick) / (double)ticksPerQuarter * (60.0 / bpm);
        return seconds;
    }
}
