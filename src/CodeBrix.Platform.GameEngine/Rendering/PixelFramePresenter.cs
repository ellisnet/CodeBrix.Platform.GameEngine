using System;
using System.Runtime.InteropServices;
using System.Threading;
using CodeBrix.Platform.GameEngine.Drawing;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Rendering; //CodeBrix (not from Gondwana)

/// <summary>
/// The engine's presentation path for software-rendered (framebuffer-style) games: the game
/// renders whole frames into its own CPU buffer and hands each one to
/// <see cref="PresentFrame(ReadOnlySpan{byte})"/>; the presenter shows the latest frame on
/// the output surface, scaled and oriented per <see cref="Configure"/>. It deliberately
/// bypasses the engine's scene/sprite pipeline — no backbuffer, no dirty rects, no snapshot
/// machinery.
/// </summary>
/// <remarks>
/// <para>
/// THREADING: <see cref="PresentFrame(ReadOnlySpan{byte})"/> may be called from any thread
/// (typically the game's own fixed-rate loop), once per tic. Presentation is
/// latest-frame-wins: frames presented faster than the surface repaints coalesce, and the
/// newest complete frame is always the one shown. Internally a triple buffer guarantees a
/// paint never reads a half-written frame. <see cref="Configure"/> must not run concurrently
/// with <see cref="PresentFrame(ReadOnlySpan{byte})"/> — call both from the game thread.
/// </para>
/// <para>
/// COST: one full-frame copy per presented frame (into a pinned buffer wrapped zero-copy by
/// an <see cref="SKImage"/>) and zero per-frame managed allocations.
/// </para>
/// <para>
/// Host implementations connect <see cref="RequestPaint"/> to their surface invalidation and
/// call <see cref="DrawCurrentFrame"/> from their paint handler.
/// </para>
/// </remarks>
public abstract class PixelFramePresenter : IDisposable
{
    private const int MailboxIndexMask = 0xFF;
    private const int MailboxNewFrameFlag = 0x100;

    private static readonly object _registryGate = new();
    private static readonly System.Collections.Generic.List<WeakReference<PixelFramePresenter>> _registry = new();

    private readonly object _paintGate = new();

    private FrameSlot?[] _slots = new FrameSlot?[3];
    private int _backIndex;
    private int _frontIndex;
    private int _mailbox;
    private volatile bool _hasFrame;
    private int _frameByteCount;
    private bool _isDisposed;

    private float _lastSurfaceWidth;
    private float _lastSurfaceHeight;

    /// <summary>The logical frame width in pixels (as the viewer sees it); 0 until configured.</summary>
    public int FrameWidth { get; private set; }

    /// <summary>The logical frame height in pixels (as the viewer sees it); 0 until configured.</summary>
    public int FrameHeight { get; private set; }

    /// <summary>The configured pixel layout.</summary>
    public PixelBufferFormat Format { get; private set; }

    /// <summary>The configured memory orientation.</summary>
    public FrameOrientation Orientation { get; private set; }

    /// <summary>The configured scale mode.</summary>
    public PixelFrameScaleMode ScaleMode { get; private set; }

    /// <summary>
    /// The sampling quality used when scaling frames to the surface. Default
    /// <see cref="ImageFilterQuality.None"/> (nearest-neighbor — the right choice for
    /// pixel-art content).
    /// </summary>
    public ImageFilterQuality FilterQuality { get; private set; }

    /// <summary>True after a successful <see cref="Configure"/> call.</summary>
    public bool IsConfigured => FrameWidth > 0;

    /// <summary>
    /// The frame the viewer was seeing at the moment the global engine pause
    /// (<see cref="Engine.Pause"/>) took effect: a stable, screen-oriented copy of the newest
    /// presented frame, captured before the <see cref="Engine.Paused"/> event is raised — so
    /// a pause handler can, for example, present a dimmed version of it as a pause screen.
    /// <c>null</c> until the first pause, or when no frame had been presented yet. The image
    /// is owned by the presenter and remains valid until the next <see cref="Engine.Pause"/>
    /// capture (or this presenter's disposal); copy it to keep it longer.
    /// </summary>
    public SKImage? LastFrameBeforePause { get; private set; }

