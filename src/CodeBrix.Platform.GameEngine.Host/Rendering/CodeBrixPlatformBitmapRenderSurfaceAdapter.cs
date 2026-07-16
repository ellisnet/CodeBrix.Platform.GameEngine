using System;
using System.Collections.Concurrent;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using Microsoft.UI.Xaml;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Host.Rendering;

/// <summary>
/// Tier A (CPU) render-surface adapter: presents each engine frame by handing the latest
/// <see cref="SKImage"/> to a <see cref="GameSurfaceCanvas"/> and invalidating it on the UI thread.
/// This is the default, works on all CodeBrix.Platform heads, and requires no GPU interop.
/// </summary>
public sealed class CodeBrixPlatformBitmapRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
{
    private readonly GameSurfaceCanvas _canvas;
    private SKImage? _currentImage;
    private readonly ConcurrentQueue<SKImage> _toDispose = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformBitmapRenderSurfaceAdapter"/> class.
    /// </summary>
    /// <param name="canvas">The <see cref="GameSurfaceCanvas"/> this adapter presents to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canvas"/> is null.</exception>
    public CodeBrixPlatformBitmapRenderSurfaceAdapter(GameSurfaceCanvas canvas)
        : base(Math.Max(1, (int)canvas.ActualWidth), Math.Max(1, (int)canvas.ActualHeight))
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
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

        var old = Interlocked.Exchange(ref _currentImage, bufferImage);
        if (old is not null && !ReferenceEquals(old, bufferImage))
            _toDispose.Enqueue(old);

        var dispatcherQueue = _canvas.DispatcherQueue;

        void Paint()
        {
            _canvas.SetImage(_currentImage, bufferRect);
            _canvas.Invalidate();
            while (_toDispose.TryDequeue(out var image))
                image.Dispose();
        }

        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
            Paint();
        else
            dispatcherQueue.TryEnqueue(Paint);
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
