using System;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MusicDemo.Game;
using MusicDemo.ViewModels;

namespace MusicDemo.Views;

/// <summary>
/// The MusicDemo control surface. The handlers call straight into <see cref="MusicDemoGame"/> rather
/// than binding commands, because what this sample is FOR is showing the music API being called —
/// one line per control, with nothing in between to read past.
/// </summary>
public sealed partial class MainPage : Page
{
    private IManageGameCanvas _gameCanvasManager;
    private MainViewModel _viewModel;

    /// <summary>Creates the page.</summary>
    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
            _gameCanvasManager = DataContext as IManageGameCanvas;
            _viewModel = DataContext as MainViewModel;
        };

        this.InitializeComponent();

        GameCanvas.FirstStarted += (_, _) => _gameCanvasManager?.CanvasFirstStart(GameCanvas);
    }

    // Null until the canvas has started, and every handler below is reachable before then.
    private MusicDemoGame Demo => _viewModel?.Demo;

    // ----- volume buses -----

    private void OnMasterVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
        => AudioMixer.MasterVolume = (float)(e.NewValue / 100.0);

    private void OnMusicVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
        => AudioMixer.MusicVolume = (float)(e.NewValue / 100.0);

    private void OnSfxVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
        => AudioMixer.SfxVolume = (float)(e.NewValue / 100.0);

    // ----- transport -----

    private void OnPlayTrackA(object sender, object e) => Demo?.PlayTrackA();

    private void OnPlayStems(object sender, object e)
    {
        Demo?.PlayStems();
        SyncStemSliders();
    }

    private void OnPlayMidi(object sender, object e) => Demo?.PlayMidi();

    private void OnPlaySamplerTheme(object sender, object e) => Demo?.PlayDecentSamplerTheme();

    private void OnPlaySongStems(object sender, object e) => Demo?.PlaySongStems();

    private void OnPlayPlaylist(object sender, object e) => Demo?.PlayPlaylist();

    private void OnNextInPlaylist(object sender, object e) => Demo?.NextInPlaylist();

    private void OnStop(object sender, object e) => Demo?.StopMusic();

    // ----- quantised transitions -----

    private void OnCrossfadeNow(object sender, object e)
        => Demo?.CrossfadeToTrackB(MusicTransitionQuantize.Immediate);

    private void OnCrossfadeOnBar(object sender, object e)
        => Demo?.CrossfadeToTrackB(MusicTransitionQuantize.Bar);

    private void OnCrossfadeBackOnBar(object sender, object e)
        => Demo?.CrossfadeToTrackA(MusicTransitionQuantize.Bar);

    private void OnCancelQueued(object sender, object e) => Demo?.CancelQueuedTransition();

    // ----- adaptive stems -----

    private void OnPadGainChanged(object sender, RangeBaseValueChangedEventArgs e) => SetStemGain(0, e.NewValue);

    private void OnBassGainChanged(object sender, RangeBaseValueChangedEventArgs e) => SetStemGain(1, e.NewValue);

    private void OnLeadGainChanged(object sender, RangeBaseValueChangedEventArgs e) => SetStemGain(2, e.NewValue);

    private void SetStemGain(int index, double percent)
    {
        var stems = Demo?.Stems;
        if (stems is not null)
        {
            stems[index].Gain = (float)(percent / 100.0);
        }
    }

    private void OnFadeLeadIn(object sender, object e)
        => FadeStem(2, 1f, LeadSlider);

    private void OnFadeLeadOut(object sender, object e)
        => FadeStem(2, 0f, LeadSlider);

    private void FadeStem(int index, float target, Slider slider)
    {
        var stems = Demo?.Stems;
        if (stems is null)
        {
            return;
        }

        stems[index].FadeTo(target, TimeSpan.FromSeconds(2));

        // The slider would otherwise keep showing where the layer WAS; setting it here would fight
        // the fade, so it is moved to the destination and the fade is left to do the audible part.
        slider.Value = target * 100.0;
    }

    private void SyncStemSliders()
    {
        var stems = Demo?.Stems;
        if (stems is null)
        {
            return;
        }

        PadSlider.Value = stems[0].Gain * 100.0;
        BassSlider.Value = stems[1].Gain * 100.0;
        LeadSlider.Value = stems[2].Gain * 100.0;
    }

    // ----- the stems export -----

    private void OnFadeSongDrumsIn(object sender, object e) => Demo?.FadeSongStem("Drums", 1f);

    private void OnFadeSongDrumsOut(object sender, object e) => Demo?.FadeSongStem("Drums", 0f);

    private void OnFadeSongBassIn(object sender, object e) => Demo?.FadeSongStem("Bass", 1f);

    private void OnFadeSongBassOut(object sender, object e) => Demo?.FadeSongStem("Bass", 0f);

    // ----- MIDI layers -----

    private void OnMidiHarmonyOut(object sender, object e)
        => Demo?.MidiTrack?.FadeLayerTo(2, 0f, TimeSpan.FromSeconds(2));

    private void OnMidiHarmonyIn(object sender, object e)
        => Demo?.MidiTrack?.FadeLayerTo(2, 1f, TimeSpan.FromSeconds(2));

    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var midi = Demo?.MidiTrack;
        if (midi is not null)
        {
            midi.Speed = (float)(e.NewValue / 100.0);
        }
    }

    // ----- ducking and stingers -----

    private void OnStinger(object sender, object e) => Demo?.PlayStinger();

    private void OnDialogue(object sender, object e) => Demo?.PlayDuckedDialogue();

    private void OnHoldDuck(object sender, object e) => Demo?.HoldDuck();

    private void OnReleaseDuck(object sender, object e) => Demo?.ReleaseHeldDuck();

    // ----- jump points -----

    private void OnJumpToVerse(object sender, object e) => Demo?.JumpToMarker("verse");

    private void OnJumpToChorus(object sender, object e) => Demo?.JumpToMarker("chorus");

    // ----- pause -----

    private void OnTogglePause(object sender, object e) => Demo?.TogglePause();
}
