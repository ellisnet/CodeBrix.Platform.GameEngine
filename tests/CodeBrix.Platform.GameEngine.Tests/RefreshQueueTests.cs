using System;
using System.Drawing;
using System.Threading;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the scene-policy gate on <see cref="RefreshQueue"/>: a full-frame (GL-thread-rendered)
/// host redraws its whole viewport every paint and never consumes dirty regions, so a scene bound
/// to one must neither retain nor accept them. Bitmap hosts, and scenes that are not bound at all,
/// keep tracking dirty regions as before.
/// </summary>
public class RefreshQueueTests : IDisposable
{
    /// <summary>
    /// Initializes the fixture, making this thread the engine thread so the queue's engine-thread
    /// marshalling runs inline (no engine loop is running during the test).
    /// </summary>
    public RefreshQueueTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
    }

    /// <summary>
    /// Hands the engine-thread identity to a thread that has already exited, so later tests see
    /// the dispatcher as unbound again. (The engine rebinds it whenever the loop starts.)
    /// </summary>
    public void Dispose()
    {
        var releaser = new Thread(() => Engine.Instance.EngineDispatcher.BindToCurrentThread());
        releaser.Start();
        releaser.Join();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddWorldRect_queue_is_cleared_and_closed_when_a_gpu_host_binds_the_scene()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 4);
        layer.RefreshQueue.AddWorldRect(new Rectangle(0, 0, 16, 16));
        layer.RefreshQueue.IsDirty.Should().BeTrue();

        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);

        //Act
        host.Bind(scene);
        layer.RefreshQueue.AddWorldRect(new Rectangle(16, 0, 16, 16));

        //Assert - the pre-bind rect is gone and the post-bind one was never stored.
        scene.UsesDirtyRegionRendering.Should().BeFalse();
        layer.RefreshQueue.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void AddViewScreenRect_returns_before_validating_arguments_when_the_queue_is_closed()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 4);
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        host.Bind(scene);

        //Act - the gate has to short-circuit ahead of the null checks and the view conversion,
        //  because that conversion is the expensive half of the call.
        Action add = () => layer.RefreshQueue.AddViewScreenRect(null!, null!, new Rectangle(0, 0, 16, 16));

        //Assert
        add.Should().NotThrow();
        layer.RefreshQueue.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void AddWorldRect_keeps_accepting_rects_when_a_bitmap_host_binds_the_scene()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 4);
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene);

        //Act
        layer.RefreshQueue.AddWorldRect(new Rectangle(0, 0, 16, 16));

        //Assert
        scene.UsesDirtyRegionRendering.Should().BeTrue();
        layer.RefreshQueue.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AddWorldRect_accepts_rects_again_after_rebinding_from_a_gpu_host_to_a_bitmap_host()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 4);

        using (var gpuAdapter = new FakeRenderSurfaceAdapter())
        using (var gpuHost = new RenderSurfaceHost<GpuBackbuffer>(gpuAdapter))
        {
            gpuHost.Bind(scene);
            layer.RefreshQueue.AddWorldRect(new Rectangle(0, 0, 16, 16));
            layer.RefreshQueue.IsDirty.Should().BeFalse();
        }

        //Act
        using var adapter = new FakeRenderSurfaceAdapter();
        using var bitmapHost = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        bitmapHost.Bind(scene);
        layer.RefreshQueue.AddWorldRect(new Rectangle(16, 0, 16, 16));

        //Assert
        layer.RefreshQueue.IsDirty.Should().BeTrue();
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(320, 200) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
