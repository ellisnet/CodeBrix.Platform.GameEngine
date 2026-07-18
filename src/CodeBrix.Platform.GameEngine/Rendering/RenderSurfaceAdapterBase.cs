using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;
public abstract class RenderSurfaceAdapterBase
{
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
    public int Width { get; protected set; }

    /// <summary>
    /// Gets the current height of the render surface in pixels.
    /// </summary>
    /// <value>The height of the render surface.</value>
    public int Height { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderSurfaceAdapterBase"/> class with the specified dimensions.
    /// </summary>
    /// <param name="destWidth">The initial width of the render surface in pixels.</param>
    /// <param name="destHeight">The initial height of the render surface in pixels.</param>
    protected RenderSurfaceAdapterBase(int destWidth, int destHeight)
    {
        SetDestinationSize(destWidth, destHeight);
    }

    /// <summary>
    /// Sets the destination size of the render surface and raises the <see cref="Resized"/> event if the dimensions have changed.
    /// </summary>
    /// <param name="destWidth">The new width of the render surface in pixels.</param>
    /// <param name="destHeight">The new height of the render surface in pixels.</param>
    /// <remarks>
    /// If the specified dimensions are the same as the current dimensions, this method returns without making changes
    /// or raising the <see cref="Resized"/> event.
    /// </remarks>
    protected void SetDestinationSize(int destWidth, int destHeight)
    {
        if (destWidth == Width && destHeight == Height)
            return;

        var oldWidth = Width;
        var oldHeight = Height;

        Width = destWidth;
        Height = destHeight;
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