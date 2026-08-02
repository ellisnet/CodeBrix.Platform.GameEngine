using System;
using System.Collections.Generic;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Music that is a MIDI sequence rendered live through a SoundFont (<c>.sf2</c>) or an SFZ
/// instrument (<c>.sfz</c>) — the format that costs kilobytes on disk instead of megabytes, and
/// whose arrangement the game can change while it plays.
/// </summary>
/// <remarks>
/// <para>
/// SHARE THE INSTRUMENT. A SoundFont runs to tens of megabytes and an SFZ instrument decodes its
/// samples eagerly, so load through <see cref="SoundFontCache"/> or <see cref="SfzInstrumentCache"/>
/// and hand the same instance to every track. The MIDI sequence itself is small.
/// </para>
/// <para>
/// SFZ FROM AN ASSET PACK IS NOT THE SAME AS SF2 FROM ONE. A <c>.sf2</c> is a single file and loads
/// from a <see cref="System.IO.Stream"/>. A <c>.sfz</c> is a text file that REFERENCES sample files
/// beside it on disk, so it needs a real directory: an SFZ instrument packed into an
/// <see cref="Assets.AssetsFile"/> must be extracted before it can be loaded.
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

    /// <summary>Creates a track over a MIDI sequence rendered by a SoundFont.</summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="soundFont">The SoundFont to render with — share one instance across tracks.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MidiMusicTrack(string key, SoundFont soundFont, MidiSequence sequence)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(soundFont);
        ArgumentNullException.ThrowIfNull(sequence);
        _player.Load(soundFont, sequence);
    }

    /// <summary>Creates a track over a MIDI sequence rendered by an SFZ instrument.</summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="instrument">The SFZ instrument to render with — share one instance across tracks.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MidiMusicTrack(string key, SfzInstrument instrument, MidiSequence sequence)
        : this(key)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(sequence);
        _player.Load(instrument, sequence);
    }

    /// <summary>
    /// Creates a track from an instrument file and a MIDI file on disk. The instrument's extension
    /// decides the synthesizer: <c>.sfz</c> loads an SFZ instrument, anything else a SoundFont.
    /// </summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="instrumentPath">Path to a <c>.sf2</c> or <c>.sfz</c> file.</param>
    /// <param name="midiFilePath">Path to a Standard MIDI File.</param>
    /// <remarks>
    /// <para>
    /// This overload loads the instrument fresh every time. For more than one track over the same
    /// instrument, load it through a cache and use the overloads above instead.
    /// </para>
    /// <para>
    /// It is also the overload that fills in <see cref="MusicTrack.Timeline"/>, because it is the
    /// only one with a MIDI FILE to read the tempo, time signature and markers from — the
    /// <see cref="MidiSequence"/> the other overloads take has already discarded them. Quantised
    /// transitions and <see cref="MusicManager.JumpToMarker"/> therefore work with no further
    /// wiring here, and need <see cref="MusicTrack.Timeline"/> set by hand there.
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
