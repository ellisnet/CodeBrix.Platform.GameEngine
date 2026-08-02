using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// The game's music transport: what is playing, fades and crossfades between tracks, ducking under
/// dialogue, stingers, and playlists.
/// </summary>
/// <remarks>
/// <para>
/// This is to music what <see cref="SfxVoicePool"/> is to sound effects — the place the policy
/// lives, so a game does not reimplement fade timing and "which track is current" bookkeeping.
/// Everything it plays is on <see cref="AudioBus.Music"/>, so the player's music slider works with
/// no further wiring.
/// </para>
/// <para>
/// WHAT DRIVES THE FADES: a single background thread (<see cref="MusicFadeTicker"/>), because fades
/// must behave identically in both hosting modes and Mode B never runs the engine cycle. It costs
/// nothing when no fade is in flight and freezes with the global engine pause.
/// </para>
/// <para>
/// THREADING: every method here is safe to call from any thread. Track <c>Ended</c> events and
/// playlist advances arrive on a background or audio thread — marshal to the engine thread with
/// <c>Engine.Instance.EngineDispatcher.Post</c> before touching game state.
/// </para>
/// </remarks>
public sealed class MusicManager : IDisposable
{
    private static readonly Lazy<MusicManager> _instance = new(() => new MusicManager());

    private readonly object _gate = new();
    private readonly MusicFadeTicker _ticker = new();
    private readonly List<DuckHandle> _ducks = new();
    private readonly List<AudioResource> _stingers = new();

    private MusicTrack? _current;
    private MusicTrack? _outgoing;
    private MusicFade? _currentFade;
    private MusicFade? _duckFade;
    private MusicFade? _pendingTransition;
    private MusicPlaylist? _playlist;
    private bool _disposed;

    private MusicManager()
    { }

    /// <summary>The shared music manager.</summary>
    public static MusicManager Instance => _instance.Value;

    /// <summary>
    /// The gain law used by <see cref="CrossfadeTo"/>. Defaults to
    /// <see cref="MusicFadeCurve.EqualPower"/>, which is what keeps a crossfade from dipping in the
    /// middle.
    /// </summary>
    public MusicFadeCurve CrossfadeCurve { get; set; } = MusicFadeCurve.EqualPower;

    /// <summary>The track currently playing, or <see langword="null"/> when the music is stopped.</summary>
    public MusicTrack? NowPlaying
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>Whether music is currently sounding.</summary>
    public bool IsPlaying
    {
        get { lock (_gate) { return _current is not null && _current.IsPlaying; } }
    }

    /// <summary>The playlist currently driving track changes, or <see langword="null"/>.</summary>
    public MusicPlaylist? Playlist
    {
        get { lock (_gate) { return _playlist; } }
    }

    /// <summary>The number of fades in flight. Diagnostics: normally 0, or 1 during a transition.</summary>
    public int ActiveFadeCount => _ticker.ActiveFadeCount;

    /// <summary>
    /// Plays a track, replacing whatever was playing. Any current track is stopped immediately —
    /// use <see cref="CrossfadeTo"/> to overlap them instead.
    /// </summary>
    /// <param name="track">The track to play. Disposing it remains the caller's business.</param>
    /// <param name="fadeIn">How long to fade in over. Zero starts at full volume.</param>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public void Play(MusicTrack track, TimeSpan fadeIn = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        // A transition the game queued for the next bar must not land after it has changed its mind
        // and started something else outright.
        CancelPendingTransition();

        MusicTrack? previous;
        lock (_gate)
        {
            ThrowIfDisposed();

            CancelCurrentFadeLocked();
            previous = _current;
            _current = track;
        }

        if (previous is not null && !ReferenceEquals(previous, track))
        {
            SafeInvoke(previous.StopCore, "stop the previous music track");
        }

        track.Volume = fadeIn > TimeSpan.Zero ? 0f : 1f;
        SafeInvoke(() => track.StartCore(fromStart: true), "start a music track");

