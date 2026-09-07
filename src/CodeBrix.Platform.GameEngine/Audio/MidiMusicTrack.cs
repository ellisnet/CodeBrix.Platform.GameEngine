using System;
using System.Collections.Generic;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Sfz;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Music that is a MIDI sequence rendered live through a sampled instrument — a SoundFont
/// (<c>.sf2</c>), an SFZ instrument (<c>.sfz</c>) or a Decent Sampler instrument
/// (<c>.dspreset</c>, <c>.dslibrary</c>, <c>.dsbundle</c>, or a folder holding a preset) — the
/// format that costs kilobytes on disk instead of megabytes, and whose arrangement the game can
/// change while it plays.
/// </summary>
/// <remarks>
/// <para>
/// SHARE THE INSTRUMENT. A sampled instrument is the expensive thing in the whole system: a
/// SoundFont runs to tens of megabytes, and an SFZ or Decent Sampler library decodes its samples
/// into memory. Load through <see cref="SoundFontCache"/>,
/// <see cref="SfzInstrumentCache"/> or
/// <see cref="DecentSamplerInstrumentCache"/> and hand the same instance to every track that wants
/// that sound. The MIDI sequence itself is small. The path constructor is the exception and says so.
/// </para>
/// <para>
/// ONE INSTRUMENT PER INDEPENDENT PERFORMANCE (Decent Sampler). A preset's knob positions and
/// modulated parameters are INSTRUMENT state, not synthesizer state, because that is where the
/// format puts them — a binding writes the group's volume. Two tracks over one instrument therefore
/// share every knob, which is exactly right for two players of the same sound and wrong when each
/// part must move its own. Give those their own instrument.
/// </para>
/// <para>
/// AN INSTRUMENT FROM AN ASSET PACK MUST REACH THE DISK, EXCEPT SF2. A <c>.sf2</c> is a single file
/// and loads from a <see cref="System.IO.Stream"/>. A <c>.sfz</c> is a text file that REFERENCES
/// sample files beside it, so it needs a real directory. A <c>.dspreset</c> is the same: its
/// <c>Samples/</c> folder sits beside it. A <c>.dslibrary</c> or <c>.dsbundle</c> IS one file, but
/// it is read IN PLACE BY PATH — nothing is unpacked — so it too must exist on disk. Anything
/// packed into an <see cref="Assets.AssetsFile"/> therefore has to be extracted before it can be
/// loaded; only the SoundFont can be handed over as a stream.
/// </para>
/// <para>
/// MPE GOES THROUGH THE PLAYER. All three engines implement it, and the settings live on
/// <see cref="Player"/>, so <c>track.Player.MpeMode = MpeMode.Auto</c> (with
/// <c>MpeMemberBendRange</c> beside it) is the whole of it. Those settings persist across loads.
/// </para>
/// <para>
/// Like every music track it plays on <see cref="AudioBus.Music"/> and suspends with the global
/// engine pause.
/// </para>
/// </remarks>
public sealed class MidiMusicTrack : MusicTrack, IEnginePausableAudio, IMixerVoice
{
    private const int MidiChannelCount = 16;

    private readonly MidiMusicPlayer _player;
    private readonly float[] _layerVolume = new float[MidiChannelCount];
    private readonly Dictionary<int, MusicFade> _layerFades = new();
    private readonly object _layerGate = new();

    private bool _disposed;
    private bool _pausedByEngine;
    private IReadOnlyList<string> _problems = Array.Empty<string>();

    /// <summary>Creates a track over a MIDI sequence rendered by a SoundFont.</summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="soundFont">The SoundFont to render with — share one instance across tracks.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <remarks>
    /// <see cref="MusicTrack.Timeline"/> is filled in from the sequence's own tempo map, so
    /// quantised transitions land on the beat even in a piece whose tempo moves. FOUR BEATS TO THE
    /// BAR IS ASSUMED, because a <see cref="MidiSequence"/> keeps no time signature; set
    /// <see cref="MusicTrack.Timeline"/> to a <see cref="MusicTimeline"/> of your own for another
    /// meter, or read the FILE (through the path constructor or
    /// <see cref="MusicTimeline.FromMidiFile"/>) to pick up its time signature and markers as well.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MidiMusicTrack(string key, SoundFont soundFont, MidiSequence sequence)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(soundFont);
        ArgumentNullException.ThrowIfNull(sequence);
        _player.Load(soundFont, sequence);

