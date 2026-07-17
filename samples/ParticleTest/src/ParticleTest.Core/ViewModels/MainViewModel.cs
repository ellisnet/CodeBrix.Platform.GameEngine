using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using ParticleTest.Game;
using System.Diagnostics;

namespace ParticleTest.ViewModels;

public interface IManageGameCanvas { void CanvasFirstStart(GameSurfaceCanvas canvas); }

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private ParticleTestGame _game;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    //public string Greeting => "Hello from ParticleTest!";

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // Render at a fixed 16:9 resolution; the surface letterboxes it to fit the window.
        canvas.SetRenderResolution(1280, 720);
        _game = new ParticleTestGame(canvas);
        _game.Start();
    }

    #endregion
}
