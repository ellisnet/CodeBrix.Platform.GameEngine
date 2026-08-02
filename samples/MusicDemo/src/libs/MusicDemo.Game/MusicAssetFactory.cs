using System;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Wave;

namespace MusicDemo.Game;

/// <summary>
/// Generates every asset this sample plays — stems, two linear tracks, a stinger, an SFZ instrument
/// and a MIDI file — into a folder beside the executable, the first time the sample runs.
/// </summary>
/// <remarks>
/// <para>
/// WHY GENERATE RATHER THAN SHIP FILES: the repository holds no binary music assets, and a sample
/// that needs some cannot be run by anyone who does not have them. Everything here is written from
/// arithmetic, so the sample is self-contained on every platform and every checkout, and the
/// generated files are ordinary <c>.wav</c> / <c>.mid</c> / <c>.sfz</c> that can be opened in any
/// editor to see what the engine was given.
/// </para>
/// <para>
/// It is not music. It is three layers that agree about tempo and key, which is exactly what the
/// stem and quantisation features need in order to be demonstrable.
/// </para>
/// </remarks>
public static class MusicAssetFactory
{
    /// <summary>The tempo everything is generated at, and the tempo the demo tells the engine about.</summary>
    public const double BeatsPerMinute = 120;

    /// <summary>The time signature everything is generated in.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>The sample rate everything is generated at; also what the demo pins the device to.</summary>
    public const int SampleRate = 44100;

    private const int TicksPerQuarter = 96;
    private const int Bars = 4;

    private static readonly double _secondsPerBeat = 60.0 / BeatsPerMinute;
    private static readonly double _loopSeconds = _secondsPerBeat * BeatsPerBar * Bars; // 8 s

