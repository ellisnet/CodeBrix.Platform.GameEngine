using System;
using System.IO;
using System.Text;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Builds the instrument, audio and MIDI fixtures the music tests load, entirely in code: a minimal
/// SoundFont, a minimal Decent Sampler preset over a generated sample, plain WAV files, and
/// Standard MIDI Files that break a rule on purpose.
/// </summary>
/// <remarks>
/// <para>
/// Nothing binary is committed to this repository. A real SoundFont or sample library runs to tens
/// or hundreds of megabytes and is variously licensed, and a test that needs one to run is a test
/// nobody runs. Everything here is generated from sine tones and written to a temp directory, so the
/// tests exercise a genuinely parsed instrument rather than a hand-made object.
/// </para>
/// <para>
/// The SoundFont writer is the smallest file the format allows: one preset, one instrument, one zone
/// over one sample, and the terminator record every list needs. It is enough to load and to render;
/// it is not a general-purpose encoder.
/// </para>
/// </remarks>
internal static class SyntheticInstrumentAssets
{
    private const int SampleRate = 44100;

    // The SoundFont specification asks for 46 zero samples after the last one, and the loader keeps
    // a four-sample margin of its own on top of that.
    private const int TrailingZeroSamples = 46;

    /// <summary>Writes a one-note SoundFont and returns its path.</summary>
    /// <param name="path">Where to write the <c>.sf2</c> file.</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string WriteSoundFont(string path)
    {
        const int frames = 4410;

        var samples = new short[frames + TrailingZeroSamples];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (short)(8000 * Math.Sin(2.0 * Math.PI * 440.0 * i / SampleRate));
        }

