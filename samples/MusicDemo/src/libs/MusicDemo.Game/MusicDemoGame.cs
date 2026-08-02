using System;
using System.Drawing;
using System.Text;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Rendering;
using SkiaSharp;
using static CodeBrix.Platform.GameEngine.Drawing.Direct.TextBlock;

namespace MusicDemo.Game;

/// <summary>
/// Drives the MusicDemo sample: every part of the engine's music system, over assets the sample
/// generates for itself (see <see cref="MusicAssetFactory"/>).
/// </summary>
/// <remarks>
/// <para>
/// The engine is genuinely running here, drawing a readout of what the music system is doing, so the
/// pause control demonstrates the real contract: pausing suspends the music AND freezes any fade in
/// flight, and a transition queued for the next bar cannot fire while paused.
/// </para>
/// <para>
/// The device format is PINNED at start-up (<see cref="AudioSystem.Initialize"/>). That is what lets
/// a stem set of assorted source rates line up, because stems then rate-convert to the pinned rate as
/// they decode rather than being rejected for disagreeing.
/// </para>
/// </remarks>
public sealed class MusicDemoGame
{
    private readonly GameSurfaceCanvas _canvas;

    private MusicPlaylist _playlist;
    private MusicStemSet _stems;
    private FileMusicTrack _trackA;
    private FileMusicTrack _trackB;
    private MidiMusicTrack _midiTrack;
    private TextBlock _readout;
    private IDisposable _heldDuck;

    /// <summary>Creates the demo over a render surface.</summary>
    /// <param name="canvas">The surface to draw the readout into.</param>
    public MusicDemoGame(GameSurfaceCanvas canvas)
        => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>The stem set, for the per-layer controls.</summary>
    public MusicStemSet Stems => _stems;

    /// <summary>The MIDI track, for the per-channel layering and speed controls.</summary>
    public MidiMusicTrack MidiTrack => _midiTrack;

    /// <summary>Whether the engine is currently paused.</summary>
    public bool IsPaused => Engine.Instance.IsPaused;

    /// <summary>
    /// Generates the assets, builds the tracks and starts the engine. Call on the UI thread once the
    /// surface has a non-zero size.
    /// </summary>
    public void Start()
    {
        MusicAssetFactory.EnsureAssets();

        // Pin the device before anything plays, so every voice converts to one known format.
        AudioSystem.Initialize(MusicAssetFactory.SampleRate, 2);

        var renderSurface = _canvas.Host;
        var adapter = renderSurface.RenderSurfaceAdapter;
        renderSurface.ViewManager.ConfigureSingleFullView();

        Engine.Instance.CPSCalculated += _ => _readout?.SetText(BuildReadout());
        Engine.Instance.Start(SynchronizationContext.Current);
        Engine.Instance.Configuration.TargetFPS = 30; // a text readout does not need more

        BuildTracks();
        BuildReadoutDisplay(renderSurface, adapter.Width, adapter.Height);
    }

    private void BuildTracks()
    {
        var resources = AudioResourceManager.Instance;

        var timeline = new MusicTimeline(MusicAssetFactory.BeatsPerMinute, MusicAssetFactory.BeatsPerBar);

        // Decoded audio cannot be asked what tempo it is, so the game says. The composer knows it;
        // the engine deliberately refuses to guess.
        _trackA = new FileMusicTrack("Track A", resources.LoadFromFile("track-a", MusicAssetFactory.TrackAPath))
        {
            IsLooping = true,
            Timeline = timeline,
        };

        _trackB = new FileMusicTrack("Track B", resources.LoadFromFile("track-b", MusicAssetFactory.TrackBPath))
        {
            IsLooping = true,
            Timeline = timeline,
        };

        // A MIDI track loaded FROM A PATH derives its own timeline - tempo, time signature and the
        // markers that become jump points - with nothing set here.
        _midiTrack = new MidiMusicTrack("MIDI Theme", MusicAssetFactory.InstrumentPath, MusicAssetFactory.MidiPath)
        {
            IsLooping = true,
        };

        _stems = new MusicStemSet("Adaptive Stems", MusicAssetFactory.StemNames, DecodeStems())
        {
            IsLooping = true,
            Timeline = timeline,
        };

        _playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.All };
        _playlist.Add(_trackA);
        _playlist.Add(_trackB);

