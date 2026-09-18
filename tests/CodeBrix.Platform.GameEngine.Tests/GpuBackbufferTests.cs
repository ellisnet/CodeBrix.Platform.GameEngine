using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class GpuBackbufferTests
{
    [Fact]
    public void ReleaseGpuSurface_reverts_to_a_valid_cpu_surface()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(64, 48);

        //Act
        backbuffer.ReleaseGpuSurface();

        //Assert - the backbuffer is back in its pre-Initialize state: same dimensions, and a
        //  usable CPU raster surface (Canvas throws when no surface exists).
        backbuffer.Width.Should().Be(64);
        backbuffer.Height.Should().Be(48);
        SKCanvas canvas = backbuffer.Canvas;
        canvas.Should().NotBeNull();
        canvas.Clear(SKColors.Black); // drawing must not throw
    }

    [Fact]
    public void EnsureInitialized_rejects_a_null_gpu_context()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(32, 32);
        Action ensure = () => backbuffer.EnsureInitialized(null!);

        //Act & Assert
        ensure.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReleaseGpuSurface_forgets_the_context_the_released_surface_belonged_to()
    {
        //Arrange - a GRContext cannot be created without a live GL context, so the recorded
        //  context is observed directly: EnsureInitialized short-circuits on an unchanged
        //  context-and-size pair, so a released surface MUST forget its context or the canvas
        //  would stay on its CPU fallback when the same context comes back.
        using var backbuffer = new GpuBackbuffer(32, 32);
        var contextField = typeof(GpuBackbuffer)
            .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!;

        //Act
        backbuffer.ReleaseGpuSurface();

        //Assert
        contextField.GetValue(backbuffer).Should().BeNull();
    }

    [Fact]
    public void BeginFrame_applies_a_pending_resolution_request_on_the_cpu_fallback_surface()
    {
        //Arrange - a GPU tier on a head that supplies no GRContext renders on the CPU fallback
        //  surface, where EnsureInitialized is never called: BeginFrame has to apply the request.
        using var backbuffer = new GpuBackbuffer(64, 48);
        var sizeChanges = new List<(int Width, int Height)>();
        backbuffer.SizeChanged += (width, height) => sizeChanges.Add((width, height));

        //Act
        backbuffer.RequestResize(128, 96);
        backbuffer.BeginFrame();

        //Assert - the logical size changed exactly once, and the canvas is usable at the new size.
        backbuffer.Width.Should().Be(128);
        backbuffer.Height.Should().Be(96);
        sizeChanges.Should().Equal([(128, 96)]);

        using var snapshot = backbuffer.Snapshot();
        snapshot.Width.Should().Be(128);
        snapshot.Height.Should().Be(96);
        backbuffer.Canvas.Clear(SKColors.Black); // drawing must not throw
    }

    [Fact]
    public void BeginFrame_needs_no_gpu_context_to_consume_the_pending_resolution_request()
    {
        //Arrange - the request must be consumed, not left queued for an EnsureInitialized call
        //  that is never going to come on this path.
        using var backbuffer = new GpuBackbuffer(64, 48);
        var requestField = typeof(GpuBackbuffer)
            .GetField("_requestedResolution", BindingFlags.Instance | BindingFlags.NonPublic)!;
        backbuffer.RequestResize(200, 100);

        //Act
        backbuffer.BeginFrame();

        //Assert
        requestField.GetValue(backbuffer).Should().BeNull();
        backbuffer.Width.Should().Be(200);
        backbuffer.Height.Should().Be(100);

        //Act - a second frame has nothing to apply and must not resize anything again.
        int laterSizeChanges = 0;
        backbuffer.SizeChanged += (_, _) => laterSizeChanges++;
        backbuffer.BeginFrame();

        //Assert
        laterSizeChanges.Should().Be(0);
        backbuffer.Width.Should().Be(200);
        backbuffer.Height.Should().Be(100);
    }

    [Fact]
    public void BeginFrame_ignores_a_pending_resolution_request_that_matches_the_current_size()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(64, 48);
        int sizeChanges = 0;
        backbuffer.SizeChanged += (_, _) => sizeChanges++;

        //Act
        backbuffer.RequestResize(64, 48);
        backbuffer.BeginFrame();

        //Assert
        sizeChanges.Should().Be(0);
        backbuffer.Width.Should().Be(64);
        backbuffer.Height.Should().Be(48);
    }

    [Fact]
    public void ReleaseGpuSurface_is_safe_to_call_repeatedly()
    {
        //Arrange
        using var backbuffer = new GpuBackbuffer(32, 32);

        //Act
        backbuffer.ReleaseGpuSurface();
        backbuffer.ReleaseGpuSurface();

        //Assert
        backbuffer.Canvas.Should().NotBeNull();
    }

    [Fact]
    public void ReleaseGpuSurface_is_a_no_op_after_dispose()
    {
        //Arrange
        var backbuffer = new GpuBackbuffer(32, 32);
        backbuffer.Dispose();

        //Act
        Action release = () => backbuffer.ReleaseGpuSurface();

        //Assert
        release.Should().NotThrow();
    }

    [Fact]
    public void ClearRect_leaves_the_dirty_rectangle_empty_on_a_gl_rendered_surface()
    {
        //Arrange
        using var gpuBackbuffer = new GpuBackbuffer(64, 48);
        using var cpuBackbuffer = new BitmapBackbuffer(64, 48);
        var rectPx = new Rectangle(4, 4, 16, 16);

        //Act
        gpuBackbuffer.ClearRect(rectPx);
        cpuBackbuffer.ClearRect(rectPx);

        //Assert - the GL path presents the whole surface every frame and tracks no dirty
        //  region; the CPU path still does, which is what makes the comparison meaningful.
        gpuBackbuffer.IsGlThreadRendered.Should().BeTrue();
        gpuBackbuffer.DirtyRectangle.Should().Be(Rectangle.Empty);
        cpuBackbuffer.IsGlThreadRendered.Should().BeFalse();
        cpuBackbuffer.DirtyRectangle.Should().NotBe(Rectangle.Empty);
    }

    [Fact]
    public void DrawDrawables_leaves_the_dirty_rectangle_empty_on_a_gl_rendered_surface()
    {
        //Arrange
        using var scene = new Scene();
        var view = CreateView(scene);
        var drawables = new IDrawable[] { new TestDrawable(new RectangleF(8f, 8f, 24f, 24f)) };
        using var gpuBackbuffer = new GpuBackbuffer(64, 48);
        using var cpuBackbuffer = new BitmapBackbuffer(64, 48);
        var clipRect = new Rectangle(0, 0, 64, 48);

        //Act
        gpuBackbuffer.DrawDrawables(view, drawables, clipRect);
        cpuBackbuffer.DrawDrawables(view, drawables, clipRect);

        //Assert
        gpuBackbuffer.DirtyRectangle.Should().Be(Rectangle.Empty);
        cpuBackbuffer.DirtyRectangle.Should().NotBe(Rectangle.Empty);
    }

    private static View CreateView(Scene scene)
    {
        var viewport = new Viewport
        {
            TargetRectPx = new Rectangle(0, 0, 64, 48),
            Zoom = 1f
        };

        var camera = new Camera(scene);
        var view = new View(camera, viewport);
        camera.SnapTo(PointF.Empty);

        return view;
    }

    /// <summary>A drawable that occupies a fixed screen rectangle and paints nothing.</summary>
    private sealed class TestDrawable(RectangleF boundsScreen) : IDrawable
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string? Nickname => null;

        public bool Visible => true;

        public int ZOrder => 0;

        public RectangleF GetDrawLocationScreen(View view) => boundsScreen;

        public void Draw(BackbufferBase backbuffer, RectangleF destRectScreen)
        {
            //Nothing to paint: these tests only observe dirty-region bookkeeping.
        }
    }
}