    /// <summary>Where the generated assets live.</summary>
    public static string AssetDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "GeneratedMusic");

    /// <summary>The three layers of the adaptive set. All the same length, rate and channel count.</summary>
    public static string[] StemPaths { get; } =
    {
        Path.Combine(AssetDirectory, "stem-pad.wav"),
        Path.Combine(AssetDirectory, "stem-bass.wav"),
        Path.Combine(AssetDirectory, "stem-lead.wav"),
    };

    /// <summary>The names the stems are addressed by.</summary>
    public static string[] StemNames { get; } = { "pad", "bass", "lead" };

    /// <summary>A linear piece, for the transport and crossfade controls.</summary>
    public static string TrackAPath { get; } = Path.Combine(AssetDirectory, "track-a.wav");

    /// <summary>A second linear piece in a different key, so a crossfade between them is audible.</summary>
    public static string TrackBPath { get; } = Path.Combine(AssetDirectory, "track-b.wav");

    /// <summary>A short one-shot fanfare, for the stinger control.</summary>
    public static string StingerPath { get; } = Path.Combine(AssetDirectory, "stinger.wav");

    /// <summary>A dialogue-length blip, for the ducking control.</summary>
    public static string VoicePath { get; } = Path.Combine(AssetDirectory, "voice.wav");

    /// <summary>The SFZ instrument the MIDI track is rendered through.</summary>
    public static string InstrumentPath { get; } = Path.Combine(AssetDirectory, "instrument.sfz");

    /// <summary>A MIDI file carrying a tempo, a time signature and two markers.</summary>
    public static string MidiPath { get; } = Path.Combine(AssetDirectory, "theme.mid");

    /// <summary>
    /// Writes every asset if it is not already there. Safe to call more than once; it is skipped
    /// entirely on later runs.
    /// </summary>
    public static void EnsureAssets()
    {
        Directory.CreateDirectory(AssetDirectory);

        // A chord that agrees with itself: C major, one layer per role.
        WriteIfMissing(StemPaths[0], () => Pad(new[] { 261.63, 329.63, 392.00 }));
        WriteIfMissing(StemPaths[1], () => Pulse(65.41, notesPerBar: 4, duty: 0.45));
        WriteIfMissing(StemPaths[2], () => Arpeggio(new[] { 523.25, 659.25, 783.99, 659.25 }));

        WriteIfMissing(TrackAPath, () => Pad(new[] { 261.63, 329.63, 392.00 }, withPulse: true));
        WriteIfMissing(TrackBPath, () => Pad(new[] { 220.00, 261.63, 329.63 }, withPulse: true));

        WriteIfMissing(StingerPath, Stinger);
        WriteIfMissing(VoicePath, Voice);

        EnsureInstrument();
        EnsureMidi();
    }

    private static void WriteIfMissing(string path, Func<float[]> render)
    {
        if (File.Exists(path))
        {
            return;
        }

        WaveFileWriter.CreateWaveFile16(path, new BufferSampleProvider(render(), SampleRate, 2));
    }

    // ----- the layers -----

    // A sustained chord with a slow swell. Stereo, with the voices spread a little.
    private static float[] Pad(double[] frequencies, bool withPulse = false)
    {
        var buffer = NewLoopBuffer();
        var frames = buffer.Length / 2;

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)SampleRate;
            var swell = 0.5 + (0.5 * Math.Sin(2 * Math.PI * t / _loopSeconds));

            double left = 0, right = 0;
            for (var voice = 0; voice < frequencies.Length; voice++)
            {
                var value = Math.Sin(2 * Math.PI * frequencies[voice] * t);
                var pan = frequencies.Length == 1 ? 0.5 : voice / (double)(frequencies.Length - 1);
                left += value * (1 - (pan * 0.6));
                right += value * (0.4 + (pan * 0.6));
            }

            var gain = 0.10 * (0.4 + (0.6 * swell)) / frequencies.Length;
            buffer[(frame * 2) + 0] += (float)(left * gain);
            buffer[(frame * 2) + 1] += (float)(right * gain);
        }

        if (withPulse)
        {
            AddPulse(buffer, frequencies[0] / 4, notesPerBar: 4, duty: 0.4, gain: 0.18);
        }

        return ApplyLoopEdgeFade(buffer);
    }

    // A note on every beat, so the pulse a listener quantises against is unmistakable.
    private static float[] Pulse(double frequency, int notesPerBar, double duty)
    {
        var buffer = NewLoopBuffer();
        AddPulse(buffer, frequency, notesPerBar, duty, gain: 0.22);
        return ApplyLoopEdgeFade(buffer);
    }

    private static void AddPulse(float[] buffer, double frequency, int notesPerBar, double duty, double gain)
    {
        var frames = buffer.Length / 2;
        var noteFrames = (int)(SampleRate * _secondsPerBeat * BeatsPerBar / notesPerBar);
        var soundingFrames = (int)(noteFrames * duty);

        for (var frame = 0; frame < frames; frame++)
        {
            var intoNote = frame % noteFrames;
            if (intoNote >= soundingFrames)
            {
                continue;
            }

            var t = frame / (double)SampleRate;
            var envelope = 1.0 - (intoNote / (double)soundingFrames);
            var value = Math.Sin(2 * Math.PI * frequency * t) * envelope * envelope * gain;

            buffer[(frame * 2) + 0] += (float)value;
            buffer[(frame * 2) + 1] += (float)value;
        }
    }

    // Eighth notes around the chord, the layer a game would bring in for "things got interesting".
    private static float[] Arpeggio(double[] frequencies)
    {
        var buffer = NewLoopBuffer();
        var frames = buffer.Length / 2;
        var noteFrames = (int)(SampleRate * _secondsPerBeat / 2);

        for (var frame = 0; frame < frames; frame++)
        {
            var note = frame / noteFrames;
            var intoNote = frame % noteFrames;
            var frequency = frequencies[note % frequencies.Length];

            var t = frame / (double)SampleRate;
            var envelope = Math.Max(0, 1.0 - (intoNote / (double)(noteFrames * 0.8)));
            var value = Math.Sin(2 * Math.PI * frequency * t) * envelope * envelope * 0.14;

            buffer[(frame * 2) + 0] += (float)value;
            buffer[(frame * 2) + 1] += (float)value;
        }

        return ApplyLoopEdgeFade(buffer);
    }

    // A rising figure, about a second and a bit: what a level-complete cue sounds like.
    private static float[] Stinger()
    {
        var notes = new[] { 523.25, 659.25, 783.99, 1046.50 };
        var noteFrames = SampleRate / 5;
        var buffer = new float[noteFrames * notes.Length * 2 * 2];
        var frames = buffer.Length / 2;

        for (var frame = 0; frame < frames; frame++)
        {
            var note = Math.Min(frame / noteFrames, notes.Length - 1);
            var t = frame / (double)SampleRate;
            var envelope = Math.Max(0, 1.0 - (frame / (double)frames));
            var value = Math.Sin(2 * Math.PI * notes[note] * t) * envelope * 0.28;

            buffer[(frame * 2) + 0] = (float)value;
            buffer[(frame * 2) + 1] = (float)value;
        }

        return buffer;
    }

    // A two-second warble standing in for a line of dialogue, so ducking has something to duck under.
    private static float[] Voice()
    {
        var frames = SampleRate * 2;
        var buffer = new float[frames * 2];

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)SampleRate;
            var wobble = 220 + (40 * Math.Sin(2 * Math.PI * 5 * t));
            var envelope = Math.Min(1.0, Math.Min(t * 8, (frames - frame) / (double)SampleRate * 4));
            var value = Math.Sin(2 * Math.PI * wobble * t) * envelope * 0.30;

            buffer[(frame * 2) + 0] = (float)value;
            buffer[(frame * 2) + 1] = (float)value;
        }

        return buffer;
    }

    private static float[] NewLoopBuffer() => new float[(int)(SampleRate * _loopSeconds) * 2];

    // A few milliseconds of fade at each end, so a looping stem does not click at the seam.
    private static float[] ApplyLoopEdgeFade(float[] buffer)
    {
        var frames = buffer.Length / 2;
        var fadeFrames = Math.Min(SampleRate / 200, frames / 2); // 5 ms

        for (var i = 0; i < fadeFrames; i++)
        {
            var gain = (float)(i / (double)fadeFrames);

            buffer[(i * 2) + 0] *= gain;
            buffer[(i * 2) + 1] *= gain;

            var tail = frames - 1 - i;
            buffer[(tail * 2) + 0] *= gain;
            buffer[(tail * 2) + 1] *= gain;
        }

        return buffer;
    }

    // ----- the MIDI side -----

    private static void EnsureInstrument()
    {
        var samplePath = Path.Combine(AssetDirectory, "instrument-tone.wav");

        if (!File.Exists(samplePath))
        {
            // One second of middle C with a soft attack, mapped across the keyboard by the SFZ.
            var frames = SampleRate;
            var samples = new float[frames];

            for (var frame = 0; frame < frames; frame++)
            {
                var t = frame / (double)SampleRate;
                var envelope = Math.Min(1.0, t * 40) * Math.Max(0, 1.0 - t);
                samples[frame] = (float)(Math.Sin(2 * Math.PI * 261.63 * t) * envelope * 0.5);
            }

            WaveFileWriter.CreateWaveFile16(samplePath, new BufferSampleProvider(samples, SampleRate, 1));
        }

        if (!File.Exists(InstrumentPath))
        {
            File.WriteAllText(InstrumentPath,
                "// Generated by the CodeBrix.Platform.GameEngine MusicDemo sample." + Environment.NewLine +
                "<region> sample=instrument-tone.wav lokey=0 hikey=127 pitch_keycenter=60 loop_mode=no_loop" + Environment.NewLine);
        }
    }

    private static void EnsureMidi()
    {
        if (File.Exists(MidiPath))
        {
            return;
        }

        var events = new MidiEventCollection(1, TicksPerQuarter);
        var beat = TicksPerQuarter;
        var bar = beat * BeatsPerBar;

        // Track 0 is the tempo map, and the markers that become the demo's jump points.
        var tempoTrack = events.AddTrack();
        tempoTrack.Add(new TempoEvent((int)Math.Round(60_000_000.0 / BeatsPerMinute), 0));
        tempoTrack.Add(new TimeSignatureEvent(0, BeatsPerBar, 2, 24, 8));
        tempoTrack.Add(new TextEvent("verse", MetaEventType.Marker, 0));
        tempoTrack.Add(new TextEvent("chorus", MetaEventType.Marker, bar * 2));
        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, bar * 4));

        // Channel 0 is the melody and channel 1 the harmony, so the demo has two layers to mix.
        var melody = events.AddTrack();
        var melodyNotes = new[] { 60, 62, 64, 67, 64, 62, 60, 55 };
        for (var i = 0; i < melodyNotes.Length * 2; i++)
        {
            melody.Add(new NoteOnEvent(i * beat, 1, melodyNotes[i % melodyNotes.Length], 100, beat));
        }

        melody.Add(new MetaEvent(MetaEventType.EndTrack, 0, bar * 4));

        var harmony = events.AddTrack();
        for (var i = 0; i < 8; i++)
        {
            harmony.Add(new NoteOnEvent(i * beat * 2, 2, 48, 90, beat * 2));
            harmony.Add(new NoteOnEvent(i * beat * 2, 2, 52, 90, beat * 2));
        }

        harmony.Add(new MetaEvent(MetaEventType.EndTrack, 0, bar * 4));

        events.PrepareForExport();
        MidiFile.Export(MidiPath, events);
    }

    /// <summary>Plays a float array straight out, so a generated buffer can be written to a file.</summary>
    private sealed class BufferSampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        internal BufferSampleProvider(float[] samples, int sampleRate, int channels)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, _samples.Length - _position);
            if (count <= 0)
            {
                return 0;
            }

            _samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
