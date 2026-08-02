using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using MusicDemo.Game;
using System.Diagnostics;

namespace MusicDemo.ViewModels;

/// <summary>Lets the page hand the view model the game canvas once it has a size.</summary>
public interface IManageGameCanvas
{
    /// <summary>Called once, when the canvas has started for the first time.</summary>
    /// <param name="canvas">The started canvas.</param>
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

/// <summary>
/// The MusicDemo page's view model. It owns the <see cref="MusicDemoGame"/> and exposes it to the
/// page, which drives the controls directly.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    /// <summary>Creates the view model.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("MusicDemo view model startup.");
    }

    /// <summary>
    /// The running demo, or null before the canvas has started. The page reads this directly rather
    /// than binding to it, so it needs no change notification.
    /// </summary>
    public MusicDemoGame Demo { get; private set; }

    #region | IManageGameCanvas implementation |

    /// <inheritdoc/>
    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        Demo = new MusicDemoGame(canvas);
        Demo.Start();
    }

    #endregion
}