        resources.LoadFromFile("stinger", MusicAssetFactory.StingerPath);
        resources.LoadFromFile("voice", MusicAssetFactory.VoicePath);
    }

    private static CachedSound[] DecodeStems()
    {
        var stems = new CachedSound[MusicAssetFactory.StemPaths.Length];
        for (var i = 0; i < stems.Length; i++)
        {
            stems[i] = CachedSound.FromFile(MusicAssetFactory.StemPaths[i]);
        }

        return stems;
    }

    private void BuildReadoutDisplay(RenderSurfaceHostBase renderSurface, int width, int height)
    {
        var panel = new DirectRectangle(Color.FromArgb(20, 24, 40), renderSurface, renderSurface.ViewManager.Views[0],
                new Rectangle(16, 16, Math.Max(320, width - 32), Math.Max(180, height - 32)), null)
            .SetAlpha(220)
            .SetCornerRadius(8f)
            .SetBorderColor(Color.FromArgb(70, 90, 140))
            .SetFilled(true)
            .SetStrokeWidth(2f);

        panel.ZOrder = 0;

        _readout = new TextBlock(renderSurface, renderSurface.ViewManager.Views[0],
                new Rectangle(32, 32, Math.Max(300, width - 64), Math.Max(160, height - 64)), null)
            .SetFont(SKTypeface.Default, 16f, minSize: 12f)
            .SetColors(Color.Gainsboro, Color.Transparent)
            .SetAlignment(SKTextAlign.Left, VerticalAlign.Top)
            .EnableWrapping()
            .SetMaxLines(20);

        _readout.ZOrder = 1;
        _readout.SetText(BuildReadout());
    }

    private string BuildReadout(bool pausing = false)
    {
        var manager = MusicManager.Instance;
        var now = manager.NowPlaying;

        var text = new StringBuilder()
            .AppendLine("CodeBrix.Platform.GameEngine - MUSIC SYSTEM DEMO")
            .AppendLine()
            .AppendLine($"Now playing : {now?.Key ?? "(nothing)"}")
            .AppendLine($"Position    : {now?.Position.TotalSeconds ?? 0:0.00}s of {now?.Duration.TotalSeconds ?? 0:0.00}s")
            .AppendLine($"Fades       : {manager.ActiveFadeCount} in flight"
                        + (manager.HasPendingTransition ? "   [transition queued for the next bar]" : string.Empty))
            .AppendLine()
            .AppendLine($"Master {AudioMixer.MasterVolume:0.00}   Music {AudioMixer.MusicVolume:0.00}   "
                        + $"Sfx {AudioMixer.SfxVolume:0.00}   Duck {AudioMixer.MusicDuckMultiplier:0.00}")
            .AppendLine();

        if (_stems is not null)
        {
            text.Append("Stems       : ");
            foreach (var stem in _stems.Stems)
            {
                text.Append($"{stem.Name} {Meter(stem.Gain)}  ");
            }

            text.AppendLine();
        }

        if (_midiTrack?.Timeline is not null)
        {
            var markers = string.Join(", ", _midiTrack.Timeline.Markers);
            text.AppendLine($"MIDI grid   : {_midiTrack.Timeline.BeatsPerMinute:0} BPM, "
                            + $"{_midiTrack.Timeline.BeatsPerBar:0} beats/bar, markers: {markers}");
        }

        if (pausing || Engine.Instance.IsPaused)
        {
            text.AppendLine().AppendLine("*** PAUSED - music suspended, fades frozen ***");
        }

        return text.ToString();
    }

    private static string Meter(float gain)
    {
        var filled = (int)Math.Round(gain * 8);
        return "[" + new string('#', filled) + new string('.', 8 - filled) + "]";
    }

    // ----- the controls the view model drives -----

    /// <summary>Plays the first linear track, fading in.</summary>
    public void PlayTrackA() => MusicManager.Instance.Play(_trackA, TimeSpan.FromSeconds(1));

    /// <summary>Crossfades to the second linear track, optionally waiting for the next bar.</summary>
    /// <param name="quantize">The boundary to wait for.</param>
    public void CrossfadeToTrackB(MusicTransitionQuantize quantize)
        => MusicManager.Instance.CrossfadeTo(_trackB, TimeSpan.FromSeconds(2), quantize);

    /// <summary>Crossfades back to the first linear track, optionally waiting for the next bar.</summary>
    /// <param name="quantize">The boundary to wait for.</param>
    public void CrossfadeToTrackA(MusicTransitionQuantize quantize)
        => MusicManager.Instance.CrossfadeTo(_trackA, TimeSpan.FromSeconds(2), quantize);

    /// <summary>Plays the layered stem set. Only the pad layer starts audible.</summary>
    public void PlayStems() => MusicManager.Instance.Play(_stems, TimeSpan.FromSeconds(1));

    /// <summary>Plays the MIDI theme, rendered live through the generated SFZ instrument.</summary>
    public void PlayMidi() => MusicManager.Instance.Play(_midiTrack, TimeSpan.FromSeconds(1));

    /// <summary>Plays the two linear tracks as a repeating playlist, crossfading between them.</summary>
    public void PlayPlaylist() => MusicManager.Instance.Play(_playlist, TimeSpan.FromSeconds(2));

    /// <summary>Skips to the playlist's next track.</summary>
    public void NextInPlaylist() => MusicManager.Instance.Next(TimeSpan.FromSeconds(2));

    /// <summary>Stops the music, fading out.</summary>
    public void StopMusic() => MusicManager.Instance.Stop(TimeSpan.FromSeconds(1.5));

    /// <summary>Drops a transition that was queued for the next bar.</summary>
    public void CancelQueuedTransition() => MusicManager.Instance.CancelPendingTransition();

    /// <summary>Fires the one-shot fanfare on its own voice, ducking the music under it.</summary>
    public void PlayStinger() => MusicManager.Instance.PlayStinger("stinger", 0.9f, duckMusic: true);

    /// <summary>
    /// Plays the stand-in dialogue line and ducks the music for exactly as long as it lasts, using
    /// the handle form so overlapping lines reference-count correctly.
    /// </summary>
    public void PlayDuckedDialogue()
    {
        var voice = AudioResourceManager.Instance.Clone("voice", $"voice_{Guid.NewGuid():N}");
        if (voice is null)
        {
            return;
        }

        var duck = MusicManager.Instance.PushDuck(0.25f, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600));

        void OnCompleted(object sender, EventArgs e)
        {
            voice.PlaybackCompleted -= OnCompleted;
            duck.Dispose();
            AudioResourceManager.Instance.Unload(voice.Key);
        }

        voice.PlaybackCompleted += OnCompleted;
        voice.Play();
    }

    /// <summary>Holds a duck open until <see cref="ReleaseHeldDuck"/>, to show the handle form.</summary>
    public void HoldDuck()
        => _heldDuck ??= MusicManager.Instance.PushDuck(0.2f, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(800));

    /// <summary>Releases the held duck.</summary>
    public void ReleaseHeldDuck()
    {
        _heldDuck?.Dispose();
        _heldDuck = null;
    }

    /// <summary>Jumps the current track to a named marker, if it has one.</summary>
    /// <param name="marker">The marker's name.</param>
    /// <returns><see langword="true"/> if the jump happened.</returns>
    public bool JumpToMarker(string marker) => MusicManager.Instance.JumpToMarker(marker);

    /// <summary>Toggles the global engine pause.</summary>
    /// <remarks>
    /// The readout is refreshed from the engine cycle, and THE CYCLE PARKS WHILE PAUSED — so the
    /// banner cannot be written after the pause, only before it. The engine renders one forced final
    /// frame on its way in (that is what makes pause overlays possible at all), and writing the text
    /// here is what gets it onto that frame.
    /// </remarks>
    public void TogglePause()
    {
        if (Engine.Instance.IsPaused)
        {
            Engine.Instance.Resume();
        }
        else
        {
            _readout?.SetText(BuildReadout(pausing: true));
            Engine.Instance.Pause();
        }
    }

    /// <summary>Stops the engine and tears the music system down. Call when the page is closing.</summary>
    public void Stop()
    {
        ReleaseHeldDuck();
        MusicManager.Instance.Dispose();

        _stems?.Dispose();
        _midiTrack?.Dispose();
        _trackA?.Dispose();
        _trackB?.Dispose();

        Engine.Instance.Stop();
        AudioSystem.Shutdown();
    }
}
