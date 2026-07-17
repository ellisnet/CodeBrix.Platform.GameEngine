using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using SoftRender.Game;
using System.Diagnostics;

namespace SoftRender.ViewModels;

public interface IManageGameCanvas { void CanvasFirstStart(GameSurfaceCanvas canvas); }

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private SoftRenderGameHost _gameHost;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    //public string Greeting => "Hello from SoftRender!";

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // Software-rendered host: the game renders 320x200 CPU frames at 70 Hz and the
        // canvas presents them (letterboxed) through its PixelFramePresenter.
        _gameHost = new SoftRenderGameHost(canvas);
        _gameHost.Initialize();
    }

    #endregion
}
