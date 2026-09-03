using System.Drawing;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;
/// <summary>
/// Ambient per-render-pass context (per thread).
/// Set once per View render pass in RenderSurfaceHost.
/// SceneLayer-mode drawables can query this when they don't hold a View reference.
/// </summary>
internal sealed class RenderContext
{
    [ThreadStatic]
    private static RenderContext? _current;
    internal static RenderContext? Current => _current;

    private readonly RenderContext? _prior;

    private RenderContext(View view, long tick, RenderContext? prior)
    {
        View = view;
        Tick = tick;

        // Capture every value used by the view's world/screen transform. GPU
        // rendering occurs on the UI/GL thread while the engine thread continues
        // updating cameras and animated viewports, so reading these values live
        // while drawing can produce multiple transforms within one frame.
        CameraPositionPx = view.Camera.PositionPx;
        ViewportTargetRectPx = view.Viewport.TargetRectPx;
        ViewportScreenOffsetPx = view.Viewport.ScreenOffsetPx;
        ViewEffectOffsetFactor = view.EffectOffsetFactor;
        ViewEffectOffsetPx = view.EffectOffsetPx;

        var z = view.Viewport.Zoom;
        ViewportZoom = (z > 0f) ? z : 1f;

        _prior = prior;
    }

    internal View View { get; }
    internal long Tick { get; }
    internal PointF CameraPositionPx { get; }
    internal Rectangle ViewportTargetRectPx { get; }
    internal PointF ViewportScreenOffsetPx { get; }
    internal float ViewportZoom { get; }
    internal PointF ViewEffectOffsetFactor { get; }
    internal PointF ViewEffectOffsetPx { get; }

    internal static void Push(View view, long tick)
    {
        _current = new RenderContext(view, tick, _current);
    }

    internal static void Pop()
    {
        _current = _current?._prior;
    }
}
