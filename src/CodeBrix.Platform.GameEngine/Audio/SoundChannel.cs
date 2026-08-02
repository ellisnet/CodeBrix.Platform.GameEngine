using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A fixed playback channel in the classic game-audio sense: allocate N channels once, then
/// swap clips onto them and restart them as often as the game likes. Each channel owns one
/// voice on the shared audio output, and hides the rate-conversion and pitch chain so any
/// raw-PCM clip (see <see cref="AudioResourceManager.LoadFromPcm"/>) plays correctly on the
/// pinned device.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AudioSystem.Initialize"/> must be called before channels are created. Members
/// are intended to be called from one game thread; live property changes reach the audio
/// callback thread without locks.
/// </para>
/// <para>
/// <see cref="State"/> transitions to <see cref="PlaybackState.Stopped"/> lag reality by up
/// to ~25 ms: end-of-stream is detected by the shared output's sweep timer, which runs on a
/// 25 ms interval. Treat "just finished" states as approximate.
/// </para>
/// </remarks>
public sealed class SoundChannel : IDisposable, IEnginePausableAudio, IMixerVoice
{
    private readonly WaveOutEvent _output = new();
    private WaveStream? _reader;
    private VariableRateSampleProvider? _rateProvider;
    private PanningSampleProvider? _monoPanProvider;
    private StereoPanSampleProvider? _stereoPanProvider;
    private VolumeSampleProvider? _volumeProvider;

    private float _volume = 1f;
    private AudioBus _bus = AudioBus.Sfx;
    private float _pan;
    private float _pitch = 1f;
    private bool _isDisposed;

    /// <summary>
    /// Creates a channel. <see cref="AudioSystem.Initialize"/> must have been called first
    /// so the device rate the channel converts to is pinned.
    /// </summary>
    public SoundChannel()
    {
        if (!AudioSystem.IsInitialized)
        {
            throw new InvalidOperationException(
                $"Call {nameof(AudioSystem)}.{nameof(AudioSystem.Initialize)}() before creating {nameof(SoundChannel)} instances; channels rate-convert to the pinned device rate.");
        }

        AudioPauseRegistry.Register(this);
        AudioMixer.Register(this);
    }

    /// <summary>
    /// Overrides the global engine pause's suspend decision for this channel: <c>true</c>
    /// always suspends, <c>false</c> never suspends, <c>null</c> (the default) applies the
    /// automatic exemption — a playing clip no longer than
    /// <see cref="Configuration.EngineConfiguration.PauseShortSoundEffectSeconds"/> is treated
    /// as a fire-and-forget effect and left to ring out.
    /// </summary>
    public bool? SuspendOnEnginePause { get; set; }

    bool IEnginePausableAudio.IsPlayingForEnginePause => !_isDisposed && State == PlaybackState.Playing;

    TimeSpan? IEnginePausableAudio.KnownDurationForEnginePause
        => Duration > TimeSpan.Zero ? Duration : null;

    void IEnginePausableAudio.EnginePause()
    {
        if (!_isDisposed)
        {
            Pause();
        }
    }

    void IEnginePausableAudio.EngineResume()
    {
        if (!_isDisposed)
        {
            Resume();
        }
    }

    /// <summary>The key of the clip currently set on this channel, if any.</summary>
    public string? ClipKey { get; private set; }

    /// <summary>
    /// The playback state of this channel's voice. Stopped-state detection lags by up to
    /// ~25 ms (see the class remarks).
    /// </summary>
    public PlaybackState State => _output.PlaybackState;

