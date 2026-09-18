using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;
public abstract class RenderSurfaceAdapterBase
{
    private sealed class PresentationState(int bufferWidth, int bufferHeight, int adapterWidth, int adapterHeight)
    {
        internal readonly int BufferWidth = bufferWidth, BufferHeight = bufferHeight;
        internal readonly int AdapterWidth = adapterWidth, AdapterHeight = adapterHeight;

        internal readonly PresentationTransform Transform =
            PresentationTransform.Fit(bufferWidth, bufferHeight, adapterWidth, adapterHeight);
    }

    private readonly object _presentationSync = new();
    private PresentationState _presentation = new(1, 1, 0, 0);

    /// <summary>
    /// Gets a value indicating whether this adapter has had a usable layout size yet. It is
    /// <see langword="false"/> for an adapter constructed before its first layout pass (and while
    /// its size is reported as zero); the first valid size establishes the logical resolution once.
    /// </summary>
    internal bool InitialSizeAvailable { get; private set; }

    /// <summary>
    /// Gets the current immutable, aspect-preserving mapping of logical Backbuffer ScreenPx to the
    /// pixels of this adapter.
    /// </summary>
    /// <value>
    /// The transform used both to present the finished image and to normalize pointer input; a
    /// default transform (<see cref="PresentationTransform.Scale"/> of zero) while nothing can be
    /// presented.
    /// </value>
    public PresentationTransform Presentation => Volatile.Read(ref _presentation).Transform;

    /// <summary>
    /// Gets the scale currently applied when presenting the logical Backbuffer image on this
    /// adapter. Read-only: resizing the adapter changes presentation, never the Backbuffer.
    /// </summary>
    /// <value>The presentation scale, or zero while nothing can be presented.</value>
    public float PresentationScale => Presentation.Scale;

    internal void SetBackbufferSize(int width, int height)
    {
        lock (_presentationSync)
        {
            var state = _presentation;
            Volatile.Write(ref _presentation, new(width, height, state.AdapterWidth, state.AdapterHeight));
        }
    }

    /// <summary>
    /// Converts a position in adapter pixels to logical Backbuffer ScreenPx, the space every
    /// engine input API works in.
    /// </summary>
    /// <remarks>
    /// Positions outside the presented image keep their outside coordinates (they are not clamped)
    /// so a captured pointer, a drag, and a leave notification stay routable.
    /// </remarks>
    /// <param name="adapterPx">The position in adapter pixels.</param>
    /// <returns>The position in logical Backbuffer ScreenPx.</returns>
    public Point AdapterPxToScreenPx(PointF adapterPx)
    {
        Presentation.TryAdapterPxToScreenPx(adapterPx, out var screen);

        // Floor is essential: a fractional negative margin must not truncate to the edge pixel zero.
        return new Point((int)Math.Floor(screen.X), (int)Math.Floor(screen.Y));
    }

    /// <summary>
    /// Presents a complete Backbuffer image on this adapter's canvas, using the same transform as
    /// pointer normalization, after clearing the margins.
    /// </summary>
    /// <param name="canvas">The adapter canvas to draw on.</param>
    /// <param name="image">The complete logical Backbuffer image.</param>
    /// <param name="clearColor">The color the letterbox or pillarbox margins are cleared to.</param>
    public void DrawImage(SKCanvas canvas, SKImage image, SKColor clearColor)
    {
        var state = Volatile.Read(ref _presentation);

        // A queued CPU snapshot may precede an explicit resolution change. Fit that complete
        // image using the same authoritative calculation until its replacement arrives.
        var transform = image.Width == state.BufferWidth && image.Height == state.BufferHeight
            ? state.Transform
            : PresentationTransform.Fit(image.Width, image.Height, state.AdapterWidth, state.AdapterHeight);

        canvas.Clear(clearColor);

        if (transform.Scale <= 0)
            return;

        canvas.DrawImage(image, transform.DestinationRect, PresentationSampling);
    }

    internal static SKSamplingOptions PresentationSampling => new(
        Engine.Instance.Configuration.RenderScalingFilter == RenderScalingFilter.NearestNeighbor
            ? SKFilterMode.Nearest
            : SKFilterMode.Linear);

    /// <summary>
    /// Occurs when the render surface adapter is resized.
    /// </summary>
    /// <remarks>
    /// This event is raised when the <see cref="Width"/> or <see cref="Height"/> properties change,
    /// providing both the old and new dimensions in the event arguments.
    /// </remarks>
    public event Action<RenderSurfaceAdapterResizedEventArgs>? Resized;

    /// <summary>
    /// Gets the current width of the render surface in pixels.
    /// </summary>
    /// <value>The width of the render surface.</value>
    public int Width
    {
        get => Volatile.Read(ref _presentation).AdapterWidth;
        protected set => SetDestinationSize(value, Height);
    }

