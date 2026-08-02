namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// The mixer bus a voice's volume is scaled by, so a game can offer the two volume sliders players
/// expect without tracking every playing voice itself. See <see cref="AudioMixer"/>.
/// </summary>
public enum AudioBus
{
    /// <summary>
    /// No bus: the voice is scaled by <see cref="AudioMixer.MasterVolume"/> alone. For audio that
    /// belongs to neither slider — a voice-over track a game gives its own control, say.
    /// </summary>
    None = 0,

    /// <summary>
    /// The music bus (<see cref="AudioMixer.MusicVolume"/>). Everything the
    /// <see cref="MusicManager"/> plays is on this bus, and it is what ducking attenuates.
    /// </summary>
    Music = 1,

    /// <summary>
    /// The sound-effects bus (<see cref="AudioMixer.SfxVolume"/>). The default for
    /// <see cref="AudioResource"/>, <see cref="SoundChannel"/> and <see cref="SfxVoicePool"/> voices.
    /// </summary>
    Sfx = 2,
}