    /// <summary>
    /// The approximate playback position within the clip. The audio pipeline reads ahead of
    /// what is audible, so this leads the speaker output slightly; <see cref="Pitch"/> is
    /// not factored in.
    /// </summary>
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    /// <summary>The duration of the current clip at <see cref="Pitch"/> 1.0, or zero when no clip is set.</summary>
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>The channel volume, 0.0 (silent) to 1.0 (full). May be changed while playing.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            ApplyMixerVolume();
        }
    }

    /// <summary>
    /// The mixer bus this channel's <see cref="Volume"/> is scaled by. Defaults to
    /// <see cref="AudioBus.Sfx"/>.
    /// </summary>
    public AudioBus Bus
    {
        get => _bus;
        set
        {
            _bus = value;
            ApplyMixerVolume();
        }
    }

    /// <inheritdoc cref="IMixerVoice.ApplyMixerVolume"/>
    void IMixerVoice.ApplyMixerVolume() => ApplyMixerVolume();

    private void ApplyMixerVolume()
    {
        if (_volumeProvider is not null)
        {
            _volumeProvider.Volume = AudioMixer.EffectiveVolume(_volume, _bus);
        }
    }

    /// <summary>The stereo pan, -1.0 (left) through 0.0 (center) to 1.0 (right). May be changed while playing.</summary>
    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1f, 1f);
            ApplyPan();
        }
    }

    /// <summary>
    /// The pitch multiplier (1.0 = unchanged; see <see cref="VariableRateSampleProvider.Pitch"/>).
    /// May be changed while playing.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = Math.Clamp(value, 0.05f, 20f);
            if (_rateProvider is not null)
            {
                _rateProvider.Pitch = _pitch;
            }
        }
    }

    /// <summary>
    /// Sets (or swaps) the clip this channel plays: any resource in
    /// <see cref="AudioResourceManager"/> that holds in-memory source data — raw-PCM clips
    /// from <see cref="AudioResourceManager.LoadFromPcm"/> always qualify. Playback in
    /// progress is stopped. Designed to be called constantly.
    /// </summary>
    /// <param name="key">The <see cref="AudioResourceManager"/> key of the clip.</param>
    public void SetClip(string key)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (AudioResourceManager.Instance.Get(key) is not { } resource)
        {
            throw new ArgumentException($"No audio resource with the key '{key}' is loaded in the {nameof(AudioResourceManager)}.", nameof(key));
        }

        // The documented clip-swap dance: a voice can only be re-initialized while stopped.
        _output.Stop();
        _reader?.Dispose();

        _reader = resource.CreateIndependentReader();
        ISampleProvider provider = _reader.ToSampleProvider();

        _rateProvider = new VariableRateSampleProvider(provider, AudioSystem.DeviceSampleRate)
        {
            Pitch = _pitch,
        };
        provider = _rateProvider;

        if (provider.WaveFormat.Channels == 1)
        {
            _monoPanProvider = new PanningSampleProvider(provider); // mono in, stereo out
            _stereoPanProvider = null;
            provider = _monoPanProvider;
        }
        else
        {
            _stereoPanProvider = new StereoPanSampleProvider(provider);
            _monoPanProvider = null;
            provider = _stereoPanProvider;
        }
        ApplyPan();

        _volumeProvider = new VolumeSampleProvider(provider)
        {
            Volume = AudioMixer.EffectiveVolume(_volume, _bus),
        };

        _output.Init(_volumeProvider);
        ClipKey = key;
    }

    /// <summary>
    /// Plays the current clip from its start, optionally setting <see cref="Volume"/>,
    /// <see cref="Pan"/>, and <see cref="Pitch"/> for this play in the same call. A play in
    /// progress restarts.
    /// </summary>
    public void Play(float? volume = null, float? pan = null, float? pitch = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_reader is not { } reader)
        {
            throw new InvalidOperationException($"Call {nameof(SetClip)} before {nameof(Play)}.");
        }

        if (volume is { } newVolume)
        {
            Volume = newVolume;
        }
        if (pan is { } newPan)
        {
            Pan = newPan;
        }
        if (pitch is { } newPitch)
        {
            Pitch = newPitch;
        }

        _output.Stop();
        reader.Position = 0;
        _output.Play();
    }

    /// <summary>Stops playback on this channel; the clip stays set.</summary>
    public void Stop() => _output.Stop();

    /// <summary>Pauses playback on this channel.</summary>
    public void Pause()
    {
        if (State == PlaybackState.Playing)
        {
            _output.Pause();
        }
    }

    /// <summary>Resumes paused playback on this channel.</summary>
    public void Resume()
    {
        if (State == PlaybackState.Paused)
        {
            _output.Play();
        }
    }

    /// <summary>Stops the channel and releases its voice.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _output.Stop();
        _output.Dispose();
        _reader?.Dispose();
        _reader = null;
    }

    private void ApplyPan()
    {
        if (_monoPanProvider is not null)
        {
            _monoPanProvider.Pan = _pan;
        }
        else if (_stereoPanProvider is not null)
        {
            // Equal-power pan: map [-1..1] to [0..pi/2].
            var angle = (_pan + 1f) * 0.5f * (float)(Math.PI / 2);
            _stereoPanProvider.LeftVolume = MathF.Cos(angle);
            _stereoPanProvider.RightVolume = MathF.Sin(angle);
        }
    }
}
