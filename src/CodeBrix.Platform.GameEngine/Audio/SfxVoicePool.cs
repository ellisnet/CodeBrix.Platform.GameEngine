using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A fixed-size pool of reusable sound-effect voices: route rapid-fire SFX triggers through
/// it instead of creating a player per shot. The pool pre-allocates its voices once (no
/// per-play player allocation → no GC stutter), plays only preloaded
/// <see cref="CachedSound"/> data (no decode or I/O on the audio thread), and enforces a
/// polyphony cap — when every voice is busy, <see cref="CullPolicy"/> decides whether the
/// new trigger steals a voice or is dropped.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AudioResourceManager.SfxPool"/> provides a shared default instance and
/// <see cref="AudioResourceManager.TryPlaySfx"/> routes key-based triggers through it; games
/// with special needs (several pools, custom sizes) construct their own.
/// </para>
/// <para>
/// Voices participate in the global engine pause (<see cref="Engine.Pause"/>) with the same
/// automatic rule as other engine audio: short fire-and-forget effects ring out, longer
/// material suspends; override per pool with <see cref="SuspendOnEnginePause"/>.
/// </para>
/// <para>
/// Triggering is thread-safe. A voice returns to the pool when its sound finishes (note the
/// shared output's ~25 ms stopped-detection sweep) or is stopped.
/// </para>
/// </remarks>
public sealed class SfxVoicePool : IDisposable
{
    private readonly object _gate = new();
    private readonly Voice[] _voices;
    private AudioBus _bus = AudioBus.Sfx;
    private long _playSequence;
    private bool _disposed;

    /// <summary>
    /// Creates a pool with <paramref name="size"/> pre-allocated voices. The size is the
    /// pool's polyphony cap; 16–64 covers typical games.
    /// </summary>
    /// <param name="size">The fixed number of voices. Defaults to 32.</param>
    public SfxVoicePool(int size = 32)
    {
        if (size < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "An SFX voice pool needs at least one voice.");
        }

