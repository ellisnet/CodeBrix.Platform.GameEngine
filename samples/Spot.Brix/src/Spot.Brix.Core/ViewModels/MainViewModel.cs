using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Spot.Brix.ViewModels;

public interface IManageGameCanvas
{
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

/// <summary>
/// Implemented by the page that can put the New Game dialog on screen. The view model owns the choices;
/// the page owns the XAML.
/// </summary>
public interface IShowNewGameDialog
{
    /// <summary>
    /// Shows the New Game dialog and waits for the player to start or cancel.
    /// </summary>
    /// <param name="viewModel">The choices to show, and the ones the player edits.</param>
    /// <returns>
    /// The options to start a game with, or <see langword="null"/> when the player cancelled.
    /// </returns>
    Task<NewGameOptions> ShowNewGameDialogAsync(NewGameViewModel viewModel);
}

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private SpotBrixGameHost _host;
    private SpotSettings _settings;
    private IShowNewGameDialog _dialogHost;
    private bool _dialogOpen;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");

        //Seed the toggles from the saved options. The backing fields are set directly: going through
        //the property setters here would write every default straight back to the configuration file.
        _settings = SpotSettings.Load();
        _musicEnabled = _settings.MusicEnabled;
        _soundEffectsEnabled = _settings.SoundEffectsEnabled;
        _jiggleEnabled = _settings.JiggleEnabled;
        _cloudsEnabled = _settings.CloudsEnabled;
        _gpuAccelerationEnabled = _settings.GpuAccelerationEnabled;
    }

    #region | Bindable properties |

    private bool _musicEnabled = true;
    public bool MusicEnabled
    {
        get => _musicEnabled;
        set { SetProperty(ref _musicEnabled, value); Persist(s => s.MusicEnabled = value); _host?.SetMusicEnabled(value); }
    }

    private bool _soundEffectsEnabled = true;
    public bool SoundEffectsEnabled
    {
        get => _soundEffectsEnabled;
        set { SetProperty(ref _soundEffectsEnabled, value); Persist(s => s.SoundEffectsEnabled = value); _host?.SetSoundEffectsEnabled(value); }
    }

    private bool _jiggleEnabled = true;
    public bool JiggleEnabled
    {
        get => _jiggleEnabled;
        set { SetProperty(ref _jiggleEnabled, value); Persist(s => s.JiggleEnabled = value); _host?.SetJiggleEnabled(value); }
    }

    private bool _cloudsEnabled = true;
    public bool CloudsEnabled
    {
        get => _cloudsEnabled;
        set { SetProperty(ref _cloudsEnabled, value); Persist(s => s.CloudsEnabled = value); _host?.SetCloudsEnabled(value); }
    }

    private bool _gpuAccelerationEnabled;

    /// <summary>
    /// Gets or sets whether the game surface renders on the GPU. The render tier is chosen when the
    /// canvas is first used, so a change here only takes effect the next time the sample is started.
    /// </summary>
    public bool GpuAccelerationEnabled
    {
        get => _gpuAccelerationEnabled;
        set { SetProperty(ref _gpuAccelerationEnabled, value); Persist(s => s.GpuAccelerationEnabled = value); }
    }

    /// <summary>
    /// Gets the model the New Game dialog binds to. It is kept for the life of the page so the dialog
    /// reopens on the previous game's choices.
    /// </summary>
    public NewGameViewModel NewGame { get; } = new();

    #endregion

    #region | Commands and their implementations |

    private SimpleCommand _newGameCommand;
    public SimpleCommand NewGameCommand => (_newGameCommand ??= new SimpleCommand((Func<object, Task>)(_ => ShowNewGameDialogAsync())));

    /// <summary>
    /// Puts the New Game dialog on screen and, if the player starts a game, hands the choices to the
    /// game host on the engine thread.
    /// </summary>
    private async Task ShowNewGameDialogAsync()
    {
        if (_host is null || _dialogOpen)
            return;

        if (_dialogHost is null)
        {
            //No page is wired up to show the dialog (design time, or a host without one): fall back to
            //the host's built-in quick game so the sample still starts.
            _host.StartDefaultGame();
            return;
        }

        _dialogOpen = true;

        try
        {
            Engine.Logger.LogInformation("Spot.Brix New Game dialog opening.");

            var options = await _dialogHost.ShowNewGameDialogAsync(NewGame);

            if (options is null)
            {
                Engine.Logger.LogInformation("Spot.Brix New Game dialog cancelled.");
                return;
            }

            Engine.Logger.LogInformation("Spot.Brix New Game dialog accepted: {0}x{1}, {2} player(s).",
                options.BoardWidth, options.BoardHeight, options.PlayerCount);

            _host.StartNewGameOnEngineThread(options);
        }
        catch (Exception ex)
        {
            //This runs fire-and-forget from the game surface, so nothing else would report a failure.
            Engine.Logger.LogError(ex, "The Spot.Brix New Game dialog failed.");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    #endregion

    #region | IManageGameCanvas implementation |

    /// <summary>
    /// Supplies the page that shows the New Game dialog. Called from the page's DataContextChanged
    /// handler, before the canvas starts.
    /// </summary>
    /// <param name="dialogHost">The page that can show the dialog.</param>
    public void SetNewGameDialogHost(IShowNewGameDialog dialogHost) => _dialogHost = dialogHost;

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        //The render tier has to be chosen before anything touches canvas.Host, which the game host does
        //as soon as it is constructed.
        canvas.UseGpuRendering = _gpuAccelerationEnabled;

        _host = new SpotBrixGameHost(canvas);
        _host.NewGameRequested += OnNewGameRequested;

        //Apply the saved toggle states before Initialize(), so the post-splash start-up already knows
        //whether the music should play.
        _host.SetMusicEnabled(_musicEnabled);
        _host.SetSoundEffectsEnabled(_soundEffectsEnabled);
        _host.SetJiggleEnabled(_jiggleEnabled);
        _host.SetCloudsEnabled(_cloudsEnabled);

        //The engine reads the same configuration file the options are saved in; pass the absolute path
        //so both sides use the copy next to the executable rather than one in the working directory.
        //Information keeps the sample's own milestones (splash, new game, game over) in the console
        //without the per-move detail the engine logs at Debug.
        _host.Initialize(configPath: SpotSettings.ConfigFilePath, logLevel: LogLevel.Information);
    }

    private void OnNewGameRequested() => _ = ShowNewGameDialogAsync();

    #endregion

    private void Persist(Action<SpotSettings> write)
    {
        if (_settings is null)
            return;

        try
        {
            write(_settings);
        }
        catch (Exception ex)
        {
            //A read-only working folder must not break the toggle itself.
            Debug.WriteLine($"Spot.Brix could not save its options: {ex.Message}");
        }
    }
}
