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
    private SKImage? _currentImage;
    private readonly ConcurrentQueue<SKImage> _toDispose = new();
    private bool _disposed;
    private int _paintScheduled;

    // True while the canvas is off the visual tree (window closing or page navigated away).
    // Set on the UI thread; read from the engine thread in Present, so volatile.
    private volatile bool _canvasUnloaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/> class.
    /// </summary>
    /// <remarks>
    /// The adapter always tracks the canvas size: that size is the presentation destination, not the
    /// render resolution. The render resolution is the host's, from
    /// <see cref="CodeBrix.Platform.GameEngine.Configuration.EngineConfiguration.RenderScale"/> or
    /// <see cref="GameSurfaceCanvas.SetRenderResolution"/>.
    /// </remarks>
    /// <param name="canvas">The <see cref="GameSurfaceCanvas"/> this adapter presents to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> is null.</exception>
    public CodeBrixPlatformBitmapRenderSurfaceAdapter(GameSurfaceCanvas canvas)
        : base(
            Math.Max(1, (int)canvas.ActualWidth),
            Math.Max(1, (int)canvas.ActualHeight),
            HasLayoutSize(canvas))
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

        // A resize changes only where and how large the frame is presented, so the adapter follows
        // the control whatever the render resolution is.
        _canvas.SizeChanged += OnSizeChanged;

        // Track whether the canvas is in the visual tree; Present stops scheduling paints
        // while it is not (see the comment there).
        _canvas.Unloaded += OnCanvasUnloaded;
        _canvas.Loaded += OnCanvasLoaded;
    }

    // A canvas created but not laid out yet reports a zero size: the adapter then starts on a
    // placeholder, and the first real layout establishes the logical render resolution once.
    private static bool HasLayoutSize(GameSurfaceCanvas canvas)
        => canvas is not null && canvas.ActualWidth >= 1 && canvas.ActualHeight >= 1;

    private void OnCanvasUnloaded(object sender, RoutedEventArgs e) => _canvasUnloaded = true;

    private void OnCanvasLoaded(object sender, RoutedEventArgs e) => _canvasUnloaded = false;

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
    /// <remarks>
    /// The canvas repaints its whole surface on every paint, so this adapter always hands it the
    /// complete frame and lets it draw through the shared presentation transform; the dirty
    /// <paramref name="bufferRect"/> and its mapped <paramref name="destRect"/> would describe a
    /// partial blit this control cannot do.
    /// </remarks>
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

        // While the canvas is off the visual tree (window closing or page navigated away), keep
        // caching the newest frame (the pause snapshot still reads it) but schedule no paints:
        // the engine may well keep presenting, and continuously posting to the dispatcher after
        // the last window closes can keep a head's message loop from ever draining its queue
        // and exiting (observed as a zombie process on the Win32-Skia head).
        if (_canvasUnloaded)
            return;

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

        _canvas.SetImage(Volatile.Read(ref _currentImage));

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
        _canvas.Unloaded -= OnCanvasUnloaded;
        _canvas.Loaded -= OnCanvasLoaded;
        _currentImage?.Dispose();
        while (_toDispose.TryDequeue(out var image))
            image.Dispose();
    }
}
