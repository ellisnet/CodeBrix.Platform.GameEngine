using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Rendering;
using Microsoft.Extensions.Logging;
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
    private MusicStemSet _songStems;
    private FileMusicTrack _trackA;
    private FileMusicTrack _trackB;
    private MidiMusicTrack _midiTrack;
    private MidiMusicTrack _samplerTrack;
    private TextBlock _readout;
    private IDisposable _heldDuck;

    /// <summary>Creates the demo over a render surface.</summary>
    /// <param name="canvas">The surface to draw the readout into.</param>
    public MusicDemoGame(GameSurfaceCanvas canvas)
        => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>The stem set, for the per-layer controls.</summary>
    public MusicStemSet Stems => _stems;

    /// <summary>
    /// The stem set built from the generated stems export, for the per-layer controls on that one.
    /// </summary>
    public MusicStemSet SongStems => _songStems;

    /// <summary>The MIDI track, for the per-channel layering and speed controls.</summary>
    public MidiMusicTrack MidiTrack => _midiTrack;

    /// <summary>The Decent Sampler MIDI track, whose file changes tempo partway through.</summary>
    public MidiMusicTrack SamplerTrack => _samplerTrack;

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

        // Off unless the environment asks for it; see MusicDemoWalkthrough for what it is for.
        if (MusicDemoWalkthrough.IsRequested)
        {
            MusicDemoWalkthrough.Start(this);
        }
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

        // The same thing again through the other instrument format, over a file whose tempo CHANGES
        // partway through. A .dspreset is not one file - its Samples folder sits beside it - so it
        // is loaded by path from disk, never from an asset pack.
        _samplerTrack = new MidiMusicTrack("Decent Sampler Theme",
            MusicAssetFactory.DecentSamplerPresetPath, MusicAssetFactory.TempoChangeMidiPath)
        {
            IsLooping = true,
        };

        // A stems export - the shape a music service hands over - straight into a stem set. The
        // grid comes out of the MIDI beside the recordings, so bar-locked layer changes and
        // bar-quantised transitions work with nothing else set up.
        _songStems = MusicStemSet.FromSunoStems("Song Stems", MusicAssetFactory.StemsExportFolder,
            "Vocals", "Drums", "Bass");
        _songStems.IsLooping = true;

        _playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.All };
        _playlist.Add(_trackA);
        _playlist.Add(_trackB);

        resources.LoadFromFile("stinger", MusicAssetFactory.StingerPath);
        resources.LoadFromFile("voice", MusicAssetFactory.VoicePath);

        LogWhatWasLoaded();
    }

    // The sample writes a line per loaded track because the interesting facts - which instrument
    // format was used, what the file said about its own tempo, what the export could not account
    // for - are otherwise invisible on a screen full of buttons.
    private void LogWhatWasLoaded()
    {
        Engine.Logger.LogInformation(
            "MusicDemo: MIDI track '{Key}' loaded through an SFZ instrument. Grid: {Tempo:0} BPM, "
            + "{BeatsPerBar:0} beats/bar, markers: {Markers}. Problems: {Problems}.",
            _midiTrack.Key,
            _midiTrack.Timeline?.BeatsPerMinute ?? 0,
            _midiTrack.Timeline?.BeatsPerBar ?? 0,
            _midiTrack.Timeline is null ? "(none)" : string.Join(", ", _midiTrack.Timeline.Markers),
            _midiTrack.Problems.Count);

        Engine.Logger.LogInformation(
            "MusicDemo: MIDI track '{Key}' loaded through a Decent Sampler instrument ({Instrument}). "
            + "Grid: {Tempo:0} BPM, {BeatsPerBar:0} beats/bar, tempo changes: {Changes}, markers: {Markers}. "
            + "Problems: {Problems}.",
            _samplerTrack.Key,
            Path.GetFileName(MusicAssetFactory.DecentSamplerPresetPath),
            _samplerTrack.Timeline?.BeatsPerMinute ?? 0,
            _samplerTrack.Timeline?.BeatsPerBar ?? 0,
            _samplerTrack.Timeline?.HasTempoChanges ?? false,
            _samplerTrack.Timeline is null ? "(none)" : string.Join(", ", _samplerTrack.Timeline.Markers),
            _samplerTrack.Problems.Count);

        Engine.Logger.LogInformation(
            "MusicDemo: stem set '{Key}' loaded from the stems export in '{Folder}': {Stems}. "
            + "Grid: {Tempo:0} BPM, tempo changes: {Changes}. Problems: {Problems}.",
            _songStems.Key,
            Path.GetFileName(MusicAssetFactory.StemsExportFolder),
            string.Join(", ", _songStems.Stems.Select(stem => stem.Name)),
            _songStems.Timeline?.BeatsPerMinute ?? 0,
            _songStems.Timeline?.HasTempoChanges ?? false,
            _songStems.Problems.Count);
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

        if (_songStems is not null)
        {
            text.Append("Song stems  : ");
            foreach (var stem in _songStems.Stems)
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

        if (_samplerTrack?.Timeline is not null)
        {
            var timeline = _samplerTrack.Timeline;
            text.AppendLine($"Sampler grid: {timeline.BeatsPerMinute:0} BPM to start, "
                            + $"{timeline.BeatsPerBar:0} beats/bar, tempo changes: {timeline.HasTempoChanges}");
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
    {
        LogQuantisedWait(quantize);
        MusicManager.Instance.CrossfadeTo(_trackB, TimeSpan.FromSeconds(2), quantize);
    }

    /// <summary>Crossfades back to the first linear track, optionally waiting for the next bar.</summary>
    /// <param name="quantize">The boundary to wait for.</param>
    public void CrossfadeToTrackA(MusicTransitionQuantize quantize)
    {
        LogQuantisedWait(quantize);
        MusicManager.Instance.CrossfadeTo(_trackA, TimeSpan.FromSeconds(2), quantize);
    }

    // Says what the grid answered and what a constant-tempo grid WOULD have answered. On a file
    // whose tempo changes the two differ, which is the whole point of quantising through the map.
    private static void LogQuantisedWait(MusicTransitionQuantize quantize)
    {
        if (quantize == MusicTransitionQuantize.Immediate)
        {
            return;
        }

        var current = MusicManager.Instance.NowPlaying;
        var timeline = current?.Timeline;

        if (current is null || timeline is null)
        {
            Engine.Logger.LogInformation(
                "MusicDemo: a {Quantize} transition was asked for with no grid to wait on, so it runs now.",
                quantize);

            return;
        }

        var position = current.Position;
        var wait = timeline.TimeToNextBoundary(position, quantize);
        var grid = quantize == MusicTransitionQuantize.Bar ? timeline.SecondsPerBar : timeline.SecondsPerBeat;
        var atOpeningTempo = grid - (position.TotalSeconds % grid);

        Engine.Logger.LogInformation(
            "MusicDemo: {Quantize} transition from '{Key}' at {Position:0.000}s waits {Wait:0.000}s. "
            + "The opening tempo is {Tempo:0} BPM (tempo changes: {Changes}), and a grid fixed at it "
            + "would have said {Fixed:0.000}s.",
            quantize, current.Key, position.TotalSeconds, wait.TotalSeconds,
            timeline.BeatsPerMinute, timeline.HasTempoChanges, atOpeningTempo);
    }

    /// <summary>Plays the layered stem set. Only the pad layer starts audible.</summary>
    public void PlayStems() => MusicManager.Instance.Play(_stems, TimeSpan.FromSeconds(1));

    /// <summary>Plays the MIDI theme, rendered live through the generated SFZ instrument.</summary>
    public void PlayMidi() => MusicManager.Instance.Play(_midiTrack, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Plays the second MIDI theme, rendered live through the generated Decent Sampler instrument.
    /// Its file changes tempo partway through, so a bar-quantised transition away from it has to
    /// follow the tempo map to land on the beat.
    /// </summary>
    public void PlayDecentSamplerTheme() => MusicManager.Instance.Play(_samplerTrack, TimeSpan.FromSeconds(1));

    /// <summary>Plays the stem set built from the generated stems export.</summary>
    public void PlaySongStems() => MusicManager.Instance.Play(_songStems, TimeSpan.FromSeconds(1));

    /// <summary>Fades one layer of the stems export in or out.</summary>
    /// <param name="stemName">The stem's name, as it appears on the export's file names.</param>
    /// <param name="target">The gain to fade to, 0.0 to 1.0.</param>
    public void FadeSongStem(string stemName, float target)
        => _songStems?[stemName].FadeTo(target, TimeSpan.FromSeconds(2));

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
        _songStems?.Dispose();
        _midiTrack?.Dispose();
        _samplerTrack?.Dispose();
        _trackA?.Dispose();
        _trackB?.Dispose();

        Engine.Instance.Stop();
        AudioSystem.Shutdown();
    }
}
