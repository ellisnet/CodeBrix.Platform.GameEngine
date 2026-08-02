namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// When a queued music transition is allowed to start — the difference between music that changes
/// when the game says so and music that changes when the music says so.
/// </summary>
/// <remarks>
/// Anything other than <see cref="Immediate"/> needs a <see cref="MusicTimeline"/> on the track that
/// is currently playing, because the engine has to know where the beats are. For MIDI music that
/// timeline is derived from the file; for a decoded audio file the game supplies it. Without one the
/// transition happens immediately and says so in the log — see <see cref="MusicTimeline"/> for why
/// guessing is not on offer.
/// </remarks>
public enum MusicTransitionQuantize
{
    /// <summary>Start now. The default, and correct whenever the change should feel like a reaction.</summary>
    Immediate = 0,

    /// <summary>Wait for the next beat. A short wait, and enough to stop a transition landing off the pulse.</summary>
    Beat = 1,

    /// <summary>
    /// Wait for the next bar. What a musical transition normally wants: the new material enters on a
    /// downbeat, so the change reads as an arrangement rather than an interruption.
    /// </summary>
    Bar = 2,
}