    /// <summary>
    /// Returns <see cref="LastFrameBeforePause"/> as a raw RGBA8888 bitmap (4 bytes per
    /// pixel in R,G,B,A memory order, row-major, unpremultiplied alpha) — the Skia-free
    /// shape imaging libraries load directly. See
    /// <see cref="Engine.LastFrameBeforePauseAsRgba"/> for the usage pattern.
    /// </summary>
    /// <param name="width">The bitmap width in pixels; 0 when the result is <c>null</c>.</param>
    /// <param name="height">The bitmap height in pixels; 0 when the result is <c>null</c>.</param>
    /// <returns>The RGBA8888 pixel bytes, or <c>null</c> when no frame has been captured.</returns>
    public byte[]? LastFrameBeforePauseAsRgba(out int width, out int height)
        => RgbaPixelExport.FromImage(LastFrameBeforePause, out width, out height);

    /// <summary>
    /// Creates the presenter and registers it for last-frame capture by the global engine
    /// pause. Presenters are tracked weakly; disposal does not need to unregister.
    /// </summary>
    protected PixelFramePresenter()
    {
        lock (_registryGate)
        {
            _registry.RemoveAll(reference => !reference.TryGetTarget(out _));
            _registry.Add(new WeakReference<PixelFramePresenter>(this));
        }
    }