        var info = Chunk("LIST", writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("INFO"));
            WriteChunk(writer, "ifil", inner =>
            {
                inner.Write((short)2);
                inner.Write((short)1);
            });
            WriteChunk(writer, "isng", inner => inner.Write(FixedString("EMU8000", 8)));
            WriteChunk(writer, "INAM", inner => inner.Write(FixedString("CodeBrix Test", 16)));
        });

        var sampleData = Chunk("LIST", writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("sdta"));
            WriteChunk(writer, "smpl", inner =>
            {
                foreach (var sample in samples)
                {
                    inner.Write(sample);
                }
            });
        });

        var parameters = Chunk("LIST", writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("pdta"));

            // One preset, then the end-of-presets terminator whose bag index closes the first one.
            WriteChunk(writer, "phdr", inner =>
            {
                WritePresetRecord(inner, "Tone", 0);
                WritePresetRecord(inner, "EOP", 1);
            });

            WriteChunk(writer, "pbag", inner =>
            {
                WriteBagRecord(inner, 0);
                WriteBagRecord(inner, 1);
            });

            // No modulators, but the list must exist and hold its terminator record.
            WriteChunk(writer, "pmod", inner => inner.Write(new byte[10]));

            WriteChunk(writer, "pgen", inner =>
            {
                WriteGenerator(inner, 41, 0);  // instrument = the one below
                WriteGenerator(inner, 0, 0);   // terminator
            });

            WriteChunk(writer, "inst", inner =>
            {
                WriteInstrumentRecord(inner, "Tone", 0);
                WriteInstrumentRecord(inner, "EOI", 1);
            });

            WriteChunk(writer, "ibag", inner =>
            {
                WriteBagRecord(inner, 0);
                WriteBagRecord(inner, 2);
            });

            WriteChunk(writer, "imod", inner => inner.Write(new byte[10]));

            // The sample generator must come LAST in a zone, or the loader reads the zone as a
            // global one and the instrument ends up with no regions at all.
            WriteChunk(writer, "igen", inner =>
            {
                WriteGenerator(inner, 43, 127 << 8); // key range 0-127
                WriteGenerator(inner, 53, 0);        // sample id
                WriteGenerator(inner, 0, 0);         // terminator
            });

            WriteChunk(writer, "shdr", inner =>
            {
                WriteSampleHeader(inner, "Tone", 0, frames, 100, frames - 100);
                WriteSampleHeader(inner, "EOS", 0, 0, 0, 0);
            });
        });

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var file = new BinaryWriter(stream);

        file.Write(Encoding.ASCII.GetBytes("RIFF"));
        file.Write(4 + info.Length + sampleData.Length + parameters.Length);
        file.Write(Encoding.ASCII.GetBytes("sfbk"));
        file.Write(info);
        file.Write(sampleData);
        file.Write(parameters);

        return path;
    }

    /// <summary>
    /// Writes a one-group Decent Sampler preset, its sample and a knob bound to the group's volume,
    /// and returns the preset's path.
    /// </summary>
    /// <param name="directory">The folder to build the instrument in. Created if it does not exist.</param>
    /// <param name="presetFileName">The preset's file name, including its extension.</param>
    /// <returns>The full path of the preset file.</returns>
    /// <remarks>
    /// The <c>Samples</c> folder beside the preset is the point of the fixture as much as the XML is:
    /// a preset is not one file, and a test that proves it loads has to prove the sample beside it
    /// resolved too.
    /// </remarks>
    public static string WriteDecentSamplerPreset(string directory, string presetFileName = "Test Instrument.dspreset")
    {
        Directory.CreateDirectory(Path.Combine(directory, "Samples"));

        WriteMonoWav(Path.Combine(directory, "Samples", "tone.wav"), seconds: 0.25, frequency: 261.63);

        var presetPath = Path.Combine(directory, presetFileName);

        File.WriteAllText(presetPath,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine +
            "<DecentSampler minVersion=\"1.0.0\">" + Environment.NewLine +
            "  <ui width=\"812\" height=\"375\">" + Environment.NewLine +
            "    <tab name=\"main\">" + Environment.NewLine +
            "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" label=\"Level\"" +
            " minValue=\"0\" maxValue=\"1\" value=\"1\">" + Environment.NewLine +
            "        <binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"AMP_VOLUME\" />" + Environment.NewLine +
            "      </labeled-knob>" + Environment.NewLine +
            "    </tab>" + Environment.NewLine +
            "  </ui>" + Environment.NewLine +
            "  <groups>" + Environment.NewLine +
            "    <group name=\"main\" tags=\"body\">" + Environment.NewLine +
            "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" />" + Environment.NewLine +
            "    </group>" + Environment.NewLine +
            "  </groups>" + Environment.NewLine +
            "</DecentSampler>" + Environment.NewLine);

        return presetPath;
    }

    /// <summary>
    /// Writes a Standard MIDI File whose key signature carries a sharps/flats byte the
    /// specification does not allow, and returns its path.
    /// </summary>
    /// <param name="path">Where to write the <c>.mid</c> file.</param>
    /// <param name="beatsPerMinute">The tempo to write at the top of the file.</param>
    /// <returns><paramref name="path"/>.</returns>
    /// <remarks>
    /// The bytes are written by hand because the typed key-signature event refuses to BUILD an
    /// out-of-range value — quite right of it — and this fixture has to exist as a file before the
    /// lenient reader can be shown forgiving it. Machine-generated stem exports do exactly this.
    /// </remarks>
    public static string WriteMidiWithAnOutOfRangeKeySignature(string path, double beatsPerMinute = 120)
    {
        const int ticksPerQuarter = 96;
        var microsecondsPerQuarter = (int)Math.Round(60_000_000.0 / beatsPerMinute);

        var meta = Track(writer =>
        {
            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0xFF, 0x51, 0x03 });
            writer.Write((byte)(microsecondsPerQuarter >> 16));
            writer.Write((byte)(microsecondsPerQuarter >> 8));
            writer.Write((byte)microsecondsPerQuarter);

            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0xFF, 0x59, 0x02, 13, 0x00 });

            WriteVarInt(writer, ticksPerQuarter * 4);
            writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        });

        var notes = Track(writer =>
        {
            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0x90, 60, 100 });

            WriteVarInt(writer, ticksPerQuarter);
            writer.Write(new byte[] { 0x80, 60, 64 });

            WriteVarInt(writer, ticksPerQuarter * 3);
            writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        });

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var file = new BinaryWriter(stream);

        file.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteInt32BigEndian(file, 6);
        WriteInt16BigEndian(file, 1);                 // format 1
        WriteInt16BigEndian(file, 2);                 // two tracks
        WriteInt16BigEndian(file, ticksPerQuarter);
        file.Write(meta);
        file.Write(notes);

        return path;
    }

    /// <summary>Writes a short mono 16-bit WAV holding a sine tone.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="seconds">How long the tone runs.</param>
    /// <param name="frequency">The tone frequency, in Hz.</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string WriteMonoWav(string path, double seconds, double frequency)
    {
        var frames = (int)(SampleRate * seconds);
        var dataBytes = frames * 2;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);            // PCM
        writer.Write((short)1);            // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);      // bytes per second
        writer.Write((short)2);            // block align
        writer.Write((short)16);           // bits per sample

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        for (var i = 0; i < frames; i++)
        {
            writer.Write((short)(8000 * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate)));
        }

        return path;
    }

    /// <summary>
    /// Writes a 16-bit stereo WAV holding one constant sample value, and returns its path.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="seconds">How long it runs.</param>
    /// <param name="value">The sample value, -1 to 1, written to both channels.</param>
    /// <param name="sampleRate">The rate to write it at.</param>
    /// <returns><paramref name="path"/>.</returns>
    /// <remarks>
    /// A constant value rather than a tone: a mix of several of these has an answer that can be
    /// asserted exactly, so a test can say WHICH layer it is hearing rather than only that it hears
    /// something. Stems downloaded from a music service are 16-bit stereo at 48 kHz, which is what
    /// the default rate of the callers matches.
    /// </remarks>
    public static string WriteStereoWav(string path, double seconds, double value, int sampleRate)
    {
        var frames = (int)(sampleRate * seconds);
        var dataBytes = frames * 4;
        var sample = (short)Math.Round(Math.Clamp(value, -1.0, 1.0) * short.MaxValue);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);                 // PCM
        writer.Write((short)2);                 // stereo
        writer.Write(sampleRate);
        writer.Write(sampleRate * 4);           // bytes per second
        writer.Write((short)4);                 // block align
        writer.Write((short)16);                // bits per sample

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        for (var i = 0; i < frames; i++)
        {
            writer.Write(sample);
            writer.Write(sample);
        }

        return path;
    }

    /// <summary>
    /// Writes a Standard MIDI File shaped the way a machine-generated stems export is: a tempo
    /// event on EVERY beat, a key signature the specification does not allow, and no time
    /// signature at all. Returns its path.
    /// </summary>
    /// <param name="path">Where to write the <c>.mid</c> file.</param>
    /// <param name="firstBeatsPerMinute">The tempo of the first half.</param>
    /// <param name="secondBeatsPerMinute">The tempo the second half runs at.</param>
    /// <param name="beats">How many beats long the file is; the tempo changes halfway.</param>
    /// <returns><paramref name="path"/>.</returns>
    /// <remarks>
    /// The point of the shape is that quantising against the FIRST tempo alone would be wrong from
    /// the change onwards, so a grid built from this file is only right if it follows the whole map.
    /// </remarks>
    public static string WriteMidiWithAPerBeatTempoMap(string path, double firstBeatsPerMinute,
        double secondBeatsPerMinute, int beats = 8)
    {
        const int ticksPerQuarter = 480;

        var meta = Track(writer =>
        {
            // Out of range on purpose: a strict reader rejects the file and every path here reads it.
            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0xFF, 0x59, 0x02, 13, 0x00 });

            for (var beat = 0; beat < beats; beat++)
            {
                var beatsPerMinute = beat < beats / 2 ? firstBeatsPerMinute : secondBeatsPerMinute;
                var microsecondsPerQuarter = (int)Math.Round(60_000_000.0 / beatsPerMinute);

                WriteVarInt(writer, beat == 0 ? 0 : ticksPerQuarter);
                writer.Write(new byte[] { 0xFF, 0x51, 0x03 });
                writer.Write((byte)(microsecondsPerQuarter >> 16));
                writer.Write((byte)(microsecondsPerQuarter >> 8));
                writer.Write((byte)microsecondsPerQuarter);
            }

            WriteVarInt(writer, ticksPerQuarter);
            writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        });

        var notes = Track(writer =>
        {
            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0xC0, 48 });     // one program change, as an export writes

            for (var beat = 0; beat < beats; beat++)
            {
                WriteVarInt(writer, beat == 0 ? 0 : ticksPerQuarter / 2);
                writer.Write(new byte[] { 0x90, 60, 100 });

                // Every note is released: a note left sounding when its track ends is a complaint
                // the sequence reader makes, and this fixture is meant to read cleanly.
                WriteVarInt(writer, ticksPerQuarter / 2);
                writer.Write(new byte[] { 0x80, 60, 64 });
            }

            WriteVarInt(writer, 0);
            writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        });

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var file = new BinaryWriter(stream);

        file.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteInt32BigEndian(file, 6);
        WriteInt16BigEndian(file, 1);                 // format 1
        WriteInt16BigEndian(file, 2);                 // two tracks
        WriteInt16BigEndian(file, ticksPerQuarter);
        file.Write(meta);
        file.Write(notes);

        return path;
    }

    // ----- RIFF and SMF plumbing -----

    private static byte[] Chunk(string id, Action<BinaryWriter> body)
    {
        using var payload = new MemoryStream();
        using (var inner = new BinaryWriter(payload, Encoding.ASCII, true))
        {
            body(inner);
        }

        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write((int)payload.Length);
            writer.Write(payload.ToArray());
        }

        return result.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string id, Action<BinaryWriter> body) =>
        writer.Write(Chunk(id, body));

    private static byte[] Track(Action<BinaryWriter> body)
    {
        using var payload = new MemoryStream();
        using (var inner = new BinaryWriter(payload, Encoding.ASCII, true))
        {
            body(inner);
        }

        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteInt32BigEndian(writer, (int)payload.Length);
            writer.Write(payload.ToArray());
        }

        return result.ToArray();
    }

    private static void WriteInt16BigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteInt32BigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteVarInt(BinaryWriter writer, int value)
    {
        var buffer = value & 0x7F;

        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= 0x80;
            buffer += value & 0x7F;
        }

        while (true)
        {
            writer.Write((byte)buffer);

            if ((buffer & 0x80) == 0)
            {
                break;
            }

            buffer >>= 8;
        }
    }

    private static byte[] FixedString(string value, int length)
    {
        var bytes = new byte[length];
        var text = Encoding.ASCII.GetBytes(value);
        Array.Copy(text, bytes, Math.Min(text.Length, length - 1));
        return bytes;
    }

    private static void WritePresetRecord(BinaryWriter writer, string name, int bagIndex)
    {
        writer.Write(FixedString(name, 20));
        writer.Write((ushort)0);          // preset number
        writer.Write((ushort)0);          // bank number
        writer.Write((ushort)bagIndex);
        writer.Write(0);                  // library
        writer.Write(0);                  // genre
        writer.Write(0);                  // morphology
    }

    private static void WriteInstrumentRecord(BinaryWriter writer, string name, int bagIndex)
    {
        writer.Write(FixedString(name, 20));
        writer.Write((ushort)bagIndex);
    }

    private static void WriteBagRecord(BinaryWriter writer, int generatorIndex)
    {
        writer.Write((ushort)generatorIndex);
        writer.Write((ushort)0);          // modulator index
    }

    private static void WriteGenerator(BinaryWriter writer, int type, int value)
    {
        writer.Write((ushort)type);
        writer.Write((ushort)value);
    }

    private static void WriteSampleHeader(BinaryWriter writer, string name, int start, int end, int startLoop, int endLoop)
    {
        writer.Write(FixedString(name, 20));
        writer.Write(start);
        writer.Write(end);
        writer.Write(startLoop);
        writer.Write(endLoop);
        writer.Write(SampleRate);
        writer.Write((byte)60);           // original pitch: middle C
        writer.Write((sbyte)0);           // pitch correction
        writer.Write((ushort)0);          // sample link
        writer.Write((ushort)1);          // mono sample
    }
}
