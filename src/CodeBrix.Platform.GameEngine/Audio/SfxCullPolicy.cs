namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// What an <see cref="SfxVoicePool"/> does with a new sound-effect trigger when every voice
/// in the pool is already playing (the polyphony cap has been reached).
/// </summary>
public enum SfxCullPolicy
{
    /// <summary>The new trigger is dropped (and logged at Debug level); playing voices are untouched.</summary>
    RejectNew,

    /// <summary>The longest-playing voice is stopped and its slot is reused for the new trigger.</summary>
    CullOldest,

    /// <summary>
    /// The lowest-priority playing voice (oldest on a tie) is stopped and reused — unless
    /// every playing voice has a HIGHER priority than the new trigger, in which case the new
    /// trigger is dropped. Games express "distance from the camera" or "critical player cue"
    /// by mapping it onto the priority they pass to <see cref="SfxVoicePool.TryPlay(CachedSound, float, float, int)"/>
    /// (e.g. nearer/critical = higher).
    /// </summary>
    CullLowestPriority,
}
