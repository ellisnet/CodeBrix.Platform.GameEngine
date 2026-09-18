using System;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;


namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the one-render-surface-host-per-scene binding rule: a scene is claimed by the host it is
/// bound to, a failed <see cref="RenderSurfaceHost{TBackbuffer}.Bind"/> leaves both hosts on the
/// scenes they already had, and both host disposal and scene disposal release the claim.
/// </summary>
public class RenderSurfaceHostTests
{
    [Fact]
    public void Bind_throws_when_the_scene_belongs_to_another_host_and_changes_neither_host()
    {
        //Arrange
        using var scene = new Scene();
        using var firstAdapter = new FakeRenderSurfaceAdapter();
        using var secondAdapter = new FakeRenderSurfaceAdapter();
        using var firstHost = new RenderSurfaceHost<BitmapBackbuffer>(firstAdapter);
        using var secondHost = new RenderSurfaceHost<BitmapBackbuffer>(secondAdapter);
        firstHost.Bind(scene);

        //Act
        Action bindAgain = () => secondHost.Bind(scene);

        //Assert
        bindAgain.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{scene.ID}*");
        scene.BoundRenderSurfaceHost.Should().BeSameAs(firstHost);
        firstHost.Scene.Should().BeSameAs(scene);
        secondHost.Scene.Should().BeSameAs(Scene.Empty);
    }

    [Fact]
    public void Bind_releases_the_previous_scene_so_another_host_can_claim_it()
    {
        //Arrange
        using var firstScene = new Scene();
        using var secondScene = new Scene();
        using var firstAdapter = new FakeRenderSurfaceAdapter();
        using var secondAdapter = new FakeRenderSurfaceAdapter();
        using var firstHost = new RenderSurfaceHost<BitmapBackbuffer>(firstAdapter);
        using var secondHost = new RenderSurfaceHost<BitmapBackbuffer>(secondAdapter);

        //Act
        firstHost.Bind(firstScene);
        firstHost.Bind(secondScene);
        secondHost.Bind(firstScene);

        //Assert
        firstScene.BoundRenderSurfaceHost.Should().BeSameAs(secondHost);
        secondScene.BoundRenderSurfaceHost.Should().BeSameAs(firstHost);
    }

    [Fact]
    public void Dispose_releases_the_scene_binding()
    {
        //Arrange
        using var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter();
        var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene);

        //Act
        host.Dispose();

