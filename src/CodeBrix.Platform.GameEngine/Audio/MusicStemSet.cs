using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Layered adaptive music: several stems of the same piece — an explore layer, a combat layer, a
/// boss layer — playing together in lock, with the game fading individual layers in and out as the
/// situation changes.
/// </summary>
/// <remarks>
/// <para>
/// It is a <see cref="MusicTrack"/>, so <see cref="MusicManager"/> plays, crossfades, ducks and
/// stops it exactly like any other music. What it adds is the indexer:
/// </para>
/// <code>
/// var stems = new MusicStemSet("battle", "explore.ogg", "combat.ogg", "boss.ogg");
/// MusicManager.Instance.Play(stems);
///
/// stems["combat"].FadeTo(1.0f, TimeSpan.FromSeconds(2));   // layer in
/// stems["explore"].FadeTo(0.0f, TimeSpan.FromSeconds(2));  // layer out
/// </code>
/// <para>
/// EVERY STEM MUST SHARE A SAMPLE RATE AND CHANNEL COUNT. A mismatch is reported as an exception
/// naming the stem and both formats, not mixed anyway — mixing them anyway would play some layers
/// at the wrong speed, which sounds like a bug in the game rather than in its assets. Calling
/// <see cref="AudioSystem.Initialize"/> makes this a non-issue: stems then rate-convert to the
/// pinned device rate as they decode, so files of assorted rates line up by construction.
/// </para>
/// <para>
/// Stems SHOULD also share a length. They do not have to: the set loops as one at the length of its
/// longest stem, and a shorter stem is silent until that point rather than wrapping early on its
/// own. A mismatch is logged once, because it is far more often a mistake than a plan.
/// </para>
/// <para>
/// MEMORY. Stems are decoded to raw PCM up front — roughly 10 MB per stereo minute at 44.1 kHz,
/// times the number of layers. That buys exact lock and an audio thread that never decodes. Layered
/// music is normally a short loop, which is what this suits; for a long linear piece use
/// <see cref="FileMusicTrack"/>, which streams.
/// </para>
/// <para>
/// FROM A STEMS DOWNLOAD. <see cref="FromSunoStems(string, string, string[])"/> builds a set
/// straight from a stems export — the zip or folder a generative music service hands over —
/// choosing the layers by name, and filling <see cref="MusicTrack.Timeline"/> in from the MIDI
/// that ships beside the recordings so bar-locked layer changes work with no further wiring.
/// </para>
/// <para>
/// FOR MIDI MUSIC, DO NOT USE THIS. A <see cref="MidiMusicTrack"/> layers far more cheaply through
/// per-channel volume on the one sequence it is already playing — no second copy of anything, no
/// shared-format requirement, and the layers cannot drift because there is only one sequence. See
/// <see cref="MidiMusicTrack.SetLayerVolume"/>.
/// </para>
/// </remarks>
public sealed partial class MusicStemSet : MusicTrack
{
    private readonly StemMixSampleProvider _provider;
    private readonly MusicStem[] _stems;
    private readonly Dictionary<string, MusicStem> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MusicFade> _stemFades = new();
    private readonly object _gate = new();
    private readonly int _sampleRate;

    private StreamingAudioSource? _source;
    private bool _disposed;

    /// <summary>
    /// Creates a stem set from audio files, naming each stem after its file. Every format the engine
    /// can load works here (see <see cref="PlatformAudioFactory"/>).
    /// </summary>
    /// <param name="key">A name for the set as a whole.</param>
    /// <param name="filePaths">Paths to the stem files; at least one.</param>
    /// <exception cref="ArgumentException">No files were given, or the stems' formats differ.</exception>
    public MusicStemSet(string key, params string[] filePaths)
        : this(key, NamesFromPaths(filePaths), DecodeAll(filePaths))
    { }

    /// <summary>
    /// Creates a stem set from audio files with explicit names, for when the file names are not what
    /// the game wants to say in <c>stems["..."]</c>.
    /// </summary>
    /// <param name="key">A name for the set as a whole.</param>
    /// <param name="namedFilePaths">Stem name to file path.</param>
    /// <exception cref="ArgumentException">No files were given, or the stems' formats differ.</exception>
    public MusicStemSet(string key, IReadOnlyDictionary<string, string> namedFilePaths)
        : this(key, NamesOf(namedFilePaths), DecodeAll(PathsOf(namedFilePaths)))
    { }

