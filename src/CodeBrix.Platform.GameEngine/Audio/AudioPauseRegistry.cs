using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using CodeBrix.Platform.GameEngine.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A live audio voice that can be suspended by the global engine pause
/// (<see cref="Engine.Pause"/>). Implemented by <see cref="SoundChannel"/>,
/// <see cref="StreamingAudioSource"/>, and <see cref="AudioResource"/>.
/// </summary>
internal interface IEnginePausableAudio
{
    /// <summary>True while this voice is audibly playing.</summary>
    bool IsPlayingForEnginePause { get; }

    /// <summary>
    /// The total duration of the currently playing material, or <c>null</c> when it is
    /// endless or unknown (streams, looping resources) — <c>null</c> always suspends.
    /// </summary>
    TimeSpan? KnownDurationForEnginePause { get; }

    /// <summary>
    /// Per-voice override of the engine-pause suspend decision: <c>true</c> always suspends,
    /// <c>false</c> never suspends, <c>null</c> (the default) applies the automatic
    /// short-sound-effect exemption.
    /// </summary>
    bool? SuspendOnEnginePause { get; }

    /// <summary>Pauses this voice on behalf of the global engine pause.</summary>
    void EnginePause();

    /// <summary>Resumes this voice when the global engine pause is lifted.</summary>
    void EngineResume();
}

/// <summary>
/// Tracks every live audio voice so the global engine pause can suspend game audio and the
/// matching resume can restore exactly the voices the pause suspended. Voices the game paused
/// itself stay paused; short fire-and-forget sound effects are left to ring out.
/// </summary>
internal static class AudioPauseRegistry
{
    private static readonly object _gate = new();
    private static readonly List<WeakReference<IEnginePausableAudio>> _voices = new();
    private static readonly List<IEnginePausableAudio> _suspended = new();

    /// <summary>Registers a newly constructed voice. Voices are held weakly.</summary>
    internal static void Register(IEnginePausableAudio voice)
    {
        lock (_gate)
        {
            // Prune dead entries opportunistically so the list does not grow unbounded in
            // games that create many transient voices.
            _voices.RemoveAll(reference => !reference.TryGetTarget(out _));
            _voices.Add(new WeakReference<IEnginePausableAudio>(voice));
        }
    }

    /// <summary>
    /// Suspends every playing voice except short fire-and-forget sound effects: voices with a
    /// known finite duration of <paramref name="shortSoundEffectSeconds"/> or less are left to
    /// ring out (and are not resumed later). Safe to call repeatedly; already-suspended voices
    /// are not re-captured.
    /// </summary>
    /// <param name="shortSoundEffectSeconds">The short-sound-effect exemption threshold, in seconds.</param>
    internal static void SuspendAll(double shortSoundEffectSeconds)
    {
        lock (_gate)
        {
            foreach (var reference in _voices.ToArray())
            {
                if (!reference.TryGetTarget(out var voice))
                    continue;

                if (_suspended.Contains(voice))
                    continue;

                try
                {
                    if (!voice.IsPlayingForEnginePause)
                        continue;

                    bool suspend = voice.SuspendOnEnginePause ?? voice.KnownDurationForEnginePause switch
                    {
                        // Endless or unknown material (streams, looping clips) always suspends.
                        null => true,
                        // A short clip is a fire-and-forget effect: let it ring out.
                        { } duration => duration.TotalSeconds > shortSoundEffectSeconds,
                    };

                    if (!suspend)
                        continue;

                    voice.EnginePause();
                    _suspended.Add(voice);
                }
                catch (Exception ex)
                {
                    // A voice racing disposal must not break the global pause.
                    EngineLogger.GetLogger<AudioResource>().LogError(ex, "Failed to suspend an audio voice for the engine pause.");
                }
            }
        }
    }

    /// <summary>Resumes exactly the voices <see cref="SuspendAll"/> suspended.</summary>
    internal static void ResumeAll()
    {
        lock (_gate)
        {
            foreach (var voice in _suspended)
            {
                try
                {
                    voice.EngineResume();
                }
                catch (Exception ex)
                {
                    // A voice disposed while suspended must not break the global resume.
                    EngineLogger.GetLogger<AudioResource>().LogError(ex, "Failed to resume an audio voice after the engine pause.");
                }
            }

            _suspended.Clear();
        }
    }
}
