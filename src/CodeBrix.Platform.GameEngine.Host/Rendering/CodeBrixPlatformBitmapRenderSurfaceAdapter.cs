using System;
using System.Collections.Concurrent;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using Microsoft.UI.Xaml;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// CpuRendering (CPU) render-surface adapter: presents each engine frame by handing the latest
/// <see cref="SKImage"/> to a <see cref="GameSurfaceCanvas"/> and invalidating it on the UI thread.
/// This is the default, works on all CodeBrix.Platform heads, and requires no GPU interop.
/// </summary>
public sealed class CodeBrixPlatformBitmapRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
{
    private readonly GameSurfaceCanvas _canvas;
    private readonly bool _fixedResolution;
    private SKImage? _currentImage;
    private readonly ConcurrentQueue<SKImage> _toDispose = new();
    private bool _disposed;
    private int _paintScheduled;
    private SKRectI _latestBufferRect;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/> class.
    /// </summary>
    /// <param name="canvas">The <see cref="GameSurfaceCanvas"/> this adapter presents to.</param>
    /// <param name="fixedWidth">
    /// A fixed render width in pixels, or 0 (the default) to track the canvas width and follow window resizes.
    /// </param>
    /// <param name="fixedHeight">
    /// A fixed render height in pixels, or 0 (the default) to track the canvas height and follow window resizes.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> is null.</exception>
    public CodeBrixPlatformBitmapRenderSurfaceAdapter(GameSurfaceCanvas canvas, int fixedWidth = 0, int fixedHeight = 0)
        : base(
            fixedWidth > 0 ? fixedWidth : Math.Max(1, (int)canvas.ActualWidth),
            fixedHeight > 0 ? fixedHeight : Math.Max(1, (int)canvas.ActualHeight))
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _fixedResolution = fixedWidth > 0 && fixedHeight > 0;

        // Only follow the control's size when the render resolution is not pinned; a pinned
        // resolution is letterboxed to fit by the canvas each paint.
        if (!_fixedResolution)
            _canvas.SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed)
            return;

        var w = (int)_canvas.ActualWidth;
        var h = (int)_canvas.ActualHeight;
        if (w > 0 && h > 0)
            SetDestinationSize(w, h);
    }

    /// <inheritdoc />
    public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
    {
        if (_disposed)
        {
            bufferImage.Dispose();
            return;
        }

        // Keep only the newest frame; queue the frame it displaces for disposal. Intermediate
        // frames are dropped rather than queued, so a slow or large present can never build an
        // unbounded backlog (which showed up as "chunky then catch up", and a freeze at full
        // screen after dragging the window larger).
        var old = Interlocked.Exchange(ref _currentImage, bufferImage);
        if (old is not null && !ReferenceEquals(old, bufferImage))
            _toDispose.Enqueue(old);
        _latestBufferRect = bufferRect;

        // Coalesce to a single in-flight present: if one is already scheduled it will pick up
        // whatever is newest when it runs, so we never enqueue more than one paint at a time.
        if (Interlocked.CompareExchange(ref _paintScheduled, 1, 0) != 0)
            return;

        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
            Paint();
        else
            dispatcherQueue.TryEnqueue(Paint);
    }

    private void Paint()
    {
        // Clear the latch first so a frame arriving during this paint schedules the next one.
        Interlocked.Exchange(ref _paintScheduled, 0);

        _canvas.SetImage(Volatile.Read(ref _currentImage), _latestBufferRect);

        while (_toDispose.TryDequeue(out var image))
            image.Dispose();
    }

    /// <summary>
    /// Releases resources held by the adapter, including any pending backbuffer images.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.SizeChanged -= OnSizeChanged;
        _currentImage?.Dispose();
        while (_toDispose.TryDequeue(out var image))
            image.Dispose();
    }
}
