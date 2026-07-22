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

    internal GameSurfaceCanvasPixelFramePresenter(GameSurfaceCanvas canvas)
    {
        _canvas = canvas;
        _paintHandler = Paint;
    }

    /// <inheritdoc />
    protected override void RequestPaint()
    {
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
