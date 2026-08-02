using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
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
/// the markers out of the file, and <see cref="MidiMusicTrack"/> does it automatically when it is
/// constructed from a path. Nothing needs to be measured or typed in.
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
/// THE GRID IS CONSTANT. One tempo, one time signature, running the length of the piece. A file that
/// changes tempo part-way is reported through <see cref="HasTempoChanges"/> and quantised against
/// its FIRST tempo, so bar boundaries drift after the change. Markers are exempt: they are converted
/// through the whole tempo map, so a jump point stays exactly where the composer put it.
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

    /// <summary>The tempo. For a timeline read from MIDI, a beat is a quarter note.</summary>
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
    /// Whether the source changed tempo after its first tempo event. When true, this timeline
    /// describes the FIRST tempo only, and bar boundaries after the change are approximate.
    /// </summary>
    public bool HasTempoChanges { get; private init; }

    /// <summary>The length of one beat.</summary>
    public double SecondsPerBeat => 60.0 / BeatsPerMinute;

    /// <summary>The length of one bar.</summary>
    public double SecondsPerBar => SecondsPerBeat * BeatsPerBar;

    /// <summary>
    /// How long to wait, from <paramref name="position"/>, before the next boundary of the requested
    /// kind. Zero when already on one, or when no waiting is asked for.
    /// </summary>
    /// <param name="position">The current playback position.</param>
    /// <param name="quantize">The boundary to wait for.</param>
    /// <returns>The wait.</returns>
    public TimeSpan TimeToNextBoundary(TimeSpan position, MusicTransitionQuantize quantize)
    {
        if (quantize == MusicTransitionQuantize.Immediate)
        {
            return TimeSpan.Zero;
        }

        var grid = quantize == MusicTransitionQuantize.Bar ? SecondsPerBar : SecondsPerBeat;
        if (grid <= 0)
        {
            return TimeSpan.Zero;
        }

        var elapsed = position.TotalSeconds - OffsetSeconds;

        if (elapsed < 0)
        {
            // Still in the lead-in: the next boundary is the first beat itself.
            return TimeSpan.FromSeconds(-elapsed);
        }

        var remaining = grid - (elapsed % grid);

        // Landing exactly on a boundary should go now rather than wait a whole bar for the next one.
        return remaining >= grid - 1e-9 ? TimeSpan.Zero : TimeSpan.FromSeconds(remaining);
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
        var tempoCount = 0;
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
                        tempoCount++;
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

        return new MusicTimeline(firstTempo.Value, beatsPerBar, 0, markers)
        {
            HasTempoChanges = tempoCount > 1,
        };
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