        if (fadeIn > TimeSpan.Zero)
        {
            var fade = _ticker.Add(0f, 1f, fadeIn, t => track.Volume = t);
            lock (_gate)
            {
                _currentFade = fade;
            }
        }
    }

    /// <summary>
    /// Crossfades from the current track to another: both play at once, the outgoing one fading
    /// down as the incoming one fades up, following <see cref="CrossfadeCurve"/>.
    /// </summary>
    /// <param name="track">The track to fade in.</param>
    /// <param name="duration">How long the crossfade takes. Zero or less is a plain <see cref="Play"/>.</param>
    /// <remarks>
    /// The outgoing track is stopped once it reaches silence. If nothing is playing this is a fade
    /// in. Starting a second crossfade while one is running finishes the first immediately.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public void CrossfadeTo(MusicTrack track, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(track);

        CancelPendingTransition();

        if (duration <= TimeSpan.Zero)
        {
            Play(track);
            return;
        }

        MusicTrack? outgoing;
        var curve = CrossfadeCurve;

        lock (_gate)
        {
            ThrowIfDisposed();

            CancelCurrentFadeLocked();
            FinishOutgoingLocked();

            outgoing = _current;
            _outgoing = outgoing;
            _current = track;
        }

        if (outgoing is null)
        {
            Play(track, duration);
            return;
        }

        track.Volume = 0f;
        SafeInvoke(() => track.StartCore(fromStart: true), "start the incoming music track");

        // ONE fade drives BOTH sides. Two independent fades could drift apart by a tick and leave a
        // hole (or a bump) in the middle; complementary values computed from a single progress
        // cannot.
        var fade = _ticker.Add(0f, 1f, duration,
            t =>
            {
                track.Volume = MusicFadeCurves.GainAt(curve, t);
                outgoing.Volume = MusicFadeCurves.GainAt(curve, 1f - t);
            },
            () =>
            {
                SafeInvoke(outgoing.StopCore, "stop the outgoing music track");
                lock (_gate)
                {
                    if (ReferenceEquals(_outgoing, outgoing))
                    {
                        _outgoing = null;
                    }
                }
            });

        lock (_gate)
        {
            _currentFade = fade;
        }
    }

    /// <summary>Stops the music, optionally fading out first.</summary>
    /// <param name="fadeOut">How long to fade out over. Zero stops immediately.</param>
    public void Stop(TimeSpan fadeOut = default)
    {
        CancelPendingTransition();

        MusicTrack? current;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CancelCurrentFadeLocked();
            FinishOutgoingLocked();

            current = _current;
            _current = null;
            _playlist = null;
        }

        if (current is null)
        {
            return;
        }

        if (fadeOut <= TimeSpan.Zero)
        {
            SafeInvoke(current.StopCore, "stop the music");
            return;
        }

        var from = current.Volume;
        _ticker.Add(from, 0f, fadeOut,
            volume => current.Volume = volume,
            () => SafeInvoke(current.StopCore, "stop the music after its fade out"));
    }

    /// <summary>Pauses the music where it is. A fade in flight is left where it is too.</summary>
    public void Pause()
    {
        MusicTrack? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is not null)
        {
            SafeInvoke(current.PauseCore, "pause the music");
        }
    }

    /// <summary>Resumes music paused by <see cref="Pause"/>.</summary>
    public void Resume()
    {
        MusicTrack? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is not null)
        {
            SafeInvoke(current.ResumeCore, "resume the music");
        }
    }

    /// <summary>Seeks the current track.</summary>
    /// <param name="position">The position to seek to.</param>
    public void Seek(TimeSpan position)
    {
        MusicTrack? current;
        lock (_gate)
        {
            current = _current;
        }

        current?.Seek(position);
    }

    // ----- quantised transitions -----

    /// <summary>
    /// Plays a track, waiting for the next beat or bar of what is playing before it starts.
    /// </summary>
    /// <param name="track">The track to play.</param>
    /// <param name="fadeIn">How long to fade in over, once the transition starts.</param>
    /// <param name="quantize">The boundary to wait for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public void Play(MusicTrack track, TimeSpan fadeIn, MusicTransitionQuantize quantize)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!TryQueueTransition(quantize, () => Play(track, fadeIn)))
        {
            Play(track, fadeIn);
        }
    }

    /// <summary>
    /// Crossfades to a track, waiting for the next beat or bar of what is playing before the
    /// crossfade begins — so the new material enters on the pulse rather than wherever the game
    /// happened to ask.
    /// </summary>
    /// <param name="track">The track to fade in.</param>
    /// <param name="duration">How long the crossfade takes, once it starts.</param>
    /// <param name="quantize">The boundary to wait for.</param>
    /// <remarks>
    /// The wait runs on the same clock as the fades, so it freezes with the global engine pause: a
    /// transition queued for the next bar cannot fire while the game is paused. If the current track
    /// has no <see cref="MusicTrack.Timeline"/> this is a plain crossfade, and says so in the log.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public void CrossfadeTo(MusicTrack track, TimeSpan duration, MusicTransitionQuantize quantize)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!TryQueueTransition(quantize, () => CrossfadeTo(track, duration)))
        {
            CrossfadeTo(track, duration);
        }
    }

    /// <summary>Stops the music on the next beat or bar.</summary>
    /// <param name="fadeOut">How long to fade out over, once the transition starts.</param>
    /// <param name="quantize">The boundary to wait for.</param>
    public void Stop(TimeSpan fadeOut, MusicTransitionQuantize quantize)
    {
        if (!TryQueueTransition(quantize, () => Stop(fadeOut)))
        {
            Stop(fadeOut);
        }
    }

    /// <summary>Whether a transition is queued and waiting for a musical boundary.</summary>
    public bool HasPendingTransition
    {
        get { lock (_gate) { return _pendingTransition is not null; } }
    }

    /// <summary>
    /// Drops a queued transition that has not fired yet. The music carries on unchanged.
    /// </summary>
    /// <remarks>
    /// For the case where the game asked for a change on the next bar and the situation resolved
    /// before the bar arrived — the enemy died, the player left the trigger. Without this the
    /// transition would still land, a beat too late to make sense.
    /// </remarks>
    public void CancelPendingTransition()
    {
        MusicFade? pending;

        lock (_gate)
        {
            pending = _pendingTransition;
            _pendingTransition = null;
        }

        _ticker.Cancel(pending);
    }

    /// <summary>
    /// Seeks the current track to one of its named markers — the jump points a MIDI file's markers
    /// become.
    /// </summary>
    /// <param name="name">The marker's name; the comparison ignores case.</param>
    /// <returns>
    /// <see langword="true"/> if the jump happened; <see langword="false"/> if nothing is playing,
    /// the track has no timeline, or it has no marker of that name.
    /// </returns>
    public bool JumpToMarker(string name)
    {
        MusicTrack? current;
        lock (_gate)
        {
            current = _current;
        }

        var timeline = current?.Timeline;
        if (current is null || timeline is null || !timeline.TryGetMarker(name, out var time))
        {
            return false;
        }

        current.Seek(time);
        return true;
    }

    // Returns true when the transition has been QUEUED for later; false means "do it now".
    private bool TryQueueTransition(MusicTransitionQuantize quantize, Action transition)
    {
        if (quantize == MusicTransitionQuantize.Immediate)
        {
            return false;
        }

        MusicTrack? current;
        lock (_gate)
        {
            ThrowIfDisposed();
            current = _current;
        }

        if (current is null)
        {
            return false; // nothing playing: there is no grid to wait for, and no gap to fill
        }

        var timeline = current.Timeline;
        if (timeline is null)
        {
            Engine.Logger.LogWarning(
                "Music track '{Key}' has no Timeline, so a {Quantize}-quantised transition ran immediately. "
                + "MIDI tracks loaded from a file get one automatically; for decoded audio set "
                + "MusicTrack.Timeline, because a beat grid cannot be inferred from a decoded stream.",
                current.Key, quantize);

            return false;
        }

        var wait = timeline.TimeToNextBoundary(current.Position, quantize);
        if (wait <= TimeSpan.Zero)
        {
            return false; // already on the boundary
        }

        CancelPendingTransition();

        // The wait is a fade that applies nothing: it exists to borrow the ticker's clock, which
        // freezes with the global engine pause exactly as the transition itself would.
        var fade = _ticker.Add(0f, 1f, wait, _ => { },
            () =>
            {
                lock (_gate)
                {
                    _pendingTransition = null;
                }

                transition();
            });

        lock (_gate)
        {
            _pendingTransition = fade;
        }

        return true;
    }

    // ----- ducking -----

    /// <summary>
    /// Ducks the music until the returned handle is disposed — the shape for "quiet while this
    /// dialogue line plays". Overlapping ducks are reference-counted and the DEEPEST wins, so two
    /// lines that overlap do not fight and the music comes back only when the last one ends.
    /// </summary>
    /// <param name="depth">The level to duck to, 0.0 (silent) to 1.0 (no ducking).</param>
    /// <param name="attack">How long to fade down over.</param>
    /// <param name="release">How long to fade back up over once every duck is released.</param>
    /// <returns>A handle; dispose it to release this duck.</returns>
    public IDisposable PushDuck(float depth, TimeSpan attack = default, TimeSpan release = default)
    {
        var handle = new DuckHandle(this, Math.Clamp(depth, 0f, 1f), release);

        lock (_gate)
        {
            ThrowIfDisposed();
            _ducks.Add(handle);
        }

        ApplyDuck(attack);
        return handle;
    }

    /// <summary>
    /// Ducks the music for a fixed time, then restores it — fire and forget, for a one-off cue.
    /// </summary>
    /// <param name="depth">The level to duck to, 0.0 (silent) to 1.0 (no ducking).</param>
    /// <param name="attack">How long to fade down over.</param>
    /// <param name="hold">How long to stay ducked once the attack completes.</param>
    /// <param name="release">How long to fade back up over.</param>
    public void Duck(float depth, TimeSpan attack, TimeSpan hold, TimeSpan release)
    {
        var handle = PushDuck(depth, attack, release);

        // The hold runs on the same ticker as everything else, so it freezes with the global pause
        // like the fades do - a duck cannot outlive its cue just because the game was paused.
        _ticker.Add(0f, 1f, attack + hold, _ => { }, handle.Dispose);
    }

    /// <summary>The music-bus attenuation ducking is currently applying, 1.0 when nothing is ducking.</summary>
    public float DuckMultiplier => AudioMixer.MusicDuckMultiplier;

    /// <summary>
    /// Releases every duck at once and restores the music bus.
    /// </summary>
    /// <param name="release">How long to fade back up over.</param>
    /// <remarks>
    /// The escape hatch for a duck whose handle was lost — a level torn down while dialogue was
    /// playing, an exception between <see cref="PushDuck"/> and its <c>Dispose</c>. Without it a
    /// leaked handle quietens the music for the rest of the process with nothing to point at.
    /// A scene change is a reasonable place to call it.
    /// </remarks>
    public void ClearDucks(TimeSpan release = default)
    {
        lock (_gate)
        {
            _ducks.Clear();
        }

        ApplyDuck(release);
    }

    internal void ReleaseDuck(DuckHandle handle)
    {
        TimeSpan release;

        lock (_gate)
        {
            if (!_ducks.Remove(handle))
            {
                return;
            }

            release = handle.Release;
        }

        ApplyDuck(release);
    }

    private void ApplyDuck(TimeSpan duration)
    {
        float target;

        lock (_gate)
        {
            // Deepest duck wins: with a big explosion and a dialogue line at once, the music should
            // be as quiet as the loudest reason demands, not as quiet as the most recent one.
            target = 1.0f;
            foreach (var duck in _ducks)
            {
                if (duck.Depth < target)
                {
                    target = duck.Depth;
                }
            }

            if (_duckFade is not null)
            {
                _ticker.Cancel(_duckFade);
                _duckFade = null;
            }
        }

        var from = AudioMixer.MusicDuckMultiplier;

        if (duration <= TimeSpan.Zero || Math.Abs(from - target) < 0.0005f)
        {
            AudioMixer.SetMusicDuckMultiplier(target);
            return;
        }

        var fade = _ticker.Add(from, target, duration, AudioMixer.SetMusicDuckMultiplier);
        lock (_gate)
        {
            _duckFade = fade;
        }
    }

    // ----- stingers -----

    /// <summary>
    /// Plays a one-shot musical hit — a level-complete fanfare, a discovery chime — over whatever
    /// music is playing, optionally ducking it.
    /// </summary>
    /// <param name="resourceKey">The key of a loaded <see cref="AudioResource"/> (see <see cref="AudioResourceManager"/>).</param>
    /// <param name="volume">The stinger's volume, 0.0 to 1.0.</param>
    /// <param name="duckMusic">Whether to duck the music underneath it for its duration.</param>
    /// <param name="duckDepth">The level to duck the music to while the stinger plays.</param>
    /// <returns><see langword="true"/> if the stinger started; <see langword="false"/> if the key was not loaded.</returns>
    /// <remarks>
    /// A stinger deliberately does NOT go through <see cref="SfxVoicePool"/>: the pool has a
    /// polyphony cap and will steal a voice when it is full, and a level-complete fanfare being
    /// culled by a busy combat scene is exactly the wrong outcome. It plays on its own voice, on the
    /// music bus, and is cleaned up when it finishes.
    /// </remarks>
    public bool PlayStinger(string resourceKey, float volume = 1.0f, bool duckMusic = false, float duckDepth = 0.3f)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return false;
        }

        var manager = AudioResourceManager.Instance;
        if (!manager.Contains(resourceKey))
        {
            Engine.Logger.LogWarning("Cannot play stinger '{Key}': no such audio resource is loaded.", resourceKey);
            return false;
        }

        var voice = manager.Clone(resourceKey, $"{resourceKey}__stinger_{Guid.NewGuid():N}");
        if (voice is null)
        {
            Engine.Logger.LogWarning("Cannot play stinger '{Key}': the resource could not be cloned.", resourceKey);
            return false;
        }

        voice.Bus = AudioBus.Music;
        voice.Volume = Math.Clamp(volume, 0f, 1f);
        voice.IsLooping = false;

        IDisposable? duck = duckMusic ? PushDuck(duckDepth, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400)) : null;

        void OnCompleted(object? sender, EventArgs e)
        {
            voice.PlaybackCompleted -= OnCompleted;
            duck?.Dispose();

            lock (_gate)
            {
                _stingers.Remove(voice);
            }

            SafeInvoke(() => manager.Unload(voice.Key), "unload a finished stinger");
        }

        voice.PlaybackCompleted += OnCompleted;

        lock (_gate)
        {
            _stingers.Add(voice);
        }

        voice.Play();
        return true;
    }

    // ----- playlists -----

    /// <summary>
    /// Plays a playlist, advancing to the next track as each one ends.
    /// </summary>
    /// <param name="playlist">The playlist to play.</param>
    /// <param name="crossfade">
    /// How long to crossfade between consecutive tracks. Zero starts each one cleanly after the last.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="playlist"/> is null.</exception>
    public void Play(MusicPlaylist playlist, TimeSpan crossfade = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        lock (_gate)
        {
            ThrowIfDisposed();
            _playlist = playlist;
        }

        playlist.Reset();
        AdvancePlaylist(playlist, crossfade, first: true);
    }

    /// <summary>Skips to the playlist's next track.</summary>
    /// <param name="crossfade">How long to crossfade over.</param>
    public void Next(TimeSpan crossfade = default)
    {
        MusicPlaylist? playlist;
        lock (_gate)
        {
            playlist = _playlist;
        }

        if (playlist is not null)
        {
            AdvancePlaylist(playlist, crossfade, first: false);
        }
    }

    private void AdvancePlaylist(MusicPlaylist playlist, TimeSpan crossfade, bool first)
    {
        var track = first ? playlist.Current ?? playlist.MoveNext() : playlist.MoveNext();

        if (track is null)
        {
            Stop();
            return;
        }

        void OnEnded(object? sender, EventArgs e)
        {
            track.Ended -= OnEnded;

            lock (_gate)
            {
                // A playlist swapped out (or a Stop) must not resurrect itself through a track that
                // was already on its way to ending.
                if (!ReferenceEquals(_playlist, playlist))
                {
                    return;
                }
            }

            AdvancePlaylist(playlist, crossfade, first: false);
        }

        track.Ended += OnEnded;

        if (crossfade > TimeSpan.Zero)
        {
            CrossfadeTo(track, crossfade);
        }
        else
        {
            Play(track);
        }
    }

    // ----- engine pause -----

    /// <summary>
    /// Freezes every fade for the global engine pause. Called by <see cref="AudioPauseRegistry"/>
    /// alongside the voice suspension, so the two cannot get out of step.
    /// </summary>
    internal static void FreezeFades()
    {
        if (_instance.IsValueCreated)
        {
            _instance.Value._ticker.Freeze();
        }
    }

    /// <summary>Resumes fades after the global engine pause.</summary>
    internal static void UnfreezeFades()
    {
        if (_instance.IsValueCreated)
        {
            _instance.Value._ticker.Unfreeze();
        }
    }

    /// <summary>The fade ticker, for tests that advance fades deterministically.</summary>
    internal MusicFadeTicker Ticker => _ticker;

    /// <summary>
    /// Stops the music, releases every duck and stinger, and shuts the fade ticker's thread down.
    /// </summary>
    public void Dispose()
    {
        MusicTrack? current;
        MusicTrack? outgoing;
        List<AudioResource> stingers;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            current = _current;
            outgoing = _outgoing;
            stingers = new List<AudioResource>(_stingers);

            _current = null;
            _outgoing = null;
            _pendingTransition = null;
            _playlist = null;
            _ducks.Clear();
            _stingers.Clear();
        }

        _ticker.CancelAll();

        if (current is not null) SafeInvoke(current.StopCore, "stop the music during disposal");
        if (outgoing is not null) SafeInvoke(outgoing.StopCore, "stop the outgoing music during disposal");

        foreach (var stinger in stingers)
        {
            SafeInvoke(stinger.Dispose, "dispose a stinger during disposal");
        }

        AudioMixer.SetMusicDuckMultiplier(1.0f);
        _ticker.Dispose();
    }

    // Callers hold _gate.
    private void CancelCurrentFadeLocked()
    {
        if (_currentFade is not null)
        {
            _ticker.Cancel(_currentFade);
            _currentFade = null;
        }
    }

    // Callers hold _gate.
    private void FinishOutgoingLocked()
    {
        if (_outgoing is not null)
        {
            var outgoing = _outgoing;
            _outgoing = null;
            SafeInvoke(outgoing.StopCore, "stop a music track left over from an interrupted crossfade");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MusicManager));
        }
    }

    // Transport calls reach a disposed voice or a torn-down device in normal shutdown races. None
    // of that should take a game down, and none of it should stop the rest of a transition.
    private static void SafeInvoke(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "Failed to {What}.", what);
        }
    }

    /// <summary>A live duck, released by disposing it.</summary>
    internal sealed class DuckHandle : IDisposable
    {
        private readonly MusicManager _owner;
        private bool _released;

        internal DuckHandle(MusicManager owner, float depth, TimeSpan release)
        {
            _owner = owner;
            Depth = depth;
            Release = release;
        }

        internal float Depth { get; }

        internal TimeSpan Release { get; }

        /// <summary>Releases this duck. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _owner.ReleaseDuck(this);
        }
    }
}