    /// <summary>
    /// Creates a stem set from stems that are already decoded — for sharing one decode across
    /// several sets, or for building a set that never touches the disk.
    /// </summary>
    /// <param name="key">A name for the set as a whole.</param>
    /// <param name="stemNames">The stems' names, in the same order as <paramref name="decodedStems"/>.</param>
    /// <param name="decodedStems">The decoded stems; at least one, all of the same format.</param>
    /// <exception cref="ArgumentException">The lists are empty or of different lengths, or the stems' formats differ.</exception>
    public MusicStemSet(string key, IReadOnlyList<string> stemNames, IReadOnlyList<CachedSound> decodedStems)
        : base(key)
    {
        ArgumentNullException.ThrowIfNull(stemNames);
        ArgumentNullException.ThrowIfNull(decodedStems);

        if (decodedStems.Count == 0)
        {
            throw new ArgumentException("A stem set needs at least one stem.", nameof(decodedStems));
        }

        if (stemNames.Count != decodedStems.Count)
        {
            throw new ArgumentException(
                $"Got {stemNames.Count} stem names for {decodedStems.Count} stems; they must correspond.",
                nameof(stemNames));
        }

        var sounds = new CachedSound[decodedStems.Count];
        for (var i = 0; i < decodedStems.Count; i++)
        {
            sounds[i] = decodedStems[i] ?? throw new ArgumentException($"Stem '{stemNames[i]}' is null.", nameof(decodedStems));
        }

        ValidateFormats(stemNames, sounds);
        WarnOnLengthMismatch(key, stemNames, sounds);

        _sampleRate = sounds[0].SampleRate;
        _provider = new StemMixSampleProvider(sounds);
        _provider.EndReached = OnEndReached;

        _stems = new MusicStem[sounds.Length];
        for (var i = 0; i < sounds.Length; i++)
        {
            var name = stemNames[i] ?? $"stem{i}";
            _stems[i] = new MusicStem(this, i, name);
            _byName[name] = _stems[i];
        }

        // Every layer starts silent: a game brings stems in deliberately, and a set that came up
        // with everything at full would be the loudest possible first impression of the feature.
        // The first stem is the exception, so Play(stems) alone is audible.
        _provider.SetGain(0, 1f);
    }

    /// <summary>The stems, in the order they were given.</summary>
    public IReadOnlyList<MusicStem> Stems => _stems;

    /// <summary>The number of stems in the set.</summary>
    public int Count => _stems.Length;

    /// <summary>Gets a stem by name; the comparison ignores case.</summary>
    /// <param name="name">The stem's name.</param>
    /// <returns>The stem.</returns>
    /// <exception cref="KeyNotFoundException">No stem has that name.</exception>
    public MusicStem this[string name]
    {
        get
        {
            if (name is not null && _byName.TryGetValue(name, out var stem))
            {
                return stem;
            }

            throw new KeyNotFoundException(
                $"The stem set '{Key}' has no stem named '{name}'. It has: {string.Join(", ", _byName.Keys)}.");
        }
    }

    /// <summary>Gets a stem by index.</summary>
    /// <param name="index">The stem's position in <see cref="Stems"/>.</param>
    /// <returns>The stem.</returns>
    public MusicStem this[int index] => _stems[index];

    /// <inheritdoc/>
    public override TimeSpan Position
        => _disposed ? TimeSpan.Zero : TimeSpan.FromSeconds(_provider.FramePosition / (double)_sampleRate);

