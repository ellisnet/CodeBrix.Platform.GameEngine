using System.Diagnostics;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using KenneyAssetsDemo.Game;
using Microsoft.Extensions.Logging;

namespace KenneyAssetsDemo.ViewModels;

/// <summary>
/// Contract the page uses to hand its <see cref="GameSurfaceCanvas"/> to the view model once the
/// canvas has a real size.
/// </summary>
public interface IManageGameCanvas
{
    /// <summary>Called the first time the game surface reports a non-zero size.</summary>
    /// <param name="canvas">The game surface canvas to render into.</param>
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

/// <summary>
/// View model for the demo's single page. It owns the <see cref="KenneyAssetsDemoGameHost"/> and
/// creates it once the game surface has a size.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private KenneyAssetsDemoGameHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | IManageGameCanvas implementation |

    /// <inheritdoc />
    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // Pin the render resolution before the first access to canvas.Host, so the engine always
        // renders the size the demo is laid out for and the surface letterboxes it into the window.
        canvas.SetRenderResolution(
            KenneyAssetsDemoGameHost.RenderWidth,
            KenneyAssetsDemoGameHost.RenderHeight);

        _host = new KenneyAssetsDemoGameHost(canvas);

        // Information keeps the demo's own start-up report and the engine's milestones on the console
        // without the per-cycle detail the engine logs at Debug. The report itself is written straight
        // to the console by the game host, so it is there whatever the logging settings are.
        _host.Initialize(logLevel: LogLevel.Information);
    }

    #endregion
}
