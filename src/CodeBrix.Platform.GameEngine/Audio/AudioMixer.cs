using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// A voice whose gain depends on <see cref="AudioMixer"/>, and which must recompute it when a bus
/// volume changes. Implemented by every engine audio voice.
/// </summary>
internal interface IMixerVoice
{
    /// <summary>Recomputes and applies this voice's gain from its own volume and its bus.</summary>
    void ApplyMixerVolume();
}

/// <summary>
/// The engine's volume buses: a master control plus the separate music and sound-effects sliders
/// players expect, applied to every engine voice without the game tracking any of them.
/// </summary>
/// <remarks>
/// <para>
/// A voice's audible gain is its own <c>Volume</c> multiplied by its bus's volume multiplied by
/// <see cref="MasterVolume"/>. Changing a bus volume takes effect immediately on everything already
/// playing on it — there is no need to walk live voices, and no need to re-apply anything after a
/// settings screen changes a slider.
/// </para>
/// <para>
/// All three default to 1.0, so a game that never touches this type sounds exactly as it did before
/// the buses existed.
/// </para>
/// <para>
/// Which bus a voice is on: <see cref="AudioResource"/>, <see cref="SoundChannel"/> and
/// <see cref="SfxVoicePool"/> voices default to <see cref="AudioBus.Sfx"/>;
/// <see cref="StreamingAudioSource"/> defaults to <see cref="AudioBus.Music"/> (it is the endless
/// material path); everything <see cref="MusicManager"/> plays is on <see cref="AudioBus.Music"/>.
/// Each of those exposes a <c>Bus</c> property to override.
/// </para>
/// <para>Thread-safe: set a volume from any thread.</para>
/// </remarks>
public static class AudioMixer
{
    private static readonly object _gate = new();
    private static readonly List<WeakReference<IMixerVoice>> _voices = new();

    private static float _masterVolume = 1.0f;
    private static float _musicVolume = 1.0f;
    private static float _sfxVolume = 1.0f;
    private static float _musicDuckMultiplier = 1.0f;

    /// <summary>
    /// The overall output level, 0.0 to 1.0. Scales every engine voice on every bus, including
    /// <see cref="AudioBus.None"/>. Defaults to 1.0.
    /// </summary>
    public static float MasterVolume
    {
        get { lock (_gate) { return _masterVolume; } }
        set => SetVolume(ref _masterVolume, value);
    }

    /// <summary>
    /// The music slider, 0.0 to 1.0. Scales every voice on <see cref="AudioBus.Music"/>.
    /// Defaults to 1.0.
    /// </summary>
    public static float MusicVolume
    {
        get { lock (_gate) { return _musicVolume; } }
        set => SetVolume(ref _musicVolume, value);
    }

    /// <summary>
    /// The sound-effects slider, 0.0 to 1.0. Scales every voice on <see cref="AudioBus.Sfx"/>.
    /// Defaults to 1.0.
    /// </summary>
    public static float SfxVolume
    {
        get { lock (_gate) { return _sfxVolume; } }
        set => SetVolume(ref _sfxVolume, value);
    }

    /// <summary>
    /// The ducking attenuation currently applied to the music bus, 0.0 to 1.0 — 1.0 when nothing is
    /// ducking. Owned by <see cref="MusicManager"/>'s ducking controls; read it for a mixer display,
    /// but duck through <see cref="MusicManager.PushDuck"/> rather than setting
    /// <see cref="MusicVolume"/>, so the player's own slider setting is not overwritten.
    /// </summary>
    public static float MusicDuckMultiplier
    {
        get { lock (_gate) { return _musicDuckMultiplier; } }
    }

    /// <summary>
    /// The volume of one bus, before <see cref="MasterVolume"/> — including any ducking on the
    /// music bus.
    /// </summary>
    /// <param name="bus">The bus to read.</param>
    /// <returns>The bus's current multiplier, 0.0 to 1.0. <see cref="AudioBus.None"/> is always 1.0.</returns>
    public static float GetBusVolume(AudioBus bus)
    {
        lock (_gate)
        {
            return bus switch
            {
                AudioBus.Music => _musicVolume * _musicDuckMultiplier,
                AudioBus.Sfx => _sfxVolume,
                _ => 1.0f,
            };
        }
    }

