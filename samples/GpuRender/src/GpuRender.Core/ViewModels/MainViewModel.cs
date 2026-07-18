using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.Simple;
using GpuRender.Game;
using System;
using System.Diagnostics;

namespace GpuRender.ViewModels;

public interface IManageGameCanvas { void CanvasFirstStart(GameSurfaceCanvas canvas); }

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private GpuRenderGame _game;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    //public string Greeting => "Hello from GpuRender!";

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        // This sample is the Tier B (GPU) showcase, so GPU rendering is the default; set
        // GPURENDER_USE_CPU=1 to run the identical shader scene on the Tier A CPU path for
        // comparison. Either way the render resolution tracks the window (no SetRenderResolution):
        // the shader scene is resolution-independent, unlike SoftRender's fixed 320x200 buffer.
        canvas.UseGpuRendering = Environment.GetEnvironmentVariable("GPURENDER_USE_CPU") != "1";
        Debug.WriteLine($"GpuRender render tier: {(canvas.UseGpuRendering ? "B (GPU)" : "A (CPU)")}");

        _game = new GpuRenderGame(canvas);
        _game.Start();
    }

    #endregion
}
