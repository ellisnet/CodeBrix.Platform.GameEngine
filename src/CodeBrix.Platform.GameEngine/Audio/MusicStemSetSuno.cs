using System;
using System.Collections.Generic;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

// The stem-set factory for a stems export downloaded from a generative music service: the one
// route into MusicStemSet that does not need the game to know the file names, the tempo or the
// format, because the export already says all three. Kept in its own file so that the general
// stem-set machinery stays readable without a service-shaped special case running through it.
public sealed partial class MusicStemSet
{
    private static readonly string[] _noProblems = Array.Empty<string>();

    /// <summary>
    /// Everything that could not be honoured about the stems export this set was built from, one
    /// human-readable line each. Empty for a clean export, and empty for a set built any other way.
    /// </summary>
    /// <remarks>
    /// The list is the export's own: missing files, MIDI that would not read, stems whose length
    /// disagrees with the song's, names outside the vocabulary the reader knows. None of it stops
    /// the set loading — it is reported here, and logged once as a warning, rather than thrown.
    /// </remarks>
    public IReadOnlyList<string> Problems { get; private set; } = _noProblems;

    /// <summary>
    /// Builds an adaptive stem set straight from a stems export — the zip a generative music
    /// service hands over, or a folder holding the same files — so a game can cross-fade the layers
    /// of a downloaded song with nothing else set up.
    /// </summary>
    /// <param name="key">A name for the set as a whole.</param>
    /// <param name="stemsZipOrFolder">The stems zip, or a folder holding the stem files.</param>
    /// <param name="stemNames">
    /// The stems to load, by the names on the files — "Vocals", "Drums", "Bass" and so on; the
    /// comparison ignores case. Pass none to load every stem that carries audio.
    /// </param>
    /// <returns>The stem set, with its <see cref="MusicTrack.Timeline"/> already filled in.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="stemsZipOrFolder"/> is empty, a requested name is not one of the export's
    /// audio stems (the message lists the ones that are), or the export carries no audio at all.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">There is no zip or folder at that path.</exception>
    /// <remarks>
    /// <para>
    /// PICK THE STEMS THE GAME WILL ACTUALLY CROSS-FADE. Every stem is decoded to memory, and a
    /// stereo stem at 48 kHz is about 23 MB per minute — so a four-minute song is about 92 MB PER
    /// STEM, and loading all ten of a full export is most of a gigabyte. Naming three or four is
    /// the difference between a feature and a memory problem. Call
    /// <see cref="AudioSystem.Initialize"/> first as well: with the device rate pinned, the decode
    /// converts once instead of the set being rejected for disagreeing with whatever is already
    /// playing.
    /// </para>
    /// <para>
    /// WHERE THE FILES COME FROM. A folder is read in place. A zip is left compressed and its
    /// entries are extracted ON DEMAND — the first time a stem is asked for its file — into a cache
    /// folder keyed by the zip, so the same download is unpacked once and reused on later runs. The
    /// cache lives under the loader's own root unless
    /// <see cref="SunoLoadOptions.CacheFolder"/> names somewhere else; a game that ships a download
    /// should point it at its own writable folder rather than leaving files in the user's temp area.
    /// </para>
    /// <para>
    /// WHAT THE GRID COMES FROM. The export's MIDI carries the tempo, so
    /// <see cref="MusicTrack.Timeline"/> is filled in from the first stem that has a MIDI file, and
    /// the grid then follows the tempo exactly however often it moves — which matters here, because
    /// a generated arrangement routinely writes ONE TEMPO EVENT PER BEAT. Four beats to the bar is
    /// assumed, because these exports carry no time signature. An export with no MIDI at all falls
    /// back to the tempo map the song itself reports, and a set built from one with neither gets no
    /// timeline (quantised transitions away from it then happen immediately and say so in the log).
    /// </para>
    /// <para>
    /// ALIGNMENT IS TURNED OFF. Measuring a stem's recording against its MIDI is a MIDI concern —
    /// it exists so a part can be played from either source — and it costs a decode of every stem.
    /// Nothing here plays the MIDI, so the option is forced off even when the caller's options set
    /// it. The recordings are sample-locked with each other by construction, which is what a stem
    /// set needs.
    /// </para>
    /// <para>
    /// THE FULL MIX IS NOT ONE OF THE STEMS. When the download includes it, it is an ordinary
    /// long linear piece: load it with <see cref="AudioResourceManager"/> and play it as a
    /// <see cref="FileMusicTrack"/>, which streams rather than decoding the whole song to memory.
    /// </para>
    /// <code>
    /// AudioSystem.Initialize(48000, 2);
    /// var stems = MusicStemSet.FromSunoStems("battle", @"C:\Music\Nightfall Stems.zip",
    ///                                        "Drums", "Bass", "Guitar");
    /// MusicManager.Instance.Play(stems);
    /// stems["Guitar"].FadeTo(1.0f, TimeSpan.FromSeconds(2));
    /// </code>
    /// </remarks>
    public static MusicStemSet FromSunoStems(string key, string stemsZipOrFolder, params string[] stemNames)
        => FromSunoStems(key, stemsZipOrFolder, null, stemNames);

