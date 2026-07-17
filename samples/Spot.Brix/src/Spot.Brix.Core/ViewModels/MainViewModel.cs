using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using System.Diagnostics;

namespace Spot.Brix.ViewModels;

public interface IManageGameCanvas
{
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private SpotBrixGameHost _host;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    private bool _musicEnabled = true;
    public bool MusicEnabled
    {
        get => _musicEnabled;
        set { SetProperty(ref _musicEnabled, value); _host?.SetMusicEnabled(value); }
    }

    private bool _soundEffectsEnabled = true;
    public bool SoundEffectsEnabled
    {
        get => _soundEffectsEnabled;
        set { SetProperty(ref _soundEffectsEnabled, value); _host?.SetSoundEffectsEnabled(value); }
    }

    private bool _jiggleEnabled = true;
    public bool JiggleEnabled
    {
        get => _jiggleEnabled;
        set { SetProperty(ref _jiggleEnabled, value); _host?.SetJiggleEnabled(value); }
    }

    private bool _cloudsEnabled = true;
    public bool CloudsEnabled
    {
        get => _cloudsEnabled;
        set { SetProperty(ref _cloudsEnabled, value); _host?.SetCloudsEnabled(value); }
    }

    #endregion

    #region | Commands and their implementations |

    private SimpleCommand _newGameCommand;
    public SimpleCommand NewGameCommand => (_newGameCommand ??= new SimpleCommand(DoNewGame));

    private void DoNewGame(object parameter) => _host?.StartDefaultGame();

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        _host = new SpotBrixGameHost(canvas);
        _host.Initialize();
        _host.StartDefaultGame();

        //Apply the current toolbar toggle states to the freshly-created host.
        _host.SetMusicEnabled(_musicEnabled);
        _host.SetSoundEffectsEnabled(_soundEffectsEnabled);
        _host.SetJiggleEnabled(_jiggleEnabled);
        _host.SetCloudsEnabled(_cloudsEnabled);
    }

    #endregion
}
