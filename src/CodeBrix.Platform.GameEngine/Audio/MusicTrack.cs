using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// One piece of music the <see cref="MusicManager"/> can play — either a decoded audio file
/// (<see cref="FileMusicTrack"/>) or a MIDI sequence rendered through a SoundFont or SFZ instrument
/// (<see cref="MidiMusicTrack"/>).
/// </summary>
/// <remarks>
/// <para>
/// A track is a HANDLE, not a transport: fetch state from it and set its <see cref="Volume"/>, but
/// play, stop, crossfade and seek through <see cref="MusicManager"/>, which owns the fades, the
/// ducking and the "what is playing now" bookkeeping. Driving a track behind the manager's back
/// leaves it describing something that is not happening.
/// </para>
/// <para>
/// Every track plays on <see cref="AudioBus.Music"/>, so the player's music slider and any active
/// duck apply automatically (see <see cref="AudioMixer"/>).
/// </para>
/// <para>Dispose a track when the game is finished with it; the manager disposes the ones it owns.</para>
/// </remarks>
public abstract class MusicTrack : IDisposable
{
    private float _volume = 1.0f;

    /// <summary>Creates a track with the given key.</summary>
    /// <param name="key">A name for the track, used in logs and by <see cref="MusicPlaylist"/>.</param>
    protected MusicTrack(string key) => Key = key ?? string.Empty;

    /// <summary>Raised when the track reaches its natural end. Not raised for a stop, and not raised while looping.</summary>
    /// <remarks>
    /// May arrive on a background or audio thread. Marshal to the engine thread with
    /// <c>Engine.Instance.EngineDispatcher.Post</c> before touching game state.
    /// </remarks>
    public event EventHandler Ended;

    /// <summary>The track's name, as given at construction.</summary>
    public string Key { get; }

    /// <summary>
    /// Where this track's beats and bars are, enabling quantised transitions away from it and
    /// <see cref="MusicManager.JumpToMarker"/>. <see langword="null"/> when unknown, which is the
    /// default for everything but a <see cref="MidiMusicTrack"/> loaded from a file.
    /// </summary>
    /// <remarks>
    /// A MIDI track fills this in from its file. For decoded audio the game sets it — see
    /// <see cref="MusicTimeline"/> for why the engine will not guess.
    /// </remarks>
    public MusicTimeline? Timeline { get; set; }

    /// <summary>The current playback position.</summary>
    public abstract TimeSpan Position { get; }

    /// <summary>The track's total length, or <see cref="TimeSpan.Zero"/> when it is not known.</summary>
    public abstract TimeSpan Duration { get; }

    /// <summary>Whether the track repeats when it reaches the end.</summary>
    public abstract bool IsLooping { get; set; }

    /// <summary>Whether the track is currently sounding.</summary>
    public abstract bool IsPlaying { get; }

    /// <summary>
    /// The track's own level, 0.0 to 1.0, before the music bus and master volume are applied. This
    /// is what a fade or crossfade moves — a game setting it directly during a fade will have its
    /// value overwritten by the next tick.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            ApplyVolume(_volume);
        }
    }

    /// <summary>Moves playback to a position.</summary>
    /// <param name="position">The position to seek to, from the start of the track.</param>
    public abstract void Seek(TimeSpan position);

    /// <summary>Applies the track's own volume to whatever is actually producing sound.</summary>
    /// <param name="volume">The track's volume, 0.0 to 1.0, before bus and master scaling.</param>
    protected abstract void ApplyVolume(float volume);

    /// <summary>Starts playback from the current position.</summary>
    internal abstract void StartCore(bool fromStart);

    /// <summary>Pauses playback, keeping the position.</summary>
    internal abstract void PauseCore();

    /// <summary>Resumes playback from where it was paused.</summary>
    internal abstract void ResumeCore();

    /// <summary>Stops playback and rewinds.</summary>
    internal abstract void StopCore();

    /// <summary>Raises <see cref="Ended"/>.</summary>
    protected void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);

    /// <summary>Releases the track's audio resources.</summary>
    public abstract void Dispose();
}
