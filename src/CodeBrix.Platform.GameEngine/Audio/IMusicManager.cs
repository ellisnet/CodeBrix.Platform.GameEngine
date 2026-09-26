using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// The part of <see cref="MusicManager"/> a game's music policy drives: the music slider, ducking
/// and stingers. <see cref="MusicManager"/> implements it; a game hands
/// <see cref="MusicManager.Instance"/> to its music code and a recording fake to that code's tests,
/// so the policy ("duck under the pause menu, sting on game over") is testable with no audio device
/// and no adapter class of the game's own.
/// </summary>
/// <remarks>
/// Every member behaves exactly as the <see cref="MusicManager"/> member of the same name - see
/// there for the details. Transport (play, crossfade, playlists) is deliberately not part of this
/// surface: a game that plays tracks drives <see cref="MusicManager"/> directly, and a game that plays
/// generated music starts it through the GeneratedMusic add-in's session seam.
/// </remarks>
public interface IMusicManager
{
    /// <summary>The player's music slider (<see cref="AudioMixer.MusicVolume"/>), 0.0 to 1.0.</summary>
    float MusicVolume { get; set; }

    /// <summary>The music-bus attenuation ducking is currently applying, 1.0 when nothing is ducking.</summary>
    float DuckMultiplier { get; }

    /// <summary>Ducks the music until the returned handle is disposed.</summary>
    /// <param name="depth">The level to duck to, 0.0 (silent) to 1.0 (no ducking).</param>
    /// <param name="attack">How long to fade down over.</param>
    /// <param name="release">How long to fade back up over once every duck is released.</param>
    /// <returns>A handle; dispose it to release this duck.</returns>
    IDisposable PushDuck(float depth, TimeSpan attack = default, TimeSpan release = default);

    /// <summary>Ducks the music for a fixed time, then restores it.</summary>
    /// <param name="depth">The level to duck to, 0.0 (silent) to 1.0 (no ducking).</param>
    /// <param name="attack">How long to fade down over.</param>
    /// <param name="hold">How long to stay ducked once the attack completes.</param>
    /// <param name="release">How long to fade back up over.</param>
    void Duck(float depth, TimeSpan attack, TimeSpan hold, TimeSpan release);

    /// <summary>Releases every duck at once and restores the music bus.</summary>
    /// <param name="release">How long to fade back up over.</param>
    void ClearDucks(TimeSpan release = default);

    /// <summary>Plays a one-shot stinger on the music bus, optionally ducking the music under it.</summary>
    /// <param name="resourceKey">The key of a loaded <see cref="AudioResource"/>.</param>
    /// <param name="volume">The stinger's volume, 0.0 to 1.0.</param>
    /// <param name="duckMusic">Whether to duck the music underneath it for its duration.</param>
    /// <param name="duckDepth">The level to duck the music to while the stinger plays.</param>
    /// <returns><see langword="true"/> if the stinger started; <see langword="false"/> if the key was not loaded.</returns>
    bool PlayStinger(string resourceKey, float volume = 1.0f, bool duckMusic = false, float duckDepth = 0.3f);

    /// <summary>Plays a one-shot stinger on a chosen bus, optionally ducking the music for its length.</summary>
    /// <param name="resourceKey">The key of a loaded <see cref="AudioResource"/>.</param>
    /// <param name="bus">The bus the stinger plays on (<see cref="AudioBus.Sfx"/> to keep it out of the music slider's reach).</param>
    /// <param name="volume">The stinger's volume, 0.0 to 1.0.</param>
    /// <param name="duckMusic">Whether to duck the music underneath it until it finishes.</param>
    /// <param name="duckDepth">The level to duck the music to while the stinger plays.</param>
    /// <returns><see langword="true"/> if the stinger started; <see langword="false"/> if the key was not loaded.</returns>
    bool PlayStingerOnBus(string resourceKey, AudioBus bus, float volume = 1.0f, bool duckMusic = false, float duckDepth = 0.3f);

    /// <summary>Plays a one-shot stinger and ducks the music until the returned handle is disposed.</summary>
    /// <param name="resourceKey">The key of a loaded <see cref="AudioResource"/>.</param>
    /// <param name="duckDepth">The level to duck the music to, 0.0 (silent) to 1.0 (no ducking).</param>
    /// <param name="attack">How long to fade the music down over.</param>
    /// <param name="release">How long to fade the music back up over once the handle is disposed.</param>
    /// <param name="bus">The bus the stinger plays on; the effects bus by default.</param>
    /// <param name="volume">The stinger's volume, 0.0 to 1.0.</param>
    /// <returns>The duck; dispose it to release the music.</returns>
    IDisposable PlayStingerWithHeldDuck(string resourceKey, float duckDepth, TimeSpan attack = default, TimeSpan release = default,
                                        AudioBus bus = AudioBus.Sfx, float volume = 1.0f);
}