    /// <summary>
    /// Gets the current height of the render surface in pixels.
    /// </summary>
    /// <value>The height of the render surface.</value>
    public int Height
    {
        get => Volatile.Read(ref _presentation).AdapterHeight;
        protected set => SetDestinationSize(Width, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderSurfaceAdapterBase"/> class with the specified dimensions.
    /// </summary>
    /// <param name="destWidth">The initial width of the render surface in pixels.</param>
    /// <param name="destHeight">The initial height of the render surface in pixels.</param>
    protected RenderSurfaceAdapterBase(int destWidth, int destHeight)
        : this(destWidth, destHeight, initialSizeAvailable: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderSurfaceAdapterBase"/> class whose initial
    /// layout may still be pending.
    /// </summary>
    /// <param name="destWidth">The initial — or placeholder — width of the render surface in pixels.</param>
    /// <param name="destHeight">The initial — or placeholder — height of the render surface in pixels.</param>
    /// <param name="initialSizeAvailable">
    /// <see langword="false"/> for a surface constructed before its first layout pass; the first
    /// valid size then establishes the logical render resolution, once.
    /// </param>
    protected RenderSurfaceAdapterBase(int destWidth, int destHeight, bool initialSizeAvailable)
    {
        SetDestinationSize(destWidth, destHeight);
        InitialSizeAvailable = initialSizeAvailable;
    }

    /// <summary>
    /// Sets the destination size of the render surface and raises the <see cref="Resized"/> event if the dimensions have changed.
    /// </summary>
    /// <param name="destWidth">The new width of the render surface in pixels.</param>
    /// <param name="destHeight">The new height of the render surface in pixels.</param>
    /// <remarks>
    /// If the dimensions and the availability of a usable size are both unchanged, this method
    /// returns without raising the <see cref="Resized"/> event. The first valid layout raises the
    /// event even when its dimensions match the placeholder the adapter was constructed with.
    /// </remarks>
    protected void SetDestinationSize(int destWidth, int destHeight)
    {
        var sizeAvailable = destWidth > 0 && destHeight > 0;

        if (destWidth == Width && destHeight == Height && InitialSizeAvailable == sizeAvailable)
            return;

        var oldWidth = Width;
        var oldHeight = Height;

        InitialSizeAvailable = sizeAvailable;

        lock (_presentationSync)
        {
            var state = _presentation;
            Volatile.Write(ref _presentation, new(state.BufferWidth, state.BufferHeight, destWidth, destHeight));
        }

        Resized?.Invoke(new RenderSurfaceAdapterResizedEventArgs(this, oldWidth, oldHeight, Width, Height));
    }

    /// <summary>
    /// Presents the specified portion of the Backbuffer image to the destination rectangle on the RenderSurfaceAdapter.
    /// </summary>
    /// <remarks>The method maps the specified region of the buffer image to the destination rectangle,
    /// scaling or transforming as necessary. Callers must ensure that the dimensions and coordinates of <paramref
    /// name="bufferRect"/> and <paramref name="destRect"/> are valid.</remarks>
    /// <param name="bufferImage">The source image from which to present. Cannot be <see langword="null"/>.</param>
    /// <param name="bufferRect">The rectangular region of the buffer image to present. Coordinates are in the buffer image's space.</param>
    /// <param name="destRect">The rectangular region in the destination space where the presented content will be drawn.</param>
    public abstract void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect);

    /// <summary>
    /// Returns an independent CPU copy of the most recent frame this adapter presented, or
    /// <see langword="null"/> when the adapter keeps no presented-frame copy (the base
    /// implementation). The engine calls this during <see cref="Engine.Pause"/> for
    /// GL-thread-rendered (GPU) surfaces — whose backbuffers cannot be snapshotted off the GL
    /// thread — so <see cref="RenderSurfaceHostBase.LastFrameBeforePause"/> can be captured for
    /// them too. The returned image's ownership transfers to the caller, which is responsible
    /// for disposing it; implementations must therefore return a copy that stays valid after
    /// the adapter presents its next frame.
    /// </summary>
    /// <returns>A caller-owned copy of the latest presented frame, or <see langword="null"/>.</returns>
    public virtual SKImage? CaptureLatestPresentedFrame() => null;

    /// <summary>
    /// Requests that this adapter render and present one frame for a surface whose rendering it
    /// drives itself (a GL-thread-rendered surface), while the engine is paused. The engine posts
    /// this to the UI thread at the end of the pause transition — after the
    /// <see cref="Engine.Paused"/> event handlers have run — so scene changes those handlers made
    /// (for example, a pause overlay) become visible, matching the final-frame behaviour CPU
    /// surfaces get from the engine's own pause path. The base implementation does nothing;
    /// adapters that do not drive their own rendering never need this.
    /// </summary>
    /// <param name="host">The render-surface host to render the paused frame for.</param>
    public virtual void PresentPausedFrame(RenderSurfaceHostBase host) { }
}