using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Wave;

namespace MusicDemo.Game;

/// <summary>
/// Generates every asset this sample plays — layers, two linear tracks, a stinger, an SFZ and a
/// Decent Sampler instrument, two MIDI files and a whole stems export — into a folder beside the
/// executable, the first time the sample runs.
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
/// It is not music. It is layers that agree about tempo and key, which is exactly what the stem and
/// quantisation features need in order to be demonstrable.
/// </para>
/// <para>
/// TWO OF THE ASSETS ARE FOLDERS, NOT FILES. A <c>.dspreset</c> points at sample files beside it,
/// and a stems export is a set of files that belong together and are named for each other. Both are
/// written as the real thing is laid out, because that is the part a game gets wrong.
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

    // The generated stems export is sixteen beats long, and its tempo changes at the halfway mark.
    private const int StemsBeatCount = 16;

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
    /// The Decent Sampler instrument the second MIDI track is rendered through: a preset, and the
    /// <c>Samples</c> folder beside it that the preset points at.
    /// </summary>
    public static string DecentSamplerPresetPath { get; } =
        Path.Combine(AssetDirectory, "DemoSampler", "Demo Instrument.dspreset");

    /// <summary>
    /// A MIDI file whose tempo CHANGES partway through, so a bar-quantised transition has something
    /// to be exact about.
    /// </summary>
    public static string TempoChangeMidiPath { get; } = Path.Combine(AssetDirectory, "tempo-change.mid");

    /// <summary>
    /// A stems export laid out the way a download from a music service is — several stem files and
    /// a MIDI file side by side, named "&lt;Title&gt; (&lt;Stem&gt;).&lt;ext&gt;".
    /// </summary>
    public static string StemsExportFolder { get; } = Path.Combine(AssetDirectory, "Fake Song Stems");

    /// <summary>The tempo the second half of <see cref="TempoChangeMidiPath"/> runs at.</summary>
    public const double SlowerBeatsPerMinute = 90;

    /// <summary>How many bars of <see cref="TempoChangeMidiPath"/> run at the opening tempo.</summary>
    public const int BarsBeforeTempoChange = 2;

    /// <summary>
    /// The rate the generated stems export is written at — 48 kHz, which is what a real stems
    /// download carries, and deliberately NOT the rate this sample pins the device to.
    /// </summary>
    public const int StemsExportSampleRate = 48000;

    /// <summary>The tempo the generated stems export starts at.</summary>
    public const double StemsBeatsPerMinute = 100;

    /// <summary>The tempo the generated stems export finishes at.</summary>
    public const double StemsSecondBeatsPerMinute = 120;

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
        EnsureDecentSamplerInstrument();
        EnsureTempoChangeMidi();
        EnsureStemsExport();
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
            AddNote(melody, i * beat, channel: 1, melodyNotes[i % melodyNotes.Length], velocity: 100, length: beat);
        }

        melody.Add(new MetaEvent(MetaEventType.EndTrack, 0, bar * 4));

        var harmony = events.AddTrack();
        for (var i = 0; i < 8; i++)
        {
            AddNote(harmony, i * beat * 2, channel: 2, note: 48, velocity: 90, length: beat * 2);
            AddNote(harmony, i * beat * 2, channel: 2, note: 52, velocity: 90, length: beat * 2);
        }

        harmony.Add(new MetaEvent(MetaEventType.EndTrack, 0, bar * 4));

        events.PrepareForExport();
        MidiFile.Export(MidiPath, events);
    }

    // ----- the Decent Sampler instrument -----

    // A .dspreset is not one file: it POINTS AT sample files beside it, so the folder is as much
    // part of the instrument as the XML is. Written here the same way as everything else, so the
    // sample still ships no binary assets.
    private static void EnsureDecentSamplerInstrument()
    {
        var instrumentFolder = Path.GetDirectoryName(DecentSamplerPresetPath);
        var samplesFolder = Path.Combine(instrumentFolder, "Samples");
        Directory.CreateDirectory(samplesFolder);

        var samplePath = Path.Combine(samplesFolder, "bell.wav");

        if (!File.Exists(samplePath))
        {
            // A bell-ish middle C: a fundamental and two partials under a long decay, so the
            // instrument sounds different from the SFZ one and a switch between them is audible.
            var frames = (int)(SampleRate * 1.5);
            var samples = new float[frames];

            for (var frame = 0; frame < frames; frame++)
            {
                var t = frame / (double)SampleRate;
                var envelope = Math.Min(1.0, t * 200) * Math.Exp(-2.2 * t);
                var value = Math.Sin(2 * Math.PI * 261.63 * t)
                            + (0.5 * Math.Sin(2 * Math.PI * 523.25 * t))
                            + (0.25 * Math.Sin(2 * Math.PI * 784.0 * t));

                samples[frame] = (float)(value * envelope * 0.22);
            }

            WaveFileWriter.CreateWaveFile16(samplePath, new BufferSampleProvider(samples, SampleRate, 1));
        }

        if (File.Exists(DecentSamplerPresetPath))
        {
            return;
        }

        // One group, one sample across the keyboard, and a labeled knob bound to the group's
        // volume — the smallest preset that still exercises the format's UI-to-parameter binding.
        File.WriteAllText(DecentSamplerPresetPath,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine +
            "<!-- Generated by the CodeBrix.Platform.GameEngine MusicDemo sample. -->" + Environment.NewLine +
            "<DecentSampler minVersion=\"1.0.0\">" + Environment.NewLine +
            "  <ui width=\"812\" height=\"375\">" + Environment.NewLine +
            "    <tab name=\"main\">" + Environment.NewLine +
            "      <labeled-knob x=\"40\" y=\"40\" width=\"60\" height=\"70\" label=\"Level\"" +
            " minValue=\"0\" maxValue=\"1\" value=\"1\">" + Environment.NewLine +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />" + Environment.NewLine +
            "      </labeled-knob>" + Environment.NewLine +
            "    </tab>" + Environment.NewLine +
            "  </ui>" + Environment.NewLine +
            "  <groups>" + Environment.NewLine +
            "    <group name=\"bells\" tags=\"tuned\">" + Environment.NewLine +
            "      <sample path=\"Samples/bell.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" />" + Environment.NewLine +
            "    </group>" + Environment.NewLine +
            "  </groups>" + Environment.NewLine +
            "</DecentSampler>" + Environment.NewLine);
    }

    // A file that changes tempo partway through, so "on the next bar" has something to be exact
    // about: quantising against the opening tempo alone would answer wrong from the change onwards.
    private static void EnsureTempoChangeMidi()
    {
        if (File.Exists(TempoChangeMidiPath))
        {
            return;
        }

        var events = new MidiEventCollection(1, TicksPerQuarter);
        var beat = TicksPerQuarter;
        var bar = beat * BeatsPerBar;
        var changeTick = bar * BarsBeforeTempoChange;
        var endTick = bar * 6;

        var tempoTrack = events.AddTrack();
        tempoTrack.Add(new TempoEvent((int)Math.Round(60_000_000.0 / BeatsPerMinute), 0));
        tempoTrack.Add(new TimeSignatureEvent(0, BeatsPerBar, 2, 24, 8));
        tempoTrack.Add(new TextEvent("fast", MetaEventType.Marker, 0));
        tempoTrack.Add(new TempoEvent((int)Math.Round(60_000_000.0 / SlowerBeatsPerMinute), changeTick));
        tempoTrack.Add(new TextEvent("slow", MetaEventType.Marker, changeTick));
        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, endTick));

        var melody = events.AddTrack();
        var notes = new[] { 60, 64, 67, 72, 67, 64 };

        for (var i = 0; i < endTick / beat; i++)
        {
            AddNote(melody, i * beat, channel: 1, notes[i % notes.Length], velocity: 96, length: beat);
        }

        melody.Add(new MetaEvent(MetaEventType.EndTrack, 0, endTick));

        events.PrepareForExport();
        MidiFile.Export(TempoChangeMidiPath, events);
    }

    // ----- the stems export -----

    // A folder shaped exactly like a stems download: "<Title> (<Stem>).wav" files side by side, all
    // the same length, rate and channel count, with the song's MIDI beside them. It is written at
    // 48 kHz on purpose — a real download is, and this sample pins the device to 44.1 kHz, so the
    // stems rate-convert as they decode rather than being rejected for disagreeing.
    private static void EnsureStemsExport()
    {
        Directory.CreateDirectory(StemsExportFolder);

        var beats = StemsBeatTimes();

        WriteStemIfMissing("Vocals", () => StemsPad(beats));
        WriteStemIfMissing("Drums", () => StemsPulse(beats, frequency: 180, gain: 0.30, decay: 26));
        WriteStemIfMissing("Bass", () => StemsPulse(beats, frequency: 65.41, gain: 0.34, decay: 6));

        EnsureStemsMidi();
    }

    private static void WriteStemIfMissing(string stemName, Func<float[]> render)
    {
        var path = Path.Combine(StemsExportFolder, $"Fake Song ({stemName}).wav");

        if (!File.Exists(path))
        {
            WaveFileWriter.CreateWaveFile16(path,
                new BufferSampleProvider(render(), StemsExportSampleRate, 2));
        }
    }

    // Where each beat of the export falls. The tempo CHANGES halfway, and the audio is generated
    // from these times, so the recordings and the MIDI beside them agree about where the beats are.
    private static double[] StemsBeatTimes()
    {
        var times = new double[StemsBeatCount + 1];
        var time = 0.0;

        for (var i = 0; i <= StemsBeatCount; i++)
        {
            times[i] = time;
            time += 60.0 / (i < StemsBeatCount / 2 ? StemsBeatsPerMinute : StemsSecondBeatsPerMinute);
        }

        return times;
    }

    private static float[] StemsPad(double[] beats)
    {
        var buffer = NewStemsBuffer(beats);
        var frames = buffer.Length / 2;
        var length = beats[StemsBeatCount];
        var voices = new[] { 261.63, 329.63, 392.00 };

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)StemsExportSampleRate;
            var swell = 0.5 + (0.5 * Math.Sin(2 * Math.PI * t / length));

            double value = 0;
            foreach (var voice in voices)
            {
                value += Math.Sin(2 * Math.PI * voice * t);
            }

            value *= 0.09 * (0.4 + (0.6 * swell)) / voices.Length;

            buffer[(frame * 2) + 0] = (float)value;
            buffer[(frame * 2) + 1] = (float)value;
        }

        return buffer;
    }

    private static float[] StemsPulse(double[] beats, double frequency, double gain, double decay)
    {
        var buffer = NewStemsBuffer(beats);
        var frames = buffer.Length / 2;

        for (var beat = 0; beat < StemsBeatCount; beat++)
        {
            var start = (int)(beats[beat] * StemsExportSampleRate);

            for (var frame = start; frame < frames; frame++)
            {
                var t = (frame - start) / (double)StemsExportSampleRate;
                var envelope = Math.Exp(-decay * t);

                if (envelope < 0.001)
                {
                    break;
                }

                var value = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * gain);

                buffer[(frame * 2) + 0] += value;
                buffer[(frame * 2) + 1] += value;
            }
        }

        return buffer;
    }

    private static float[] NewStemsBuffer(double[] beats)
        => new float[(int)(StemsExportSampleRate * beats[StemsBeatCount]) * 2];

    // The MIDI a stems download ships beside the recordings: a tempo event on EVERY beat (which is
    // what "follow tempo changes" produces), one part, and no time signature.
    private static void EnsureStemsMidi()
    {
        var path = Path.Combine(StemsExportFolder, "Fake Song (Drums).mid");

        if (File.Exists(path))
        {
            return;
        }

        const int stemsTicksPerQuarter = 480;

        var events = new MidiEventCollection(1, stemsTicksPerQuarter);
        var tempoTrack = events.AddTrack();

        for (var beat = 0; beat < StemsBeatCount; beat++)
        {
            var beatsPerMinute = beat < StemsBeatCount / 2 ? StemsBeatsPerMinute : StemsSecondBeatsPerMinute;
            tempoTrack.Add(new TempoEvent((int)Math.Round(60_000_000.0 / beatsPerMinute), beat * stemsTicksPerQuarter));
        }

        tempoTrack.Add(new MetaEvent(MetaEventType.EndTrack, 0, StemsBeatCount * stemsTicksPerQuarter));

        var part = events.AddTrack();

        for (var beat = 0; beat < StemsBeatCount; beat++)
        {
            AddNote(part, beat * stemsTicksPerQuarter, channel: 10, note: 36, velocity: 100,
                length: stemsTicksPerQuarter / 2);
        }

        part.Add(new MetaEvent(MetaEventType.EndTrack, 0, StemsBeatCount * stemsTicksPerQuarter));

        events.PrepareForExport();
        MidiFile.Export(path, events);
    }

    // A note is TWO events. PrepareForExport sorts a track and closes it, but it does not release
    // anything: a note added without its off event is still sounding when the track ends, which the
    // reader reports as a problem with the file (and every player then has to guess about).
    private static void AddNote(IList<MidiEvent> track, long tick, int channel, int note, int velocity, int length)
    {
        var noteOn = new NoteOnEvent(tick, channel, note, velocity, length);

        track.Add(noteOn);
        track.Add(noteOn.OffEvent);
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