    /// <summary>
    /// Builds an adaptive stem set from a stems export, with control over how the export is read —
    /// where a zip is unpacked to, and which files the reader looks for.
    /// </summary>
    /// <param name="key">A name for the set as a whole.</param>
    /// <param name="stemsZipOrFolder">The stems zip, or a folder holding the stem files.</param>
    /// <param name="options">
    /// How to read the export, or <see langword="null"/> for the defaults. The options are COPIED,
    /// and <see cref="SunoLoadOptions.MeasureAlignment"/> is forced off in the copy; nothing else
    /// is changed, so the caller's own object is left exactly as it was.
    /// </param>
    /// <param name="stemNames">
    /// The stems to load, by the names on the files; the comparison ignores case. Pass none to load
    /// every stem that carries audio.
    /// </param>
    /// <returns>The stem set, with its <see cref="MusicTrack.Timeline"/> already filled in.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="stemsZipOrFolder"/> is empty, a requested name is not one of the export's
    /// audio stems (the message lists the ones that are), or the export carries no audio at all.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">There is no zip or folder at that path.</exception>
    /// <remarks>
    /// See the other overload for the memory cost, the cache folder, where the grid comes from and
    /// why alignment is off.
    /// </remarks>
    public static MusicStemSet FromSunoStems(string key, string stemsZipOrFolder, SunoLoadOptions? options,
        params string[] stemNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stemsZipOrFolder);

        var settings = (options ?? new SunoLoadOptions()).Clone();

        // Alignment lines a stem's recording up with its MIDI, which is only useful to something
        // that plays the MIDI, and it decodes every stem to do it. A stem set plays recordings.
        settings.MeasureAlignment = false;

        var song = SunoStemsLoader.Load(stemsZipOrFolder, settings);
        var chosen = ChooseStems(song, stemNames);

        var names = new string[chosen.Count];
        var sounds = new CachedSound[chosen.Count];

        for (var i = 0; i < chosen.Count; i++)
        {
            var stem = chosen[i];
            names[i] = stem.Name;

            // GetWavPath extracts the entry from the zip on the way past, when that is where it
            // lives. The MP3 is the fallback for a download whose WAVs were not taken.
            sounds[i] = CachedSound.FromFile(stem.HasWav ? stem.GetWavPath() : stem.GetMp3Path());
        }

        var set = new MusicStemSet(key, names, sounds)
        {
            Timeline = TimelineFor(song),
            Problems = song.Problems ?? _noProblems,
        };

        if (set.Problems.Count > 0)
        {
            // One line per set rather than one per problem: a download with an unrecognised stem
            // name reports it once, and a game's log should not scroll because of it.
            Engine.Logger.LogWarning(
                "Stem set '{Key}' was built from '{Source}', which reported {ProblemCount} problem(s); "
                + "the first is: {FirstProblem}. The set still plays. See MusicStemSet.Problems for all of them.",
                key, song.SourcePath, set.Problems.Count, set.Problems[0]);
        }

