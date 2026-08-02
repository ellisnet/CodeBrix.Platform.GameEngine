using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Music that is a decoded audio file — WAV, MP3, Ogg Vorbis, FLAC, or any other format registered
/// with the engine (Opus, once the application calls <c>CodeBrixAudioOpus.Register()</c>; see
/// <see cref="PlatformAudioFactory"/>).
/// </summary>
/// <remarks>
/// It plays through an <see cref="AudioResource"/>, so it streams from the loaded data rather than
/// holding a second decoded copy, participates in the global engine pause, and answers to the music
/// bus like everything else the <see cref="MusicManager"/> plays.
/// </remarks>
public sealed class FileMusicTrack : MusicTrack
{
    private readonly AudioResource _resource;
    private readonly bool _ownsResource;
    private bool _disposed;

    /// <summary>
    /// Wraps an already-loaded <see cref="AudioResource"/> as a music track and moves it to the
    /// music bus.
    /// </summary>
    /// <param name="key">A name for the track.</param>
    /// <param name="resource">The loaded resource to play.</param>
    /// <param name="ownsResource">
    /// Whether disposing this track should dispose the resource. False when the resource is shared
    /// or owned by <see cref="AudioResourceManager"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
    public FileMusicTrack(string key, AudioResource resource, bool ownsResource = false)
        : base(key)
    {
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _ownsResource = ownsResource;

        _resource.Bus = AudioBus.Music;

        // Music is long and usually looping, so it always suspends with the global pause - the
        // short-effect exemption is for fire-and-forget sounds, not for a soundtrack.
        _resource.SuspendOnEnginePause = true;
        _resource.PlaybackCompleted += OnPlaybackCompleted;
    }

    /// <inheritdoc/>
    public override TimeSpan Position => _disposed ? TimeSpan.Zero : _resource.CurrentTime;

    /// <inheritdoc/>
    public override TimeSpan Duration => _disposed ? TimeSpan.Zero : _resource.Duration;

    /// <inheritdoc/>
    public override bool IsLooping
    {
        get => !_disposed && _resource.IsLooping;
        set
        {
            if (!_disposed)
            {
                _resource.IsLooping = value;
            }
        }
    }

    /// <inheritdoc/>
    public override bool IsPlaying => !_disposed && _resource.IsPlaying;

    /// <summary>The underlying resource, for a game that needs its pan or its raw transport.</summary>
    public AudioResource Resource => _resource;

    /// <inheritdoc/>
    public override void Seek(TimeSpan position)
    {
        if (!_disposed)
        {
            _resource.Seek(position);
        }
    }

    /// <inheritdoc/>
    protected override void ApplyVolume(float volume)
    {
        if (!_disposed)
        {
            // AudioResource applies the music bus and master volume itself (it is an IMixerVoice),
            // so this sets only the track's own level.
            _resource.Volume = volume;
        }
    }

    /// <inheritdoc/>
    internal override void StartCore(bool fromStart) => _resource.Play(fromStart);

    /// <inheritdoc/>
    internal override void PauseCore() => _resource.Pause();

    /// <inheritdoc/>
    internal override void ResumeCore() => _resource.Resume();

    /// <inheritdoc/>
    internal override void StopCore() => _resource.Stop();

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resource.PlaybackCompleted -= OnPlaybackCompleted;

        if (_ownsResource)
        {
            _resource.Dispose();
        }
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e) => RaiseEnded();
}