    /// <summary>
    /// Configures (or reconfigures) the presenter for frames of the given logical size and
    /// layout. Callable again at any time from the game thread — e.g. to switch between
    /// 320x200 and 640x400.
    /// </summary>
    /// <param name="width">The logical frame width in pixels.</param>
    /// <param name="height">The logical frame height in pixels.</param>
    /// <param name="format">The in-memory pixel layout of presented frames.</param>
    /// <param name="orientation">
    /// Row-major (<see cref="FrameOrientation.Identity"/>) or column-major
    /// (<see cref="FrameOrientation.Rotate90"/>) memory layout.
    /// </param>
    /// <param name="scaleMode">How frames scale to the surface.</param>
    /// <param name="filterQuality">The sampling quality used for the scaled draw.</param>
    public void Configure(
        int width,
        int height,
        PixelBufferFormat format,
        FrameOrientation orientation = FrameOrientation.Identity,
        PixelFrameScaleMode scaleMode = PixelFrameScaleMode.Fit,
        ImageFilterQuality filterQuality = ImageFilterQuality.None)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The frame width must be positive.");
        }
        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The frame height must be positive.");
        }

        lock (_paintGate)
        {
            DisposeSlots();

            FrameWidth = width;
            FrameHeight = height;
            Format = format;
            Orientation = orientation;
            ScaleMode = scaleMode;
            FilterQuality = filterQuality;
            _frameByteCount = width * height * 4;

            // The wrapped image's row-major dimensions: a column-major buffer of logical
            // size W x H is, in memory, an H-wide, W-tall row-major image.
            var (imageWidth, imageHeight) = orientation == FrameOrientation.Rotate90
                ? (height, width)
                : (width, height);
            var colorType = format == PixelBufferFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888;
            var info = new SKImageInfo(imageWidth, imageHeight, colorType, SKAlphaType.Opaque);

            var slots = new FrameSlot?[3];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = new FrameSlot(info);
            }

            _slots = slots;
            _backIndex = 0;
            _frontIndex = 1;
            _mailbox = 2;
            _hasFrame = false;
        }
    }

    /// <summary>
    /// Presents a complete frame: exactly <c>width * height * 4</c> bytes in the configured
    /// format and orientation. Callable from any thread; one copy, no allocations,
    /// latest-frame-wins.
    /// </summary>
    /// <param name="frame">The frame bytes.</param>
    public void PresentFrame(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!IsConfigured)
        {
            throw new InvalidOperationException($"Call {nameof(Configure)} before {nameof(PresentFrame)}.");
        }
        if (frame.Length != _frameByteCount)
        {
            throw new ArgumentException(
                $"The frame is {frame.Length} bytes but the configured {FrameWidth}x{FrameHeight} frame needs exactly {_frameByteCount}.",
                nameof(frame));
        }

        var slot = _slots[_backIndex]!;
        frame.CopyTo(slot.Bytes);

        // Mailbox handoff: publish the filled slot, take back whichever slot the mailbox
        // held. The paint side swaps with the mailbox the same way, so producer and
        // consumer never touch the same slot.
        var previous = Interlocked.Exchange(ref _mailbox, _backIndex | MailboxNewFrameFlag);
        _backIndex = previous & MailboxIndexMask;
        _hasFrame = true;

        RequestPaint();
    }

    /// <summary>
    /// Presents a complete frame given as packed 32-bit pixels (exactly
    /// <c>width * height</c> values, interpreted in memory order per the configured
    /// <see cref="PixelBufferFormat"/>). See <see cref="PresentFrame(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="frame">The frame pixels.</param>
    public void PresentFrame(uint[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        PresentFrame(MemoryMarshal.AsBytes(frame.AsSpan()));
    }

    /// <summary>
    /// Presents a complete frame given as a byte memory block. See
    /// <see cref="PresentFrame(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="frame">The frame bytes.</param>
    public void PresentFrame(ReadOnlyMemory<byte> frame) => PresentFrame(frame.Span);

    /// <summary>
    /// Draws the newest presented frame onto <paramref name="canvas"/>, scaled and oriented
    /// per the configuration. Host implementations call this from their paint handler; the
    /// surface size also feeds <see cref="WindowToBuffer"/>/<see cref="BufferToWindow"/>.
    /// Draws nothing when no frame has been presented yet.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="surfaceWidth">The surface width, in the canvas's pixel units.</param>
    /// <param name="surfaceHeight">The surface height, in the canvas's pixel units.</param>
    public void DrawCurrentFrame(SKCanvas canvas, float surfaceWidth, float surfaceHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        lock (_paintGate)
        {
            if (_isDisposed || !IsConfigured)
            {
                return;
            }

            _lastSurfaceWidth = surfaceWidth;
            _lastSurfaceHeight = surfaceHeight;

            if (!_hasFrame || surfaceWidth <= 0f || surfaceHeight <= 0f)
            {
                return;
            }

            DrawFrameCore(canvas, surfaceWidth, surfaceHeight);
        }
    }

    // The shared draw path for DrawCurrentFrame and the engine-pause capture. Caller holds
    // _paintGate and has verified a frame exists.
    private void DrawFrameCore(SKCanvas canvas, float surfaceWidth, float surfaceHeight)
    {
        // Take the newest frame out of the mailbox, if the producer put one there.
        if ((Volatile.Read(ref _mailbox) & MailboxNewFrameFlag) != 0)
        {
            var previous = Interlocked.Exchange(ref _mailbox, _frontIndex);
            _frontIndex = previous & MailboxIndexMask;
        }

        var image = _slots[_frontIndex]!.Image;
        var destination = ComputeDestinationRect(surfaceWidth, surfaceHeight);
        var sampling = FilterQuality.ToSamplingOptions();

        if (Orientation == FrameOrientation.Rotate90)
        {
            // One transformed draw maps the column-major (transposed) image to the
            // destination rect: screenX tracks the image's row axis, screenY its column
            // axis.
            var matrix = new SKMatrix(
                scaleX: 0f, skewX: destination.Width / FrameWidth, transX: destination.Left,
                skewY: destination.Height / FrameHeight, scaleY: 0f, transY: destination.Top,
                persp0: 0f, persp1: 0f, persp2: 1f);
            canvas.Save();
            canvas.Concat(in matrix);
            canvas.DrawImage(image, 0f, 0f, sampling);
            canvas.Restore();
        }
        else
        {
            var source = new SKRect(0f, 0f, image.Width, image.Height);
            canvas.DrawImage(image, source, destination, sampling);
        }
    }

    /// <summary>
    /// Captures the newest presented frame as a stable, screen-oriented image and stores it
    /// in <see cref="LastFrameBeforePause"/> (disposing the previous capture). Returns the
    /// captured image, or <c>null</c> when unconfigured, disposed, or no frame has been
    /// presented yet.
    /// </summary>
    internal SKImage? CaptureForEnginePause()
    {
        lock (_paintGate)
        {
            if (_isDisposed || !IsConfigured || !_hasFrame)
            {
                return null;
            }

            // Render at the logical frame size: every scale mode maps the frame to the full
            // rect at that size, and the orientation transform is applied for Rotate90.
            var info = new SKImageInfo(FrameWidth, FrameHeight, SKImageInfo.PlatformColorType, SKAlphaType.Opaque);
            using var surface = SKSurface.Create(info);
            if (surface is null)
            {
                return null;
            }

            surface.Canvas.Clear(SKColors.Black);
            DrawFrameCore(surface.Canvas, FrameWidth, FrameHeight);

            var capture = surface.Snapshot();
            LastFrameBeforePause?.Dispose();
            LastFrameBeforePause = capture;
            return capture;
        }
    }

    /// <summary>
    /// Captures <see cref="LastFrameBeforePause"/> on every live presenter for the global
    /// engine pause and returns the first captured image (for <see cref="Engine.LastFrameBeforePause"/>).
    /// </summary>
    internal static SKImage? CaptureAllForEnginePause()
    {
        WeakReference<PixelFramePresenter>[] snapshot;
        lock (_registryGate)
        {
            snapshot = _registry.ToArray();
        }

        SKImage? first = null;
        foreach (var reference in snapshot)
        {
            if (reference.TryGetTarget(out var presenter))
            {
                var capture = presenter.CaptureForEnginePause();
                first ??= capture;
            }
        }

        return first;
    }

    /// <summary>
    /// Converts a point in surface units (the units <see cref="DrawCurrentFrame"/> was last
    /// called with) to logical frame-buffer coordinates — closing the letterbox mouse-mapping
    /// gap for pointer aiming. Returns null before the first paint or when unconfigured; the
    /// result may lie outside 0..width/height when the point is over the letterbox bars.
    /// </summary>
    /// <param name="surfacePoint">The point in surface units.</param>
    public SKPoint? WindowToBuffer(SKPoint surfacePoint)
    {
        if (!IsConfigured || _lastSurfaceWidth <= 0f || _lastSurfaceHeight <= 0f)
        {
            return null;
        }

        var destination = ComputeDestinationRect(_lastSurfaceWidth, _lastSurfaceHeight);
        return new SKPoint(
            (surfacePoint.X - destination.Left) / destination.Width * FrameWidth,
            (surfacePoint.Y - destination.Top) / destination.Height * FrameHeight);
    }

    /// <summary>
    /// Converts logical frame-buffer coordinates to surface units (the inverse of
    /// <see cref="WindowToBuffer"/>). Returns null before the first paint or when
    /// unconfigured.
    /// </summary>
    /// <param name="bufferPoint">The point in logical frame coordinates.</param>
    public SKPoint? BufferToWindow(SKPoint bufferPoint)
    {
        if (!IsConfigured || _lastSurfaceWidth <= 0f || _lastSurfaceHeight <= 0f)
        {
            return null;
        }

        var destination = ComputeDestinationRect(_lastSurfaceWidth, _lastSurfaceHeight);
        return new SKPoint(
            destination.Left + bufferPoint.X / FrameWidth * destination.Width,
            destination.Top + bufferPoint.Y / FrameHeight * destination.Height);
    }

    /// <summary>Releases the presenter's pinned frame buffers.</summary>
    public void Dispose()
    {
        lock (_paintGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            DisposeSlots();
            LastFrameBeforePause?.Dispose();
            LastFrameBeforePause = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Implemented by hosts: schedule a repaint of the output surface (coalescing repeated
    /// requests) that ends up calling <see cref="DrawCurrentFrame"/>. Called from
    /// <see cref="PresentFrame(ReadOnlySpan{byte})"/> on the presenting thread; must not
    /// allocate per call on the steady-state path.
    /// </summary>
    protected abstract void RequestPaint();

    private SKRect ComputeDestinationRect(float surfaceWidth, float surfaceHeight)
    {
        float drawWidth;
        float drawHeight;

        switch (ScaleMode)
        {
            case PixelFrameScaleMode.Stretch:
                return new SKRect(0f, 0f, surfaceWidth, surfaceHeight);

            case PixelFrameScaleMode.Center:
                drawWidth = FrameWidth;
                drawHeight = FrameHeight;
                break;

            case PixelFrameScaleMode.PixelPerfect:
                {
                    var scale = MathF.Floor(Math.Min(surfaceWidth / FrameWidth, surfaceHeight / FrameHeight));
                    if (scale < 1f)
                    {
                        goto case PixelFrameScaleMode.Fit; // too small for 1:1 — shrink to fit
                    }
                    drawWidth = FrameWidth * scale;
                    drawHeight = FrameHeight * scale;
                    break;
                }

            case PixelFrameScaleMode.Fit:
            default:
                {
                    var scale = Math.Min(surfaceWidth / FrameWidth, surfaceHeight / FrameHeight);
                    drawWidth = FrameWidth * scale;
                    drawHeight = FrameHeight * scale;
                    break;
                }
        }

        var left = (surfaceWidth - drawWidth) * 0.5f;
        var top = (surfaceHeight - drawHeight) * 0.5f;
        return new SKRect(left, top, left + drawWidth, top + drawHeight);
    }

    private void DisposeSlots()
    {
        foreach (var slot in _slots)
        {
            slot?.Dispose();
        }

        Array.Clear(_slots);
        _hasFrame = false;
    }

    private sealed class FrameSlot : IDisposable
    {
        private GCHandle _handle;

        public FrameSlot(SKImageInfo info)
        {
            Bytes = new byte[info.BytesSize];
            _handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            // FromPixels wraps the pinned memory without copying; every PresentFrame copy
            // into Bytes is immediately visible to draws of this image.
            Image = SKImage.FromPixels(info, _handle.AddrOfPinnedObject(), info.RowBytes);
        }

        public byte[] Bytes { get; }

        public SKImage Image { get; }

        public void Dispose()
        {
            Image.Dispose();
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }
    }
}
