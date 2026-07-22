using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using Microsoft.UI.Dispatching;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// The <see cref="PixelFramePresenter"/> implementation bound to a
/// <see cref="GameSurfaceCanvas"/> (see <see cref="GameSurfaceCanvas.UsePixelFramePresenter"/>):
/// paint requests from any thread coalesce onto a single in-flight canvas invalidation on the
/// UI thread.
/// </summary>
internal sealed class GameSurfaceCanvasPixelFramePresenter : PixelFramePresenter
{
    private readonly GameSurfaceCanvas _canvas;
    private readonly DispatcherQueueHandler _paintHandler; // cached: RequestPaint must not allocate per frame
    private int _paintScheduled;

    // True while the canvas is off the visual tree (window closing or page navigated away).
    // Set on the UI thread; read from the game-loop thread in RequestPaint, so volatile.
    private volatile bool _canvasUnloaded;

    internal GameSurfaceCanvasPixelFramePresenter(GameSurfaceCanvas canvas)
    {
        _canvas = canvas;
        _paintHandler = Paint;

        // Track whether the canvas is in the visual tree; RequestPaint stops scheduling
        // invalidations while it is not (see the comment there). The presenter and the canvas
        // share a lifetime, so these subscriptions are never unhooked.
        _canvas.Unloaded += (_, _) => _canvasUnloaded = true;
        _canvas.Loaded += (_, _) => _canvasUnloaded = false;
    }

    /// <inheritdoc />
    protected override void RequestPaint()
    {
        // While the canvas is off the visual tree, drop the invalidation (the frame itself is
        // still cached by the base presenter): the game loop may well keep presenting, and
        // continuously posting to the dispatcher after the last window closes can keep a
        // head's message loop from ever draining its queue and exiting (observed as a zombie
        // process on the Win32-Skia head).
        if (_canvasUnloaded)
        {
            return;
        }

        // Coalesce to a single in-flight invalidation, the same pattern as the CpuRendering
        // render-surface adapter.
        if (Interlocked.CompareExchange(ref _paintScheduled, 1, 0) != 0)
        {
            return;
        }

        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            Paint();
        }
        else
        {
            dispatcherQueue.TryEnqueue(_paintHandler);
        }
    }

    private void Paint()
    {
        // Clear the latch before painting: a PresentFrame racing this paint schedules the
        // next invalidation instead of being lost.
        Interlocked.Exchange(ref _paintScheduled, 0);
        _canvas.Invalidate();
    }
}