        //Assert - the scene is free for another host, and the disposed one holds it no longer.
        scene.BoundRenderSurfaceHost.Should().BeNull();
        host.Scene.Should().BeSameAs(Scene.Empty);
    }

    [Fact]
    public void Bind_binding_is_released_when_the_bound_scene_is_disposed()
    {
        //Arrange
        var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene);

        //Act
        scene.Dispose();

        //Assert - this port clears the host's scene to null (rather than Scene.Empty) so the
        //  render path can skip the frame outright; see RenderToBackbuffer's null-scene guard.
        scene.BoundRenderSurfaceHost.Should().BeNull();
        host.Scene.Should().BeNull();
    }

    [Fact]
    public void GlRenderToCanvas_returns_false_for_a_surface_that_is_not_gl_rendered()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        using var destination = SKSurface.Create(new SKImageInfo(320, 200));

        //Act
        bool rendered = host.GlRenderToCanvas(destination.Canvas);
        bool drawn = host.GlDrawCurrentFrameToCanvas(destination.Canvas);
        SKImage? snapshot = host.GlSnapshotCurrentFrame();

        //Assert - the CPU path is driven by the engine loop, not by a GPU paint callback.
        rendered.Should().BeFalse();
        drawn.Should().BeFalse();
        snapshot.Should().BeNull();
    }

    [Fact]
    public void GlRenderToCanvas_rejects_a_null_destination_canvas()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);

        //Act
        Action renderToNothing = () => host.GlRenderToCanvas(null!);
        Action drawToNothing = () => host.GlDrawCurrentFrameToCanvas(null!);

        //Assert
        renderToNothing.Should().Throw<ArgumentNullException>();
        drawToNothing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GlDrawCurrentFrameToCanvas_copies_the_current_surface_to_the_destination()
    {
        //Arrange - a GPU backbuffer starts on its CPU-fallback raster surface (no GRContext),
        //  which is enough to prove the surface-to-canvas copy.
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        using var destination = SKSurface.Create(new SKImageInfo(320, 200));
        destination.Canvas.Clear(SKColors.Blue);
        host.Backbuffer.Canvas.Clear(SKColors.Red);

        //Act
        bool drawn = host.GlDrawCurrentFrameToCanvas(destination.Canvas);

        //Assert
        drawn.Should().BeTrue();
        ReadPixel(destination, 10, 10).Should().Be(SKColors.Red);
    }

    [Fact]
    public void GlSnapshotCurrentFrame_returns_the_current_frame_without_rendering_a_new_one()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        int renderCount = 0;
        Action onRenderBegin = () => renderCount++;
        host.RenderBackbufferBegin += onRenderBegin;
        host.Backbuffer.Canvas.Clear(SKColors.Lime);

        //Act
        using SKImage? snapshot = host.GlSnapshotCurrentFrame();

        //Assert
        host.RenderBackbufferBegin -= onRenderBegin;
        snapshot.Should().NotBeNull();
        renderCount.Should().Be(0);
    }

    [Fact]
    public void GlRenderToCanvas_renders_a_new_frame_when_the_engine_is_not_paused()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        using var destination = SKSurface.Create(new SKImageInfo(320, 200));
        int renderCount = 0;
        Action onRenderBegin = () => renderCount++;
        host.RenderBackbufferBegin += onRenderBegin;

        //Act
        bool rendered = host.GlRenderToCanvas(destination.Canvas);

        //Assert
        host.RenderBackbufferBegin -= onRenderBegin;
        rendered.Should().BeTrue();
        renderCount.Should().Be(1);
    }

    [Fact]
    public void GlRenderToCanvas_re_presents_the_current_frame_while_the_engine_is_paused()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        using var destination = SKSurface.Create(new SKImageInfo(320, 200));
        destination.Canvas.Clear(SKColors.Blue);
        host.Backbuffer.Canvas.Clear(SKColors.Red);
        int renderCount = 0;
        Action onRenderBegin = () => renderCount++;
        host.RenderBackbufferBegin += onRenderBegin;
        bool rendered;

        //Act
        Engine.Instance.Pause();

        try
        {
            rendered = host.GlRenderToCanvas(destination.Canvas);
        }
        finally
        {
            Engine.Instance.Resume();
            host.RenderBackbufferBegin -= onRenderBegin;
        }

        //Assert - the global pause must not let a GPU presenter advance the scene: the frame
        //  that was last rendered is re-presented instead.
        rendered.Should().BeTrue();
        renderCount.Should().Be(0);
        ReadPixel(destination, 10, 10).Should().Be(SKColors.Red);
    }

    [Fact]
    public void GlRenderToCanvas_renders_while_paused_when_the_caller_asks_for_it()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        using var destination = SKSurface.Create(new SKImageInfo(320, 200));
        int renderCount = 0;
        Action onRenderBegin = () => renderCount++;
        host.RenderBackbufferBegin += onRenderBegin;
        bool rendered;

        //Act - this is the adapter-driven paused-overlay frame, matching GlRenderAndSnapshot.
        Engine.Instance.Pause();

        try
        {
            rendered = host.GlRenderToCanvas(destination.Canvas, renderWhilePaused: true);
        }
        finally
        {
            Engine.Instance.Resume();
            host.RenderBackbufferBegin -= onRenderBegin;
        }

        //Assert
        rendered.Should().BeTrue();
        renderCount.Should().Be(1);
    }

    private static SKColor ReadPixel(SKSurface surface, int x, int y)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
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