        return set;
    }

    // The stems the caller asked for, in the order they asked for them; every audio stem, in the
    // export's own order, when they asked for none.
    private static List<SunoStem> ChooseStems(SunoSong song, string[]? stemNames)
    {
        var chosen = new List<SunoStem>(stemNames is null ? song.AudioStems.Count : stemNames.Length);

        if (stemNames is null || stemNames.Length == 0)
        {
            foreach (var stem in song.AudioStems)
            {
                chosen.Add(stem);
            }
        }
        else
        {
            foreach (var name in stemNames)
            {
                var stem = song.FindStem(name);

                if (stem is null || !stem.HasAudio)
                {
                    // Naming what IS there turns "it did not work" into "you meant Drums", which is
                    // the whole difference when the names come off someone else's file names.
                    throw new ArgumentException(
                        $"'{song.SourcePath}' has no audio stem named '{name}'. Its audio stems are: "
                        + $"{DescribeStems(song)}.",
                        nameof(stemNames));
                }

                chosen.Add(stem);
            }
        }

        if (chosen.Count == 0)
        {
            throw new ArgumentException(
                $"'{song.SourcePath}' holds no audio stems, so there is nothing to layer. Files are "
                + "recognised by the '<Title> (<Stem>).wav' shape the export writes.",
                nameof(stemNames));
        }

        return chosen;
    }

    private static string DescribeStems(SunoSong song)
    {
        if (song.AudioStems.Count == 0)
        {
            return "(none - no file in it carries audio)";
        }

        var names = new string[song.AudioStems.Count];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = song.AudioStems[i].Name;
        }

        return string.Join(", ", names);
    }

    // The exact grid, from the MIDI the export ships beside the recordings.
    private static MusicTimeline? TimelineFor(SunoSong song)
    {
        foreach (var stem in song.MidiStems)
        {
            var map = stem.Midi?.TempoMap;

            if (map is not null)
            {
                // Four beats to the bar: these exports carry no time signature. A game that knows
                // the song is in something else replaces Timeline afterwards.
                return new MusicTimeline(map, 4);
            }
        }

        var converted = ConvertTempoMap(song.TempoMap);

        return converted is null ? null : new MusicTimeline(converted, 4);
    }

    // The song's own tempo map says WHEN each tempo starts but not WHICH BEAT that is, and the grid
    // is quantised in beat space — so the beats are integrated forward from the times and tempos.
    private static MidiTempoMap? ConvertTempoMap(IReadOnlyList<SunoTempoChange> tempoMap)
    {
        if (tempoMap is null || tempoMap.Count == 0 || !(tempoMap[0].BeatsPerMinute > 0))
        {
            return null;
        }

        var changes = new List<MidiTempoChange>(tempoMap.Count + 1)
        {
            // An export whose first entry is not at time zero runs its lead-in at that first tempo.
            // Saying so explicitly keeps MidiTempoMap from prepending the MIDI default of 120 and
            // changing the tempo the timeline would report.
            new MidiTempoChange(TimeSpan.Zero, 0, tempoMap[0].BeatsPerMinute),
        };

        var beat = 0.0;
        var lastTime = TimeSpan.Zero;
        var beatsPerMinute = tempoMap[0].BeatsPerMinute;

        foreach (var change in tempoMap)
        {
            if (!(change.BeatsPerMinute > 0))
            {
                continue;
            }

            var time = change.Time < TimeSpan.Zero ? TimeSpan.Zero : change.Time;

            if (time > lastTime)
            {
                beat += (time - lastTime).TotalSeconds * beatsPerMinute / 60.0;
                lastTime = time;
            }

            beatsPerMinute = change.BeatsPerMinute;

            // An entry that restates the tempo already running moves no boundary, and letting it
            // through would report a song whose tempo never varies as one that does.
            if (changes[^1].BeatsPerMinute.Equals(beatsPerMinute))
            {
                continue;
            }

            changes.Add(new MidiTempoChange(time, beat, beatsPerMinute));
        }

        return new MidiTempoMap(changes);
    }
}