    /// <inheritdoc/>
    public override TimeSpan Duration
        => _disposed ? TimeSpan.Zero : TimeSpan.FromSeconds(_provider.LoopFrames / (double)_sampleRate);

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => !_disposed && _provider.IsLooping;
        set
        {
            if (!_disposed)
            {
                _provider.IsLooping = value;
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _source is not null && _source.IsPlaying;
            }
        }
    }

    /// <inheritdoc/>
    public override void Seek(TimeSpan position)
    {
        if (!_disposed)
        {
            _provider.SeekFrames((long)(position.TotalSeconds * _sampleRate));
        }
    }

    /// <inheritdoc/>
    protected override void ApplyVolume(float volume)
    {
        lock (_gate)
        {
            // StreamingAudioSource is an IMixerVoice and applies the music bus itself, so this sets
            // only the set's own level.
            if (_source is not null)
            {
                _source.Volume = volume;
            }
        }
    }

    /// <inheritdoc/>
    internal override void StartCore(bool fromStart)
    {
        if (_disposed)
        {
            return;
        }

        if (fromStart)
        {
            _provider.SeekFrames(0);
        }

        EnsureSource().Start();
    }

    /// <inheritdoc/>
    internal override void PauseCore()
    {
        lock (_gate)
        {
            _source?.Stop();
        }
    }

    /// <inheritdoc/>
    internal override void ResumeCore()
    {
        if (!_disposed)
        {
            EnsureSource().Start();
        }
    }

    /// <inheritdoc/>
    internal override void StopCore()
    {
        lock (_gate)
        {
            _source?.Stop();
        }

        _provider.SeekFrames(0);
    }

    internal float GetStemGain(int index) => _provider.GetGain(index);

    internal void SetStemGain(int index, float gain)
    {
        CancelStemFade(index);
        _provider.SetGain(index, gain);
    }

    internal void FadeStem(int index, float target, TimeSpan duration)
    {
        CancelStemFade(index);

        var clamped = Math.Clamp(target, 0f, 1f);
        var from = _provider.GetGain(index);

        if (duration <= TimeSpan.Zero)
        {
            _provider.SetGain(index, clamped);
            return;
        }

        var fade = MusicManager.Instance.Ticker.Add(from, clamped, duration, value => _provider.SetGain(index, value));

        lock (_gate)
        {
            _stemFades[index] = fade;
        }
    }

    private void CancelStemFade(int index)
    {
        MusicFade? existing = null;

        lock (_gate)
        {
            if (_stemFades.Remove(index, out var fade))
            {
                existing = fade;
            }
        }

        if (existing is not null)
        {
            MusicManager.Instance.Ticker.Cancel(existing);
        }
    }

    private StreamingAudioSource EnsureSource()
    {
        lock (_gate)
        {
            if (_source is null)
            {
                // Created on first play, not at construction: a set built during level load should
                // not hold an output voice open until the game actually starts it.
                _source = new StreamingAudioSource(_provider)
                {
                    Bus = AudioBus.Music,
                    SuspendOnEnginePause = true,
                    Volume = Volume,
                };
            }

            return _source;
        }
    }

    // Raised from the audio thread. It deliberately does NOT stop the voice: touching the device
    // from inside its own callback is how deadlocks happen. The provider keeps producing silence,
    // and whoever is driving the music (a playlist, or the game) stops it from its own thread.
    private void OnEndReached() => RaiseEnded();

    /// <inheritdoc/>
    public override void Dispose()
    {
        StreamingAudioSource? source;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            source = _source;
            _source = null;

            foreach (var fade in _stemFades.Values)
            {
                MusicManager.Instance.Ticker.Cancel(fade);
            }

            _stemFades.Clear();
        }

        _provider.EndReached = null;
        source?.Dispose();
    }

    private static void ValidateFormats(IReadOnlyList<string> names, CachedSound[] stems)
    {
        var rate = stems[0].SampleRate;
        var channels = stems[0].Channels;

        for (var i = 1; i < stems.Length; i++)
        {
            if (stems[i].SampleRate == rate && stems[i].Channels == channels)
            {
                continue;
            }

            throw new ArgumentException(
                $"Stem '{names[i]}' is {stems[i].SampleRate} Hz / {stems[i].Channels} ch but "
                + $"'{names[0]}' is {rate} Hz / {channels} ch. Every stem in a set must share a "
                + "format, because they are summed sample for sample. Convert the files to match, "
                + "or call AudioSystem.Initialize(...) to pin the device format so stems "
                + "rate-convert to it as they decode.");
        }
    }

    private static void WarnOnLengthMismatch(string key, IReadOnlyList<string> names, CachedSound[] stems)
    {
        var shortest = 0;
        var longest = 0;

        for (var i = 1; i < stems.Length; i++)
        {
            if (stems[i].AudioData.Length < stems[shortest].AudioData.Length) { shortest = i; }
            if (stems[i].AudioData.Length > stems[longest].AudioData.Length) { longest = i; }
        }

        if (stems[shortest].AudioData.Length == stems[longest].AudioData.Length)
        {
            return;
        }

        Engine.Logger.LogWarning(
            "Stem set '{Key}' has stems of different lengths: '{Shortest}' is {ShortestSeconds:0.###}s and "
            + "'{Longest}' is {LongestSeconds:0.###}s. The set loops at the longest, and shorter stems are "
            + "silent until then. Stems are normally the same length.",
            key, names[shortest], stems[shortest].Duration.TotalSeconds, names[longest], stems[longest].Duration.TotalSeconds);
    }

    private static string[] NamesFromPaths(string[] filePaths)
    {
        RequirePaths(filePaths);

        var names = new string[filePaths.Length];
        for (var i = 0; i < filePaths.Length; i++)
        {
            names[i] = Path.GetFileNameWithoutExtension(filePaths[i]) ?? $"stem{i}";
        }

        return names;
    }

    private static CachedSound[] DecodeAll(string[] filePaths)
    {
        RequirePaths(filePaths);

        var stems = new CachedSound[filePaths.Length];
        for (var i = 0; i < filePaths.Length; i++)
        {
            stems[i] = CachedSound.FromFile(filePaths[i]);
        }

        return stems;
    }

    private static void RequirePaths(string[] filePaths)
    {
        if (filePaths is null || filePaths.Length == 0)
        {
            throw new ArgumentException("A stem set needs at least one stem file.", nameof(filePaths));
        }
    }

    private static string[] NamesOf(IReadOnlyDictionary<string, string> namedFilePaths)
    {
        ArgumentNullException.ThrowIfNull(namedFilePaths);

        var names = new string[namedFilePaths.Count];
        var i = 0;
        foreach (var name in namedFilePaths.Keys)
        {
            names[i++] = name;
        }

        return names;
    }

    private static string[] PathsOf(IReadOnlyDictionary<string, string> namedFilePaths)
    {
        ArgumentNullException.ThrowIfNull(namedFilePaths);

        var paths = new string[namedFilePaths.Count];
        var i = 0;
        foreach (var name in NamesOf(namedFilePaths))
        {
            paths[i++] = namedFilePaths[name];
        }

        return paths;
    }
}
