using CodeBrix.Platform.GameEngine.Effects;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;
/// <summary>
/// Represents a base class for hosting a render surface, providing functionality for managing rendering operations,
/// backbuffer access, and integration with platform-specific adapters.
/// </summary>
public abstract class RenderSurfaceHostBase : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderSurfaceHostBase"/> class and registers it
    /// with the <see cref="RenderSurfaceHostRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Registration ensures the render surface host is tracked for lifecycle management and can be
    /// enumerated by other system components. The host's <see cref="Effects"/> manager is created
    /// here, so every host has one from the moment it exists.
    /// </remarks>
    protected RenderSurfaceHostBase()
    {
        Effects = new EffectsManager(this);
        RenderSurfaceHostRegistry.Register(this);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="RenderSurfaceHostBase"/> class, ensuring resources
    /// are released when the object is garbage collected.
    /// </summary>
    ~RenderSurfaceHostBase() => Dispose(false);

    /// <summary>
    /// Gets the in-memory <see cref="BackbufferBase"/> associated with the current rendering context.
    /// </summary>
    public abstract BackbufferBase Backbuffer { get; }

    /// <summary>
    /// Gets the scale at which this surface's logical <see cref="Backbuffer"/> image is currently
    /// fitted into its render surface adapter.
    /// </summary>
    /// <value>
    /// The presentation scale, or <c>0</c> when there is no adapter or nothing can be presented
    /// yet. Read-only: the logical resolution follows
    /// <see cref="Configuration.EngineConfiguration.RenderScale"/> and
    /// <see cref="RequestRenderResolution"/>, never the size of the surface.
    /// </value>
    public float PresentationScale => RenderSurfaceAdapter?.PresentationScale ?? 0f;

    /// <summary>
    /// Gets the width, in logical Backbuffer ScreenPx, that this surface renders at — including a
    /// resolution change that has been requested but not yet applied by the rendering thread.
    /// </summary>
    /// <value>
    /// The established logical width. This is what views are sized from, so game code can lay out
    /// in the new resolution in the same breath as requesting it.
    /// </value>
    public virtual int LogicalWidth => Backbuffer.Width;

    /// <summary>
    /// Gets the height, in logical Backbuffer ScreenPx, that this surface renders at — including a
    /// resolution change that has been requested but not yet applied by the rendering thread.
    /// </summary>
    /// <value>The established logical height; the counterpart of <see cref="LogicalWidth"/>.</value>
    public virtual int LogicalHeight => Backbuffer.Height;

    /// <summary>
    /// Gets or sets a value indicating whether this surface's logical render resolution follows the
    /// size of its render surface adapter.
    /// </summary>
    /// <value>
    /// <see langword="false"/> by default: the resolution is established once and every later
    /// adapter resize changes presentation only (the image is fitted and centred). When
    /// <see langword="true"/>, every adapter resize re-establishes the resolution from the new
    /// adapter size and <see cref="Configuration.EngineConfiguration.RenderScale"/> — which
    /// reallocates the backbuffer, rescales views, and forces a full redraw on each resize.
    /// </value>
    /// <remarks>
    /// This opt-in is specific to this engine port; upstream always keeps the established
    /// resolution. Use it for a surface that is meant to expose more of the world as its window
    /// grows. It supersedes an explicit <see cref="RequestRenderResolution"/> on the next resize,
    /// so use one approach or the other for a given surface.
    /// </remarks>
    public bool TrackAdapterSize { get; set; }

    /// <summary>
    /// Establishes an explicit logical render resolution for this surface, independent of
    /// <see cref="Configuration.EngineConfiguration.RenderScale"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request is applied on the thread that owns this surface's rendering, and the adapter
    /// then fits and centres that image at whatever size the surface happens to be. A request made
    /// while the adapter has no usable size (a minimized or not-yet-laid-out window) is deferred
    /// until it has one.
    /// </para>
    /// <para>
    /// This is specific to this engine port — it is how a host pins a surface to a fixed
    /// resolution, for example 1280x720 regardless of window size.
    /// <see cref="Configuration.EngineConfiguration.RenderScale"/> is the upstream way, and a later
    /// change to it supersedes the pinned resolution.
    /// </para>
    /// </remarks>
    /// <param name="width">The logical Backbuffer width in pixels; at least <c>1</c>.</param>
    /// <param name="height">The logical Backbuffer height in pixels; at least <c>1</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is less than <c>1</c>.
    /// </exception>
    public virtual void RequestRenderResolution(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
    }

    internal virtual void RequestRenderScale(float scale) { }

    internal virtual void InvalidatePresentation() { }

    /// <summary>
    /// Gets the source <see cref="Scenes.Scene"/> used for rendering operations.
    /// </summary>
    public abstract Scene Scene { get; }

    /// <summary>
    /// Gets the platform-specific <see cref="RenderSurfaceAdapterBase"/> responsible
    /// for rendering the image from the <see cref="Backbuffer"/>.
    /// </summary>
    public abstract RenderSurfaceAdapterBase? RenderSurfaceAdapter { get; }

    /// <summary>
    /// Gets the view manager that controls camera positions, viewports, and multi-view rendering
    /// for this render surface host.
    /// </summary>
    /// <value>
    /// The <see cref="ViewManager"/> instance managing all views associated with this render surface host.
    /// </value>
    /// <remarks>
    /// The view manager enables split-screen, picture-in-picture, and minimap rendering by managing
    /// multiple views with independent cameras and viewports.
    /// </remarks>
    public abstract ViewManager ViewManager { get; }

    /// <summary>
    /// Gets the manager that owns presentation effects for this render surface.
    /// </summary>
    /// <value>
    /// The <see cref="EffectsManager"/> that runs fades, slides, wipes, zooms, and camera shake
    /// against the views this host owns and the layers of the scene it is bound to.
    /// </value>
    /// <remarks>
    /// Effects change presentation state only; they never move world objects, alter collision
    /// geometry, or change a scene layer's origin.
    /// </remarks>
    public EffectsManager Effects { get; }

    /// <summary>
    /// The frame the viewer was seeing at the moment the global engine pause
    /// (<see cref="Engine.Pause"/>) took effect: an immutable snapshot of this surface's
    /// backbuffer, captured before the <see cref="Engine.Paused"/> event is raised — so a
    /// pause handler can, for example, display a dimmed version of it as a pause screen.
    /// For GL-thread-rendered (GPU) surfaces the capture is the adapter's copy of its most
    /// recently presented frame (see
    /// <see cref="RenderSurfaceAdapterBase.CaptureLatestPresentedFrame"/>). <c>null</c> until
    /// the first pause, or when a GPU surface has not presented a frame yet. The image is
    /// owned by the engine and remains valid until the next <see cref="Engine.Pause"/>
    /// capture; copy it to keep it longer.
    /// </summary>
    public SKImage? LastFrameBeforePause { get; internal set; }

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
    /// Renders the current scene frame on the GL thread and returns a snapshot of the GPU backbuffer
    /// ready to be drawn to the window surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is intended to be called from <c>SKGLControl.PaintSurface</c> (the GL thread)
    /// by a <c>WinFormGpuRenderSurfaceAdapter</c> that has been set as the host's adapter.
    /// It is the Option-A equivalent of the engine loop's <c>RenderToBackbuffer</c> +
    /// <c>PresentBackbufferToAdapter</c> pair; both operations happen synchronously on the GL
    /// thread, with no cross-thread posting.
    /// </para>
    /// <para>
    /// The returned <see cref="SKImage"/> is a lightweight GPU-backed view of the backbuffer
    /// texture.  The caller <strong>must dispose</strong> it after drawing (typically with a
    /// <c>using</c> statement), and must do so within the same <c>PaintSurface</c> call to avoid
    /// aliasing with the next frame's rendering pass.
    /// </para>
    /// <para>
    /// Returns <see langword="null"/> when <see cref="BackbufferBase.IsGlThreadRendered"/> is
    /// <see langword="false"/> (i.e. this is not a GPU-rendered surface).
    /// </para>
    /// </remarks>
    /// <param name="renderWhilePaused">
    /// <see langword="true"/> to render even while the engine is globally paused — used by the
    /// adapter's <see cref="RenderSurfaceAdapterBase.PresentPausedFrame"/> path to produce the one
    /// post-<see cref="Engine.Paused"/> frame that makes pause-screen scene changes visible.
    /// The default (<see langword="false"/>) returns <see langword="null"/> while paused, so the
    /// window keeps showing the last presented frame.
    /// </param>
    /// <returns>
    /// A GPU-backed <see cref="SKImage"/> snapshot of the rendered frame, or
    /// <see langword="null"/> if this surface does not use GL-thread rendering.
    /// </returns>
    public SKImage? GlRenderAndSnapshot(bool renderWhilePaused = false)
    {
        if (!Backbuffer.IsGlThreadRendered)
            return null;

        // The global engine pause halts GL-thread rendering too: no new frame is produced,
        // so the window keeps showing the last presented one. The single exception is the
        // adapter-driven paused-overlay frame (renderWhilePaused), which runs after the Paused
        // event while the engine is quiescent.
        if (Engine.Instance.IsPaused && !renderWhilePaused)
            return null;

        var tick = CodeBrix.Platform.GameEngine.Timers.HighResTimer.GetCurrentTick();

        RenderToBackbuffer(tick);
        Backbuffer.EndFrame();

        var img = Backbuffer.Snapshot();

        Backbuffer.BeginFrame();

        return img;
    }

    /// <summary>
    /// Renders the current scene frame and draws the GPU backbuffer surface directly to another
    /// GPU canvas without creating an intermediate <see cref="SKImage"/> snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call only from the active GPU paint callback while both surfaces share the current
    /// <c>GRContext</c>. The surface is drawn through the adapter's aspect-preserving presentation
    /// transform, so a surface larger or smaller than the logical backbuffer is fitted and centred.
    /// Linear scaling uses a scoped GPU texture snapshot — a texture view, not a readback — because
    /// Skia's direct surface drawing does not expose sampling options.
    /// </para>
    /// <para>
    /// While the engine is globally paused (<see cref="Engine.Pause"/>) no new scene frame is
    /// rendered: the surface that was last rendered is drawn instead, so a presentation loop that
    /// keeps running during a pause cannot advance the scene. Pass <paramref name="renderWhilePaused"/>
    /// to override that, matching <see cref="GlRenderAndSnapshot"/>.
    /// </para>
    /// </remarks>
    /// <param name="destinationCanvas">The active platform GPU canvas.</param>
    /// <param name="renderWhilePaused">
    /// <see langword="true"/> to render a new frame even while the engine is globally paused — used
    /// by the adapter's paused-overlay path to produce the one post-<see cref="Engine.Paused"/>
    /// frame that makes pause-screen scene changes visible. The default (<see langword="false"/>)
    /// re-presents the current surface while paused.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a GPU surface was drawn to <paramref name="destinationCanvas"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="destinationCanvas"/> is <see langword="null"/>.
    /// </exception>
    public bool GlRenderToCanvas(SKCanvas destinationCanvas, bool renderWhilePaused = false)
    {
        ArgumentNullException.ThrowIfNull(destinationCanvas);

        if (!Backbuffer.IsGlThreadRendered)
            return false;

        // The global engine pause halts GL-thread rendering too: no new frame is produced, so the
        // presenter re-draws the surface it last rendered. The single exception is the
        // adapter-driven paused-overlay frame (renderWhilePaused).
        if (Engine.Instance.IsPaused && !renderWhilePaused)
            return GlDrawCurrentFrameToCanvas(destinationCanvas);

        var tick = CodeBrix.Platform.GameEngine.Timers.HighResTimer.GetCurrentTick();

        RenderToBackbuffer(tick);
        Backbuffer.EndFrame();

        try
        {
            var surface = Backbuffer.Canvas.Surface;

            if (surface is null)
                return false;

            DrawCurrentSurface(destinationCanvas);
            return true;
        }
        finally
        {
            Backbuffer.BeginFrame();
        }
    }

    /// <summary>
    /// Draws the existing GPU backbuffer surface directly to another GPU canvas without rendering
    /// a new scene frame.
    /// </summary>
    /// <remarks>
    /// Call only from the active GPU paint callback while both surfaces share the current
    /// <c>GRContext</c>. This is intended for presentation loops that run more frequently than the
    /// engine's configured foreground/render cadence. The surface is drawn through the adapter's
    /// aspect-preserving presentation transform; linear scaling uses a scoped GPU texture snapshot
    /// without transferring any pixels to the CPU.
    /// </remarks>
    /// <param name="destinationCanvas">The active platform GPU canvas.</param>
    /// <returns>
    /// <see langword="true"/> when the current GPU surface was drawn; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="destinationCanvas"/> is <see langword="null"/>.
    /// </exception>
    public bool GlDrawCurrentFrameToCanvas(SKCanvas destinationCanvas)
    {
        ArgumentNullException.ThrowIfNull(destinationCanvas);

        if (!Backbuffer.IsGlThreadRendered)
            return false;

        var surface = Backbuffer.Canvas.Surface;

        if (surface is null)
            return false;

        DrawCurrentSurface(destinationCanvas);
        return true;
    }

    /// <summary>
    /// Draws the backbuffer's current surface onto <paramref name="canvas"/> through the adapter's
    /// presentation transform, clearing the margins first.
    /// </summary>
    /// <param name="canvas">The destination GPU canvas.</param>
    private void DrawCurrentSurface(SKCanvas canvas)
    {
        var presentation = RenderSurfaceAdapter?.Presentation ?? default;

        canvas.Clear(Backbuffer.ClearColor);

        if (presentation.Scale <= 0)
            return;

        canvas.Save();

        try
        {
            canvas.Translate(presentation.DestinationRect.Left, presentation.DestinationRect.Top);
            canvas.Scale(presentation.Scale);

            bool needsLinearSampling =
                Engine.Instance.Configuration.RenderScalingFilter == RenderScalingFilter.Linear &&
                (presentation.Scale != 1f ||
                 presentation.DestinationRect.Left != MathF.Floor(presentation.DestinationRect.Left) ||
                 presentation.DestinationRect.Top != MathF.Floor(presentation.DestinationRect.Top));

            if (needsLinearSampling)
            {
                // Skia's DrawSurface does not expose sampling and ignores paint filtering.
                // A scoped snapshot is a GPU texture view, not a readback or an intermediate copy.
                using var image = Backbuffer.Snapshot();
                canvas.DrawImage(image, 0, 0, RenderSurfaceAdapterBase.PresentationSampling);
            }
            else
            {
                canvas.DrawSurface(Backbuffer.Canvas.Surface, 0, 0);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// Returns a snapshot of the current GPU backbuffer without rendering a new scene frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is intended for GPU presentation loops that run more frequently than the engine's
    /// configured foreground/render cadence. It allows the platform surface to re-blit the most
    /// recently rendered backbuffer while preserving
    /// <see cref="Configuration.EngineConfiguration.TargetFPS"/>.
    /// </para>
    /// <para>
    /// Call only from the active GPU paint callback while the backbuffer's <c>GRContext</c> is
    /// current. The returned <see cref="SKImage"/> must be disposed before that callback returns.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A GPU-backed snapshot of the current backbuffer, or <see langword="null"/> when the surface
    /// does not use GL-thread rendering.
    /// </returns>
    public SKImage? GlSnapshotCurrentFrame()
    {
        if (!Backbuffer.IsGlThreadRendered)
            return null;

        return Backbuffer.Snapshot();
    }

    /// <summary>
    /// Renders all visible scene layers for every configured view onto the backbuffer.
    /// Called as part of DoForegroundTasks().
    /// </summary>
    internal abstract void RenderToBackbuffer(long tick);

    /// <summary>
    /// Runs as part of DoForegroundTasks(). This renders the DirtyRectangle
    /// area of the backbuffer to the adapter.
    /// </summary>
    internal abstract void PresentBackbufferToAdapter();

    /// <summary>
    /// Releases all resources used by this <see cref="RenderSurfaceHostBase"/> instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method unregisters the render surface host from the <see cref="RenderSurfaceHostRegistry"/>
    /// and releases any managed resources. Derived classes should override <see cref="Dispose(bool)"/>
    /// to release additional resources specific to their implementation.
    /// </para>
    /// <para>
    /// After calling <see cref="Dispose()"/>, this instance should not be used. Calling
    /// <see cref="Dispose()"/> multiple times is safe and has no additional effect.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by this <see cref="RenderSurfaceHostBase"/> instance and unregisters
    /// it from the <see cref="RenderSurfaceHostRegistry"/>.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources;
    /// <see langword="false"/> to release only unmanaged resources (called from finalizer).
    /// </param>
    /// <remarks>
    /// This method always unregisters the instance from the registry, and disposes the
    /// <see cref="Effects"/> manager when called from <see cref="Dispose()"/> — which cancels every
    /// running effect and restores the presentation state it captured. Derived classes should
    /// override this method to release additional resources but must call the base implementation
    /// to ensure proper unregistration.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Effects.Dispose();

        RenderSurfaceHostRegistry.Unregister(this);
    }
}
