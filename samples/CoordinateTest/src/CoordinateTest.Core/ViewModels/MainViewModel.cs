using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using CoordinateTest.Game;
using System.Diagnostics;

namespace CoordinateTest.ViewModels;

public interface IManageGameCanvas { void CanvasFirstStart(GameSurfaceCanvas canvas); }

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private CoordinateTestGame _game;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    //public string Greeting => "Hello from CoordinateTest!";

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        _game = new CoordinateTestGame(canvas);
        _game.InitializeGame();
    }

    #endregion
}