    /// <summary>
    /// The gain a voice should actually apply: its own volume, scaled by its bus and the master.
    /// </summary>
    /// <param name="voiceVolume">The voice's own volume, 0.0 to 1.0.</param>
    /// <param name="bus">The bus the voice plays on.</param>
    /// <returns>The effective gain, clamped to 0.0-1.0.</returns>
    public static float EffectiveVolume(float voiceVolume, AudioBus bus)
    {
        lock (_gate)
        {
            var busVolume = bus switch
            {
                AudioBus.Music => _musicVolume * _musicDuckMultiplier,
                AudioBus.Sfx => _sfxVolume,
                _ => 1.0f,
            };

            return Math.Clamp(voiceVolume, 0f, 1f) * busVolume * _masterVolume;
        }
    }

    /// <summary>
    /// Sets the music bus's ducking multiplier. Internal because ducking is a policy
    /// <see cref="MusicManager"/> owns — it reference-counts overlapping ducks and fades between
    /// levels, neither of which a raw setter could do correctly.
    /// </summary>
    /// <param name="multiplier">The attenuation, 0.0 (silent) to 1.0 (no ducking).</param>
    internal static void SetMusicDuckMultiplier(float multiplier)
    {
        var clamped = Math.Clamp(multiplier, 0f, 1f);

        lock (_gate)
        {
            // A fade calls this every tick, so skipping imperceptible changes keeps the notification
            // (and every voice's gain recompute) off the hot path.
            if (Math.Abs(_musicDuckMultiplier - clamped) < 0.0005f)
            {
                return;
            }

            _musicDuckMultiplier = clamped;
        }

        NotifyVoices();
    }

    /// <summary>
    /// Registers a voice so bus-volume changes reach it. Voices are held weakly, so registering
    /// does not keep one alive and there is nothing to unregister on disposal.
    /// </summary>
    /// <param name="voice">The voice to register.</param>
    internal static void Register(IMixerVoice voice)
    {
        if (voice is null)
        {
            return;
        }

        lock (_gate)
        {
            // Prune opportunistically so a game creating many transient voices does not grow this
            // list without bound - the same approach AudioPauseRegistry takes.
            _voices.RemoveAll(reference => !reference.TryGetTarget(out _));
            _voices.Add(new WeakReference<IMixerVoice>(voice));
        }
    }

    /// <summary>
    /// Restores all three volumes to 1.0 and clears any ducking. Intended for tests and for a game
    /// resetting its audio settings; it does not stop or change any voice's own volume.
    /// </summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _masterVolume = 1.0f;
            _musicVolume = 1.0f;
            _sfxVolume = 1.0f;
            _musicDuckMultiplier = 1.0f;
        }

        NotifyVoices();
    }

    private static void SetVolume(ref float field, float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);

        lock (_gate)
        {
            if (Math.Abs(field - clamped) < float.Epsilon)
            {
                return;
            }

            field = clamped;
        }

        NotifyVoices();
    }

    // Snapshots the live voices under the lock, then applies OUTSIDE it: a voice's gain setter
    // reaches into the audio graph, and holding the mixer lock across that would put this lock
    // underneath the audio path's own locks in some orders and not others.
    private static void NotifyVoices()
    {
        List<IMixerVoice> live = new();

        lock (_gate)
        {
            for (var i = _voices.Count - 1; i >= 0; i--)
            {
                if (_voices[i].TryGetTarget(out var voice))
                {
                    live.Add(voice);
                }
                else
                {
                    _voices.RemoveAt(i);
                }
            }
        }

        foreach (var voice in live)
        {
            try
            {
                voice.ApplyMixerVolume();
            }
            catch (Exception ex)
            {
                // A voice racing disposal must not break a volume change for every other voice.
                Engine.Logger.LogError(ex, "Failed to apply a mixer volume change to an audio voice.");
            }
        }
    }
}
