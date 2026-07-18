using System;
using System.Drawing;
using System.Text;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using SkiaSharp;
using static CodeBrix.Platform.GameEngine.Drawing.Direct.TextBlock;

namespace GpuRender.Game;

/// <summary>
/// The GpuRender demo: the Tier B (GPU) showcase and the GPU-first counterpart to the SoftRender
/// sample. Where SoftRender computes every pixel on the CPU into a fixed 320x200 framebuffer,
/// GpuRender draws a resolution-independent shader scene — the <see cref="PlasmaBackdrop"/>
/// (SkSL plasma + starfield) — that the GPU rasterises at the full window size through the
/// engine's <see cref="CodeBrixPlatformGpuRenderSurfaceAdapter"/> (offscreen OpenGL/GLES + one
/// readback per frame). A stats overlay shows the live cycle and GPU frame rates; clicking
/// anywhere toggles the global engine pause, which also demonstrates the Tier B pause-overlay
/// frame and <see cref="Engine.LastFrameBeforePause"/> capture.
/// </summary>
public sealed class GpuRenderGame
{
    private readonly GameSurfaceCanvas _canvas;

    private RenderSurfaceHost<BackbufferBase> _renderSurface;
    private PlasmaBackdrop _backdrop;
    private TextBlock _statsText;
    private DirectRectangle _pausedDimmer;
    private TextBlock _pausedText;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuRenderGame"/> class.
    /// </summary>
    /// <param name="canvas">The render surface to draw into.</param>
    public GpuRenderGame(GameSurfaceCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    /// <summary>
    /// Starts the engine and builds the shader scene. Must be called on the UI thread once the
    /// surface has a non-zero size, after <see cref="GameSurfaceCanvas.UseGpuRendering"/> has
    /// been chosen.
    /// </summary>
    public void Start()
    {
        _renderSurface = _canvas.Host;
        _renderSurface.ViewManager.ConfigureSingleFullView();

        Engine.Instance.CPSCalculated += OnCpsCalculated;
        Engine.Instance.Paused += OnEnginePaused;
        Engine.Instance.Resumed += OnEngineResumed;

        Engine.Instance.Start(SynchronizationContext.Current);
        Engine.Instance.Configuration.TargetFPS = 60;

        var adapter = _renderSurface.RenderSurfaceAdapter;
        var view = _renderSurface.ViewManager.Views[0];

        // The shader scene, at the bottom of the direct-drawing Z-order; the engine renders the
        // stats text (and the pause overlay) above it.
        _backdrop = new PlasmaBackdrop(_renderSurface, view, new Rectangle(0, 0, adapter.Width, adapter.Height));
        _backdrop.ZOrder = 0;

        _statsText = new TextBlock(_renderSurface, view,
                new Rectangle(16, 12, Math.Max(200, adapter.Width - 32), 170), null)
            .SetFont(SKTypeface.FromFamilyName("Open Sans"), 15f, minSize: 12f)
            .SetColors(Color.White, Color.Transparent)
            .SetAlignment(SKTextAlign.Left, VerticalAlign.Top)
            .EnableWrapping()
            .SetMaxLines(9)
            .UseShadow()
            .SetShadow(2, 2, 220, 2.0f);
        _statsText.ZOrder = 10;

        // The render resolution tracks the window in this sample, so follow adapter resizes.
        adapter.Resized += OnAdapterResized;

        // Clicking anywhere toggles the global engine pause. This hooks the canvas's UI-level
        // pointer event on purpose: engine input pollers stop while paused, but UI-level input
        // keeps flowing — so the same click resumes.
        _canvas.PointerPressed += OnCanvasPointerPressed;
    }

    /// <summary>
    /// Stops the engine. Call when the hosting page is closing.
    /// </summary>
    public void Stop()
    {
        _canvas.PointerPressed -= OnCanvasPointerPressed;
        Engine.Instance.CPSCalculated -= OnCpsCalculated;
        Engine.Instance.Paused -= OnEnginePaused;
        Engine.Instance.Resumed -= OnEngineResumed;
        if (_renderSurface is not null)
            _renderSurface.RenderSurfaceAdapter.Resized -= OnAdapterResized;
        Engine.Instance.Stop();
    }

    private void OnAdapterResized(RenderSurfaceAdapterResizedEventArgs args)
    {
        if (_backdrop is not null)
            _backdrop.ScreenBounds = new Rectangle(0, 0, args.NewWidth, args.NewHeight);
        if (_statsText is not null)
            _statsText.ScreenBounds = new Rectangle(16, 12, Math.Max(200, args.NewWidth - 32), 170);
    }

    // UI thread (the engine posts CPS samples there).
    private void OnCpsCalculated(CyclesPerSecondCalculatedEventArgs cps)
    {
        var adapter = _renderSurface?.RenderSurfaceAdapter;
        string tier = adapter is CodeBrixPlatformGpuRenderSurfaceAdapter gpu
            ? gpu.IsGpuInitialized switch
            {
                true => "B (GPU: offscreen GL + readback)",
                false => "B requested, GPU unavailable -> CPU fallback",
                null => "B (GPU initializing...)",
            }
            : "A (CPU)";

        var sb = new StringBuilder()
            .AppendLine($"GpuRender — engine version {EngineInfo.Version}")
            .AppendLine($"Render tier: {tier}   |   surface {_renderSurface?.Backbuffer.Width}x{_renderSurface?.Backbuffer.Height}   |   click to pause/resume")
            .Append(cps.ToString());

        _statsText?.SetText(sb.ToString());
    }

    // Engine thread, after the pause snapshot was captured and the loops are quiescent — the
    // sanctioned place to build a pause screen. The engine renders (Tier A) or requests from the
    // adapter (Tier B) one final frame after this handler returns, which makes the overlay visible.
    private void OnEnginePaused()
    {
        var adapter = _renderSurface.RenderSurfaceAdapter;
        var bounds = new Rectangle(0, 0, adapter.Width, adapter.Height);
        var view = _renderSurface.ViewManager.Views[0];

        _pausedDimmer = new DirectRectangle(Color.Black, _renderSurface, view, bounds, null)
            .SetFilled(true)
            .SetAlpha(140);
        _pausedDimmer.ZOrder = 90;

        _pausedText = new TextBlock(_renderSurface, view, bounds, null)
            .SetFont(SKTypeface.FromFamilyName("Open Sans"), 44f, minSize: 20f)
            .SetColors(Color.White, Color.Transparent)
            .SetAlignment(SKTextAlign.Center, VerticalAlign.Center)
            .UseShadow()
            .SetShadow(3, 3, 255, 4.0f);
        _pausedText.SetText("PAUSED — click to resume");
        _pausedText.ZOrder = 100;

        // Log the pause capture so the Tier B snapshot path is verifiable from the console alone
        // (the capture happens before this event, per the engine's pause contract).
        var snapshot = Engine.Instance.LastFrameBeforePause;
        Console.WriteLine(snapshot is null
            ? "[GpuRender] PAUSED — no LastFrameBeforePause captured"
            : $"[GpuRender] PAUSED — LastFrameBeforePause captured {snapshot.Width}x{snapshot.Height}");
    }

    // Engine thread, first resumed cycle.
    private void OnEngineResumed()
    {
        _pausedDimmer?.Dispose();
        _pausedDimmer = null;
        _pausedText?.Dispose();
        _pausedText = null;
        Console.WriteLine("[GpuRender] RESUMED");
    }

    private void OnCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Engine.Instance.IsPaused)
            Engine.Instance.Resume();
        else
            Engine.Instance.Pause();
    }
}