        _voices = new Voice[size];
        for (int i = 0; i < size; i++)
        {
            _voices[i] = new Voice(this);
            AudioPauseRegistry.Register(_voices[i]);
        }
    }

    /// <summary>The fixed voice count (the polyphony cap) chosen at construction.</summary>
    public int Size => _voices.Length;

    /// <summary>The number of voices currently playing.</summary>
    public int ActiveVoiceCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (var voice in _voices)
                {
                    if (voice.Busy)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// What happens to a new trigger when every voice is busy. Defaults to
    /// <see cref="SfxCullPolicy.CullOldest"/>.
    /// </summary>
    public SfxCullPolicy CullPolicy { get; set; } = SfxCullPolicy.CullOldest;

    /// <summary>
    /// Pool-wide override of the global engine pause's suspend decision for these voices:
    /// <c>true</c> always suspends, <c>false</c> never suspends, <c>null</c> (the default)
    /// applies the automatic short-sound-effect exemption.
    /// </summary>
    public bool? SuspendOnEnginePause { get; set; }

    /// <summary>
    /// The mixer bus every voice in this pool plays on. Defaults to <see cref="AudioBus.Sfx"/>, so
    /// the player's effects slider controls it. Changing it applies to voices already playing.
    /// </summary>
    public AudioBus Bus
    {
        get => _bus;
        set
        {
            _bus = value;
            foreach (var voice in _voices)
            {
                ((IMixerVoice)voice).ApplyMixerVolume();
            }
        }
    }

    /// <summary>
    /// Plays a preloaded sound on a pool voice. When the pool is full, <see cref="CullPolicy"/>
    /// decides whether an existing voice is stolen for it.
    /// </summary>
    /// <param name="sound">The preloaded sound to play.</param>
    /// <param name="volume">The voice volume, 0.0–1.0. Defaults to 1.0.</param>
    /// <param name="pan">The stereo pan position, -1.0 (left) to 1.0 (right). Defaults to 0.0 (center).</param>
    /// <param name="priority">
    /// The trigger's priority for <see cref="SfxCullPolicy.CullLowestPriority"/> (higher wins;
    /// map camera distance or gameplay importance onto it). Ignored by the other policies.
    /// </param>
    /// <returns><see langword="true"/> if the sound started; <see langword="false"/> if it was dropped.</returns>
    public bool TryPlay(CachedSound sound, float volume = 1.0f, float pan = 0.0f, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(sound);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int slot = SelectVoiceSlot(
                CullPolicy,
                Array.ConvertAll(_voices, voice => voice.Busy),
                Array.ConvertAll(_voices, voice => voice.Sequence),
                Array.ConvertAll(_voices, voice => voice.Priority),
                priority,
                out bool culled);

            if (slot < 0)
            {
                Engine.Logger.LogDebug(
                    "SfxVoicePool full ({Size} voices): trigger dropped by the {Policy} policy (priority {Priority}).",
                    Size, CullPolicy, priority);
                return false;
            }

            if (culled)
            {
                Engine.Logger.LogDebug(
                    "SfxVoicePool full ({Size} voices): culling a voice via the {Policy} policy for a new trigger (priority {Priority}).",
                    Size, CullPolicy, priority);
            }

            _voices[slot].Start(sound, volume, pan, priority, ++_playSequence);
            return true;
        }
    }

    /// <summary>
    /// Plays a preloaded <see cref="AudioResource"/> (one whose short-effect PCM was cached at
    /// load time — see <see cref="AudioResource.IsPreloaded"/>) on a pool voice. Resources
    /// that were not preloaded are rejected rather than decoded on the fly.
    /// </summary>
    /// <param name="resource">The preloaded audio resource to play.</param>
    /// <param name="volume">The voice volume, 0.0–1.0. Defaults to 1.0.</param>
    /// <param name="pan">The stereo pan position, -1.0 (left) to 1.0 (right). Defaults to 0.0 (center).</param>
    /// <param name="priority">The trigger's priority for <see cref="SfxCullPolicy.CullLowestPriority"/>.</param>
    /// <returns><see langword="true"/> if the sound started; <see langword="false"/> if it was dropped.</returns>
    public bool TryPlay(AudioResource resource, float volume = 1.0f, float pan = 0.0f, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.CachedData is not { } cached)
        {
            Engine.Logger.LogWarning(
                "AudioResource '{Key}' is not preloaded, so SfxVoicePool will not play it (decoding on trigger stutters). " +
                "Raise AudioResourceManager.PreloadShortSoundEffectMaxSeconds so it preloads at load time, or play it via its own voice.",
                resource.Key);
            return false;
        }

        return TryPlay(cached, volume, pan, priority);
    }

    /// <summary>
    /// Plays a preloaded audio resource from <see cref="AudioResourceManager"/> by key on a
    /// pool voice.
    /// </summary>
    /// <param name="key">The <see cref="AudioResourceManager"/> key of the preloaded resource.</param>
    /// <param name="volume">The voice volume, 0.0–1.0. Defaults to 1.0.</param>
    /// <param name="pan">The stereo pan position, -1.0 (left) to 1.0 (right). Defaults to 0.0 (center).</param>
    /// <param name="priority">The trigger's priority for <see cref="SfxCullPolicy.CullLowestPriority"/>.</param>
    /// <returns><see langword="true"/> if the sound started; <see langword="false"/> if it was dropped.</returns>
    public bool TryPlay(string key, float volume = 1.0f, float pan = 0.0f, int priority = 0)
    {
        if (!AudioResourceManager.Instance.TryGet(key, out var resource) || resource is null)
        {
            Engine.Logger.LogWarning("SfxVoicePool: no AudioResource is loaded under the key '{Key}'.", key);
            return false;
        }

        return TryPlay(resource, volume, pan, priority);
    }

    /// <summary>Stops every playing voice and returns it to the pool.</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var voice in _voices)
            {
                voice.StopAndFree();
            }
        }
    }

    /// <summary>Stops and disposes every voice in the pool.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var voice in _voices)
            {
                voice.Dispose();
            }
        }
    }

    /// <summary>
    /// The pool's slot-selection/cull decision, kept pure for testability: returns the slot
    /// to (re)use, or -1 when the trigger should be dropped. <paramref name="culled"/>
    /// reports whether the returned slot is stealing a busy voice.
    /// </summary>
    internal static int SelectVoiceSlot(
        SfxCullPolicy policy,
        bool[] busy,
        long[] sequences,
        int[] priorities,
        int newPriority,
        out bool culled)
    {
        culled = false;

        for (int i = 0; i < busy.Length; i++)
        {
            if (!busy[i])
            {
                return i;
            }
        }

        switch (policy)
        {
            case SfxCullPolicy.RejectNew:
                return -1;

            case SfxCullPolicy.CullOldest:
            {
                int oldest = 0;
                for (int i = 1; i < busy.Length; i++)
                {
                    if (sequences[i] < sequences[oldest])
                    {
                        oldest = i;
                    }
                }

                culled = true;
                return oldest;
            }

            case SfxCullPolicy.CullLowestPriority:
            {
                int candidate = 0;
                for (int i = 1; i < busy.Length; i++)
                {
                    if (priorities[i] < priorities[candidate]
                        || (priorities[i] == priorities[candidate] && sequences[i] < sequences[candidate]))
                    {
                        candidate = i;
                    }
                }

                if (priorities[candidate] > newPriority)
                {
                    return -1; // every playing voice outranks the new trigger
                }

                culled = true;
                return candidate;
            }

            default:
                return -1;
        }
    }

    /// <summary>
    /// One reusable pool voice: a pre-allocated player whose per-play graph is a
    /// <see cref="CachedSoundSampleProvider"/> plus a pan stage (the same mono/stereo/mux
    /// handling as <see cref="AudioResource"/>); volume rides the player's own master gain.
    /// </summary>
    private sealed class Voice : IEnginePausableAudio, IMixerVoice, IDisposable
    {
        private readonly SfxVoicePool _pool;
        private readonly WaveOutEvent _player = new();
        private TimeSpan _duration;
        private float _volume = 1f;

        internal bool Busy;
        internal long Sequence;
        internal int Priority;

        internal Voice(SfxVoicePool pool)
        {
            _pool = pool;
            _player.PlaybackStopped += OnPlaybackStopped;
            AudioMixer.Register(this);
        }

        internal void Start(CachedSound sound, float volume, float pan, int priority, long sequence)
        {
            _player.Stop(); // ensure the Stopped state Init requires (also frees a culled voice)

            ISampleProvider graph = new CachedSoundSampleProvider(sound);
            pan = Math.Clamp(pan, -1f, 1f);
            switch (sound.Channels)
            {
                case 1:
                    graph = new PanningSampleProvider(graph) { Pan = pan };
                    break;

                case 2:
                {
                    var stereoPan = new StereoPanSampleProvider(graph);
                    AudioResource.ApplyStereoPan(stereoPan, pan);
                    graph = stereoPan;
                    break;
                }

                default:
                {
                    var mux = new MultiplexingSampleProvider(new[] { graph }, 2);
                    mux.ConnectInputToOutput(0, 0);
                    mux.ConnectInputToOutput(1, 1);
                    var stereoPan = new StereoPanSampleProvider(mux);
                    AudioResource.ApplyStereoPan(stereoPan, pan);
                    graph = stereoPan;
                    break;
                }
            }

            _player.Init(graph);
            _volume = Math.Clamp(volume, 0f, 1f);
            _player.Volume = AudioMixer.EffectiveVolume(_volume, _pool.Bus);
            _player.Play();

            Busy = true;
            Sequence = sequence;
            Priority = priority;
            _duration = sound.Duration;
        }

        internal void StopAndFree()
        {
            _player.Stop();
            Busy = false;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            lock (_pool._gate)
            {
                // A stale stop notification can arrive after this voice was culled and
                // restarted; only free the voice when it is actually stopped now.
                if (_player.PlaybackState == PlaybackState.Stopped)
                {
                    Busy = false;
                }
            }
        }

        bool IEnginePausableAudio.IsPlayingForEnginePause
            => _player.PlaybackState == PlaybackState.Playing;

        TimeSpan? IEnginePausableAudio.KnownDurationForEnginePause => _duration;

        bool? IEnginePausableAudio.SuspendOnEnginePause => _pool.SuspendOnEnginePause;

        // A bus volume can change mid-play, so a pooled voice recomputes its gain like any other.
        void IMixerVoice.ApplyMixerVolume()
        {
            if (Busy)
            {
                _player.Volume = AudioMixer.EffectiveVolume(_volume, _pool.Bus);
            }
        }

        void IEnginePausableAudio.EnginePause() => _player.Pause();

        void IEnginePausableAudio.EngineResume() => _player.Play();

        public void Dispose()
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Dispose();
            Busy = false;
        }
    }
}
