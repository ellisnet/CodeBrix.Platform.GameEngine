namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Where an <see cref="IStreamingMusicProvider"/> is in its life, as reported through
/// <see cref="IStreamingMusicProvider.State"/> and <see cref="StreamingMusicTrack.State"/>.
/// </summary>
/// <remarks>
/// <see cref="Starting"/> and <see cref="Starved"/> are ORDINARY states, not errors: a provider may
/// take seconds to produce its first audio, and may fall behind for a moment later on. The engine
/// plays silence through both and keeps pulling. Only <see cref="Stopped"/> and
/// <see cref="Faulted"/> end a stream.
/// </remarks>
public enum StreamingMusicState
{
    /// <summary>Not running. Rendering yields nothing until the provider is started.</summary>
    Stopped,

    /// <summary>
    /// Started, but not producing audio yet (a model loading, a first piece being prepared). The
    /// engine plays silence.
    /// </summary>
    Starting,

    /// <summary>Producing audio.</summary>
    Playing,

    /// <summary>
    /// Started and meant to be producing audio, but it has fallen behind and has nothing ready. The
    /// engine plays silence and keeps pulling; the provider returns to <see cref="Playing"/> by itself.
    /// </summary>
    Starved,

    /// <summary>
    /// Stopped by a failure. <see cref="IStreamingMusicProvider.Fault"/> says what went wrong; the
    /// failure is never thrown into the engine.
    /// </summary>
    Faulted,
}