        // A SoundFont reports no problems of its own; the sequence still can.
        AdoptSequence(null, sequence);
    }

    /// <summary>Creates a track over a MIDI sequence rendered by an SFZ instrument.</summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="instrument">The SFZ instrument to render with — share one instance across tracks.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <remarks>
    /// <see cref="MusicTrack.Timeline"/> is filled in from the sequence's own tempo map, so
    /// quantised transitions land on the beat even in a piece whose tempo moves. FOUR BEATS TO THE
    /// BAR IS ASSUMED, because a <see cref="MidiSequence"/> keeps no time signature; set
    /// <see cref="MusicTrack.Timeline"/> to a <see cref="MusicTimeline"/> of your own for another
    /// meter, or read the FILE (through the path constructor or
    /// <see cref="MusicTimeline.FromMidiFile"/>) to pick up its time signature and markers as well.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MidiMusicTrack(string key, SfzInstrument instrument, MidiSequence sequence)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(sequence);
        _player.Load(instrument, sequence);

        AdoptSequence(instrument.Problems, sequence);
    }

    /// <summary>Creates a track over a MIDI sequence rendered by a Decent Sampler instrument.</summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="instrument">
    /// The Decent Sampler instrument to render with. Share one instance across tracks that play the
    /// same sound — through a <see cref="DecentSamplerInstrumentCache"/>, or the process-wide
    /// <see cref="MidiMusicPlayer.SharedDecentSamplerCache"/> the path constructor uses — and give a
    /// track its own instrument when it must move its own knobs.
    /// </param>
    /// <param name="sequence">The sequence to play.</param>
    /// <remarks>
    /// <para>
    /// The track never disposes the instrument: a cache owns it, or the caller does.
    /// </para>
    /// <para>
    /// <see cref="MusicTrack.Timeline"/> is filled in from the sequence's own tempo map, so
    /// quantised transitions land on the beat even in a piece whose tempo moves. FOUR BEATS TO THE
    /// BAR IS ASSUMED, because a <see cref="MidiSequence"/> keeps no time signature; set
    /// <see cref="MusicTrack.Timeline"/> to a <see cref="MusicTimeline"/> of your own for another
    /// meter, or read the FILE (through the path constructor or
    /// <see cref="MusicTimeline.FromMidiFile"/>) to pick up its time signature and markers as well.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MidiMusicTrack(string key, DecentSamplerInstrument instrument, MidiSequence sequence)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(sequence);
        _player.Load(instrument, sequence);

        AdoptSequence(instrument.Problems, sequence);
    }

    /// <summary>
    /// Creates a track from an instrument file and a MIDI file on disk. The instrument's extension
    /// decides the synthesizer: <c>.sfz</c> loads an SFZ instrument, <c>.dspreset</c>,
    /// <c>.dslibrary</c> and <c>.dsbundle</c> (or a FOLDER holding a <c>.dspreset</c>) load a Decent
    /// Sampler instrument, and anything else is read as a SoundFont.
    /// </summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="instrumentPath">
    /// Path to a <c>.sf2</c>, <c>.sfz</c>, <c>.dspreset</c>, <c>.dslibrary</c> or <c>.dsbundle</c>
    /// file, or to a folder holding a Decent Sampler preset.
    /// </param>
    /// <param name="midiFilePath">Path to a Standard MIDI File.</param>
    /// <remarks>
    /// <para>
    /// A DECENT SAMPLER INSTRUMENT NAMED HERE IS SHARED; THE OTHER TWO ARE NOT. The engine resolves
    /// a Decent Sampler path through the process-wide
    /// <see cref="MidiMusicPlayer.SharedDecentSamplerCache"/>, so two tracks naming the same library
    /// decode it once — and share its knob state, which is the caveat in the type's own remarks. A
    /// SoundFont or SFZ instrument named here is loaded FRESH every time; for more than one track
    /// over one of those, load it through a cache and use the overloads above.
    /// </para>
    /// <para>
    /// It is also the overload that reads the MIDI FILE for its time signature and its markers, so
    /// <see cref="MusicTrack.Timeline"/> comes back with the real meter (6/8 is three quarter-note
    /// beats to the bar) and with <see cref="MusicManager.JumpToMarker"/> working. The tempo map
    /// comes with it either way: the overloads above take a <see cref="MidiSequence"/>, which keeps
    /// its tempo map but not its meta events' timing, and assume four beats to the bar.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    public MidiMusicTrack(string key, string instrumentPath, string midiFilePath)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(instrumentPath);
        ArgumentNullException.ThrowIfNull(midiFilePath);
        _player.Load(instrumentPath, midiFilePath);

        Timeline = MusicTimeline.FromMidiFile(midiFilePath);

        // The player holds the instrument it built from the path and does not hand it back, so the
        // instrument's own problems are not reachable from here; the sequence's are.
        RecordProblems(null, _player.Sequence?.Problems);
    }

    // Everything the three instrument-and-sequence constructors share: the grid comes from the
    // sequence's tempo map (four beats to the bar, because a sequence keeps no time signature), and
    // the two sources of problems are gathered and logged once.
    private void AdoptSequence(IReadOnlyList<string>? instrumentProblems, MidiSequence sequence)
    {
        Timeline = MusicTimeline.FromMidiSequence(sequence);
        RecordProblems(instrumentProblems, sequence.Problems);
    }

    // One warning per track, not one per line: a machine-generated file can report several, and a
    // game that ships a hundred of them should still have a readable log.
    private void RecordProblems(IReadOnlyList<string>? instrumentProblems, IReadOnlyList<string>? sequenceProblems)
    {
        var instrumentCount = instrumentProblems?.Count ?? 0;
        var sequenceCount = sequenceProblems?.Count ?? 0;

        if (instrumentCount + sequenceCount == 0)
        {
            return;
        }

        var combined = new List<string>(instrumentCount + sequenceCount);

        if (instrumentProblems is not null)
        {
            combined.AddRange(instrumentProblems);
        }

        if (sequenceProblems is not null)
        {
            combined.AddRange(sequenceProblems);
        }

        _problems = combined;

        Engine.Logger.LogWarning(
            "Music track '{Key}' loaded with {Count} problem(s) reported by its instrument or MIDI file: {Problems}",
            Key,
            combined.Count,
            string.Join(" | ", combined));
    }

    private MidiMusicTrack(string key)
        : base(key)
    {
        _player = new MidiMusicPlayer();
        _player.PlaybackEnded += OnPlaybackEnded;

        // A channel nobody has touched is at full: GetLayerVolume must not claim a layer is silent
        // when the arrangement is playing it.
        Array.Fill(_layerVolume, 1f);

        // MidiMusicPlayer knows nothing about the engine, so the pause and bus wiring every other
        // engine voice gets for free has to be done here.
        AudioPauseRegistry.Register(this);
        AudioMixer.Register(this);

        ApplyVolume(Volume);
    }

    /// <inheritdoc/>
    public override TimeSpan Position => _disposed ? TimeSpan.Zero : _player.Position;

    /// <inheritdoc/>
    public override TimeSpan Duration => _disposed ? TimeSpan.Zero : _player.Duration;

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => !_disposed && _player.IsLooping;
        set
        {
            if (!_disposed)
            {
                _player.IsLooping = value;
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsPlaying
        => !_disposed && _player.PlaybackState == CodeBrix.Audio.Wave.PlaybackState.Playing;

    /// <summary>
    /// The underlying player, for the controls that only a synthesized track has — per-channel
    /// volume for mixing a layered arrangement live, playback speed, and the MIDI message hooks that
    /// let notes drive game events.
    /// </summary>
    /// <remarks>
    /// Set the track's overall level through <see cref="MusicTrack.Volume"/> rather than the
    /// player's, so the music bus and any active duck still apply.
    /// </remarks>
    public MidiMusicPlayer Player => _player;

    /// <summary>
    /// What the instrument and the MIDI file complained about when this track was loaded. Empty
    /// when both read cleanly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sources, in this order. THE INSTRUMENT: an SFZ or Decent Sampler instrument reports the
    /// opcodes or features it asked for and did not get, a sample it could not resolve, and any
    /// memory decision that went against what the preset asked. A preset that needs an oscillator or
    /// a creative effect this engine does not synthesize still LOADS, and says here what would make
    /// it complete. A SoundFont reports nothing. THE FILE: a MIDI file is read leniently, so one
    /// that breaks a rule — an out-of-range key signature, a truncated track — plays instead of
    /// throwing, and says here what was wrong with it. That is common in machine-generated files.
    /// </para>
    /// <para>
    /// The list is also logged once as a warning when the track loads, so a game that never reads
    /// this property still leaves a trace of why an asset sounds odd.
    /// </para>
    /// <para>
    /// The path constructor reports the FILE's problems only: the instrument it built is held inside
    /// the player and is not handed back. Pass the instrument in — through one of the overloads that
    /// takes one — to see both.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Overrides the global engine pause's decision for this track. Music always suspends by
    /// default; see <see cref="AudioPauseRegistry"/>.
    /// </summary>
    public bool? SuspendOnEnginePause { get; set; }

    /// <summary>
    /// The tempo multiplier: 1.0 is the sequence's own tempo, 0.5 half speed, 2.0 double. It scales
    /// the tempo WITHOUT changing pitch — every note is still rendered at its written frequency, the
    /// sequence just advances more slowly or quickly.
    /// </summary>
    /// <remarks>
    /// This is something only synthesized music can do. A <see cref="FileMusicTrack"/> is already
    /// mixed down, so slowing it would drop its pitch with it; here the arrangement genuinely plays
    /// slower, which is what a slow-motion effect or a "heartbeat" low-health state wants. 0 freezes
    /// the transport and lets sounding voices ring out.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public float Speed
    {
        get => _disposed ? 1f : _player.Speed;
        set
        {
            if (!_disposed)
            {
                _player.Speed = value;
            }
        }
    }

    /// <summary>
    /// Sets one MIDI channel's level — the cheap way to layer a MIDI arrangement, and the MIDI
    /// counterpart of <see cref="MusicStemSet"/>.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0-15.</param>
    /// <param name="volume">The level, 0.0 (silent) to 1.0 (full). Clamped.</param>
    /// <remarks>
    /// <para>
    /// This is far cheaper than an audio-file stem set and cannot drift, because there are not
    /// several streams to keep in step — there is one sequence, and a channel within it is louder or
    /// quieter. It also costs no extra memory: the layers are already in the file.
    /// </para>
    /// <para>
    /// THE SEQUENCE CAN OVERWRITE THIS. It is sent as MIDI control change 7, so a track that
    /// automates its own volume will overwrite the game's value the next time it does. Reserve the
    /// channels a game means to control as layers, and leave their CC7 alone in the arrangement.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetLayerVolume(int channel, float volume)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, MidiChannelCount - 1);

        var clamped = Math.Clamp(volume, 0f, 1f);
        _layerVolume[channel] = clamped;

        if (!_disposed)
        {
            _player.SetChannelVolume(channel, clamped);
        }
    }

    /// <summary>
    /// The level last set for a channel by <see cref="SetLayerVolume"/> or
    /// <see cref="FadeLayerTo"/>, 1.0 if it has never been set.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0-15.</param>
    /// <returns>The level, 0.0 to 1.0.</returns>
    /// <remarks>
    /// This reports what the GAME asked for, which is not necessarily what is sounding: the sequence
    /// may have sent its own control change 7 since (see <see cref="SetLayerVolume"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public float GetLayerVolume(int channel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, MidiChannelCount - 1);

        return _layerVolume[channel];
    }

    /// <summary>
    /// Fades one MIDI channel's level over a duration — the MIDI counterpart of
    /// <see cref="MusicStem.FadeTo"/>.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0-15.</param>
    /// <param name="target">The level to end at, 0.0 to 1.0.</param>
    /// <param name="duration">How long the fade takes. Zero or less applies <paramref name="target"/> at once.</param>
    /// <remarks>
    /// It runs on the same clock as every other music fade, so it freezes with the global engine
    /// pause. Starting a second fade on the same channel replaces the first from where it had
    /// reached.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void FadeLayerTo(int channel, float target, TimeSpan duration = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, MidiChannelCount - 1);

        CancelLayerFade(channel);

        var clamped = Math.Clamp(target, 0f, 1f);

        if (duration <= TimeSpan.Zero)
        {
            SetLayerVolume(channel, clamped);
            return;
        }

        var from = _layerVolume[channel];
        var fade = MusicManager.Instance.Ticker.Add(from, clamped, duration, value => SetLayerVolume(channel, value));

        lock (_layerGate)
        {
            _layerFades[channel] = fade;
        }
    }

    /// <summary>Sets one MIDI channel's stereo position, as control change 10.</summary>
    /// <param name="channel">The MIDI channel, 0-15.</param>
    /// <param name="pan">The position, -1.0 (full left) through 0.0 (centre) to 1.0 (full right). Clamped.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetLayerPan(int channel, float pan)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, MidiChannelCount - 1);

        if (!_disposed)
        {
            _player.SetChannelPan(channel, Math.Clamp(pan, -1f, 1f));
        }
    }

    private void CancelLayerFade(int channel)
    {
        MusicFade? existing = null;

        lock (_layerGate)
        {
            if (_layerFades.Remove(channel, out var fade))
            {
                existing = fade;
            }
        }

        if (existing is not null)
        {
            MusicManager.Instance.Ticker.Cancel(existing);
        }
    }

    /// <inheritdoc/>
    public override void Seek(TimeSpan position)
    {
        if (!_disposed && _player.IsLoaded)
        {
            _player.Seek(position);
        }
    }

    /// <inheritdoc/>
    protected override void ApplyVolume(float volume)
    {
        if (!_disposed)
        {
            // Unlike AudioResource, MidiMusicPlayer is not an engine voice and does not apply the
            // bus itself, so the effective gain is computed here.
            _player.Volume = AudioMixer.EffectiveVolume(volume, AudioBus.Music);
        }
    }

    /// <inheritdoc cref="IMixerVoice.ApplyMixerVolume"/>
    void IMixerVoice.ApplyMixerVolume() => ApplyVolume(Volume);

    /// <inheritdoc/>
    internal override void StartCore(bool fromStart)
    {
        if (fromStart)
        {
            _player.Stop();
        }

        _player.Play();
    }

    /// <inheritdoc/>
    internal override void PauseCore() => _player.Pause();

    /// <inheritdoc/>
    internal override void ResumeCore() => _player.Play();

    /// <inheritdoc/>
    internal override void StopCore() => _player.Stop();

    bool IEnginePausableAudio.IsPlayingForEnginePause => IsPlaying;

    // Music is endless as far as the pause rules are concerned: null always suspends, which is what
    // a soundtrack should do. A short MIDI sting is the exception, and it reports its real length.
    TimeSpan? IEnginePausableAudio.KnownDurationForEnginePause
        => IsLooping ? null : Duration == TimeSpan.Zero ? null : Duration;

    bool? IEnginePausableAudio.SuspendOnEnginePause => SuspendOnEnginePause ?? true;

    void IEnginePausableAudio.EnginePause()
    {
        if (!_disposed && IsPlaying)
        {
            _pausedByEngine = true;
            _player.Pause();
        }
    }

    void IEnginePausableAudio.EngineResume()
    {
        if (!_disposed && _pausedByEngine)
        {
            _pausedByEngine = false;
            _player.Play();
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_layerGate)
        {
            foreach (var fade in _layerFades.Values)
            {
                MusicManager.Instance.Ticker.Cancel(fade);
            }

            _layerFades.Clear();
        }

        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => RaiseEnded();
}
