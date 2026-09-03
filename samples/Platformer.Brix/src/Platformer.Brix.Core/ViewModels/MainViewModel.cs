using System;
using System.Diagnostics;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using Platformer.Brix.Game;

namespace Platformer.Brix.ViewModels;

/// <summary>
/// Contract the page uses to hand its <see cref="GameSurfaceCanvas"/> to the view model once the
/// canvas has a real size, and to learn that the game wants the application closed.
/// </summary>
public interface IManageGameCanvas
{
    /// <summary>Raised when the game asks for the application to close (the player pressed Esc).</summary>
    event EventHandler ExitRequested;

    /// <summary>Called the first time the game surface reports a non-zero size.</summary>
    /// <param name="canvas">The game surface canvas to render into.</param>
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

/// <summary>
/// View model for the platformer's single page. It owns the <see cref="PlatformerGameHost"/> and
/// creates it once the game surface has a size.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    // The engine renders at this fixed resolution; the surface letterboxes it into the window.
    private const int RenderWidth = 960;
    private const int RenderHeight = 576;

    private PlatformerGameHost _host;

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
    public event EventHandler ExitRequested;

    /// <inheritdoc />
    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // Pin the render resolution before the first access to canvas.Host, so the engine
        // always renders the 960x576 level view the game was laid out for.
        canvas.SetRenderResolution(RenderWidth, RenderHeight);

        _host = new PlatformerGameHost(canvas);
        _host.ExitRequested += OnHostExitRequested;
        _host.Initialize();

        Debug.WriteLine($"Platformer.Brix started at {RenderWidth}x{RenderHeight}.");
    }

    #endregion

    private void OnHostExitRequested(object sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
