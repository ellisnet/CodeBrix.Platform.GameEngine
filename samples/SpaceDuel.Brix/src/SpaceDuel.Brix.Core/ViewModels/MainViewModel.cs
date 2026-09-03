using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using System;
using System.Diagnostics;

namespace SpaceDuel.Brix.ViewModels;

/// <summary>
/// Implemented by the view model that owns the game host, so the page can hand over its
/// <see cref="GameSurfaceCanvas"/> as soon as the canvas has a real size.
/// </summary>
public interface IManageGameCanvas
{
    /// <summary>Called once, when the canvas first has a non-zero size.</summary>
    /// <param name="canvas">The canvas the game renders into.</param>
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

/// <summary>
/// View model for the SpaceDuel.Brix main page. It owns the <see cref="SpaceDuelGameHost"/> and
/// chooses the render tier before the canvas builds its scene pipeline.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private SpaceDuelGameHost _host;

    /// <summary>Initializes a new instance of the <see cref="MainViewModel"/> class.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    //The game draws its own HUD, so the page has no bindable state.

    #endregion

    #region | Commands and their implementations |

    //No commands - every control is a key on the keyboard.

    #endregion

    #region | IManageGameCanvas implementation |

    /// <inheritdoc />
    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // Space Duel is a GPU-tier sample: the ships and lasers are rotated every frame and the
        // star fields are two full parallax layers, which is exactly the load the GPU backbuffer
        // is for. Set SPACEDUEL_USE_CPU=1 to run the identical game on the CPU path instead, for
        // a side-by-side comparison. UseGpuRendering MUST be set before the first access to
        // canvas.Host - the render tier cannot change once the scene pipeline exists.
        canvas.UseGpuRendering = Environment.GetEnvironmentVariable("SPACEDUEL_USE_CPU") != "1";
        Debug.WriteLine($"SpaceDuel render tier: {(canvas.UseGpuRendering ? "GPU" : "CPU")}");

        _host = new SpaceDuelGameHost(canvas);
        _host.Initialize();
    }

    #endregion
}
