using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Presentation decoupling: a render surface establishes its logical Backbuffer resolution once,
/// from the first valid adapter size and the configured render scale, and every later adapter
/// resize changes presentation only — the complete image is fitted into the surface and centred,
/// with the margins cleared. Also covers the two port-only ways to control that resolution
/// explicitly: <see cref="RenderSurfaceHostBase.RequestRenderResolution"/> and
/// <see cref="RenderSurfaceHostBase.TrackAdapterSize"/>.
/// </summary>
public class ViewportScalingTests : IDisposable
{
    private readonly List<Scene> _scenes = new();

    /// <summary>Binds the engine dispatcher to the test thread and starts from the default scale.</summary>
    public ViewportScalingTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
        Engine.Instance.Configuration.RenderScale = 1f;
        Engine.Instance.Configuration.RenderScalingFilter = RenderScalingFilter.Linear;
    }

    /// <summary>Restores the global render-scale settings and clears the scene registry.</summary>
    public void Dispose()
    {
        Engine.Instance.Configuration.RenderScale = 1f;
        Engine.Instance.Configuration.RenderScalingFilter = RenderScalingFilter.Linear;

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private Scene CreateScene()
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene;
    }

    [Theory]
    [InlineData(3840, 2160, 0.5f, 1920, 1080, 2f)]
    [InlineData(1920, 1080, 1f, 1920, 1080, 1f)]
    [InlineData(1920, 1080, 2f, 3840, 2160, 0.5f)]
    [InlineData(3, 5, 0.5f, 2, 3, 1.5f)]
    [InlineData(1, 1, 0.001f, 1, 1, 1f)]
    public void the_first_layout_establishes_the_logical_resolution_from_the_render_scale(
        int adapterWidth,
        int adapterHeight,
        float renderScale,
        int bufferWidth,
        int bufferHeight,
        float presentationScale)
    {
        //Arrange
        Engine.Instance.Configuration.RenderScale = renderScale;

        //Act
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(new TestAdapter(adapterWidth, adapterHeight));
        var scene = CreateScene();
        host.Bind(scene, false);

        //Assert - the backbuffer is the scaled surface, and the default view covers all of it.
        host.Backbuffer.Width.Should().Be(bufferWidth);
        host.Backbuffer.Height.Should().Be(bufferHeight);
        host.PresentationScale.Should().BeApproximately(presentationScale, 0.00001f);
        host.ViewManager.Views.Should().ContainSingle();
        host.ViewManager.Views[0].Viewport.TargetRectPx
            .Should().Be(new Rectangle(0, 0, bufferWidth, bufferHeight));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RenderScale_rejects_a_value_that_is_not_finite_and_positive(float renderScale)
    {
        //Arrange
        Action setScale = () => Engine.Instance.Configuration.RenderScale = renderScale;

        //Act & Assert
        setScale.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void resizing_the_adapter_keeps_the_backbuffer_views_and_camera_while_an_explicit_scale_re_establishes_them()
    {
        //Arrange
        var adapter = new TestAdapter(1920, 1080);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;
        var scene = CreateScene();
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];
        view.Viewport.Zoom = 2f;
        var canvas = buffer.Canvas;
        var originalViewport = view.Viewport.TargetRectPx;
        var cameraPosition = view.Camera.PositionPx;
        int sizeChanges = 0;
        buffer.SizeChanged += (_, _) => sizeChanges++;

        //Act - every one of these is a presentation-only change, minimizing included.
        foreach (var (width, height) in new[] { (3840, 2160), (1600, 1000), (0, 0), (900, 1600), (1600, 1000) })
        {
            adapter.Resize(width, height);
            buffer.BeginFrame();

            //Assert - same backbuffer, same canvas, same logical size, same views, same zoom.
            host.Backbuffer.Should().BeSameAs(buffer);
            buffer.Canvas.Should().BeSameAs(canvas);
            buffer.Width.Should().Be(1920);
            buffer.Height.Should().Be(1080);
            view.Viewport.TargetRectPx.Should().Be(originalViewport);
            view.Camera.PositionPx.Should().Be(cameraPosition);
            view.Viewport.Zoom.Should().Be(2f);
        }

        //Assert - the letterbox is centred on the axis that has room to spare.
        sizeChanges.Should().Be(0);
        host.PresentationScale.Should().BeApproximately(5f / 6f, 0.00001f);
        adapter.Presentation.DestinationRect.Should().Be(new SKRect(0, 50, 1600, 950));

        //Act - an explicit scale change re-establishes the resolution from the CURRENT adapter size.
        Engine.Instance.Configuration.RenderScale = 0.5f;
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(800);
        buffer.Height.Should().Be(500);
        sizeChanges.Should().Be(1);
        host.PresentationScale.Should().Be(2f);
        view.Viewport.TargetRectPx.Should().Be(new Rectangle(0, 0, 800, 500));
    }

    [Fact]
    public void a_deferred_first_layout_establishes_the_resolution_once_and_a_scale_change_while_minimized_waits()
    {
        //Arrange - a host constructed before its first layout pass starts on a 1x1 placeholder.
        Engine.Instance.Configuration.RenderScale = 0.5f;
        var adapter = new TestAdapter(1, 1, initialSizeAvailable: false);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;

        //Act - the first valid layout establishes the resolution...
        adapter.Resize(1920, 1080);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(960);
        buffer.Height.Should().Be(540);

        //Act - ...and no later layout re-establishes it.
        adapter.Resize(1600, 1000);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(960);
        buffer.Height.Should().Be(540);

        //Act - a scale change while the surface has no usable size is deferred to the next layout.
        adapter.Resize(0, 0);
        Engine.Instance.Configuration.RenderScale = 2f;
        adapter.Resize(800, 600);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(1600);
        buffer.Height.Should().Be(1200);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    public void SetDestinationSize_raises_one_resize_for_repeated_unusable_dimensions(int width, int height)
    {
        //Arrange
        var adapter = new TestAdapter(1, 1, initialSizeAvailable: false);
        int events = 0;
        adapter.Resized += _ => events++;

        //Act
        adapter.Resize(width, height);
        int afterFirst = events;
        adapter.Resize(width, height);
        adapter.Resize(width, height);

        //Assert - the change of dimensions raises the event; repeating it does not.
        afterFirst.Should().Be(1);
        events.Should().Be(1);
        adapter.InitialSizeAvailable.Should().BeFalse();
    }

    [Fact]
    public void SetDestinationSize_raises_the_first_valid_layout_even_when_it_matches_the_placeholder()
    {
        //Arrange
        Engine.Instance.Configuration.RenderScale = 2f;
        var adapter = new TestAdapter(1, 1, initialSizeAvailable: false);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;
        int events = 0;
        adapter.Resized += _ => events++;

        //Act
        adapter.Resize(1, 1);
        buffer.BeginFrame();

        //Assert - a 1x1 window IS a layout; the resolution is established from it, once.
        adapter.InitialSizeAvailable.Should().BeTrue();
        buffer.Width.Should().Be(2);
        buffer.Height.Should().Be(2);

        //Act
        adapter.Resize(1, 1);

        //Assert
        events.Should().Be(1);
    }

    [Theory]
    [InlineData(0.5f, 2f)]
    [InlineData(2f, 0.5f)]
    public void AdapterPxToScreenPx_round_trips_to_the_same_world_and_grid_point_at_any_presented_size(
        float renderScale,
        float zoom)
    {
        //Arrange
        Engine.Instance.Configuration.RenderScale = renderScale;
        var adapter = new TestAdapter(800, 600);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var scene = CreateScene();
        var layer = scene.AddLayer(100, 100, width: 32, height: 32);
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];
        view.Viewport.Zoom = zoom;
        view.Camera.SnapTo(new PointF(32, 16));
        var world = new PointF(96, 80);
        var logical = view.WorldPxToScreenPx(layer, world);

        //Act & Assert - the adapter transform is the only thing that changes with the window size.
        foreach (var (width, height) in new[] { (800, 600), (1600, 1000), (400, 300) })
        {
            adapter.Resize(width, height);
            var transform = adapter.Presentation;
            var adapterPx = new PointF(transform.DestinationRect.Left + (logical.X * transform.Scale),
                                       transform.DestinationRect.Top + (logical.Y * transform.Scale));

            transform.TryAdapterPxToScreenPx(adapterPx, out var screen).Should().BeTrue();

            var restored = view.ScreenPxToWorldPx(layer, screen);
            restored.X.Should().BeApproximately(world.X, 0.001f);
            restored.Y.Should().BeApproximately(world.Y, 0.001f);

            var grid = view.ScreenPxToGrid(layer, screen);
            grid.X.Should().BeApproximately(3f, 0.001f);
            grid.Y.Should().BeApproximately(2.5f, 0.001f);
        }
    }

    [Fact]
    public void AdapterPxToScreenPx_maps_the_margins_outside_the_image_without_clamping_them_to_zero()
    {
        //Arrange - a 1920x1080 image letterboxed into a 1600x1000 surface leaves 50 px bars.
        var adapter = new TestAdapter(1600, 1000);
        adapter.SetLogicalSize(1920, 1080);

        //Act & Assert
        adapter.AdapterPxToScreenPx(new PointF(800, 500)).Should().Be(new Point(960, 540));

        // Floor, not truncation: a point inside the top bar must not land on row zero.
        adapter.AdapterPxToScreenPx(new PointF(0, 49.9f)).Y.Should().Be(-1);

        adapter.Presentation.TryAdapterPxToScreenPx(new PointF(800, 20), out _).Should().BeFalse();
        adapter.Presentation.TryAdapterPxToScreenPx(new PointF(800, 950), out _).Should().BeFalse();
        adapter.Presentation.TryAdapterPxToScreenPx(new PointF(0, 50), out _).Should().BeTrue();

        //Act - a minimized surface presents nothing at all.
        adapter.Resize(0, 0);

        //Assert
        adapter.PresentationScale.Should().Be(0f);
        adapter.AdapterPxToScreenPx(Point.Empty).Should().Be(new Point(-1, -1));
    }

    [Fact]
    public void ScreenRectToAdapterRect_rounds_dirty_edges_outwards_without_leaving_holes()
    {
        //Arrange
        var transform = PresentationTransform.Fit(1920, 1080, 1600, 1000);

        //Act
        var first = transform.ScreenRectToAdapterRect(new Rectangle(0, 0, 7, 11));
        var second = transform.ScreenRectToAdapterRect(new Rectangle(7, 0, 7, 11));

        //Assert - adjacent logical rects must overlap, never leave a seam.
        first.Should().Be(new Rectangle(0, 50, 6, 10));
        first.Right.Should().BeGreaterThanOrEqualTo(second.Left);
    }

    [Theory]
    [InlineData(RenderScalingFilter.NearestNeighbor)]
    [InlineData(RenderScalingFilter.Linear)]
    public void DrawImage_clears_the_margins_and_honours_the_scaling_filter(RenderScalingFilter filter)
    {
        //Arrange - a 2x1 image presented on an 8x8 surface: 8x4 image, 2 px bars top and bottom.
        Engine.Instance.Configuration.RenderScalingFilter = filter;
        var adapter = new TestAdapter(8, 8);
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);
        using var image = SKImage.FromBitmap(source);
        using var destination = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(destination);
        canvas.Clear(SKColors.Green);

        //Act
        adapter.DrawImage(canvas, image, SKColors.Black);

        //Assert
        destination.GetPixel(4, 0).Should().Be(SKColors.Black);
        destination.GetPixel(0, 3).Should().Be(SKColors.Red);
        destination.GetPixel(7, 3).Should().Be(SKColors.Blue);

        if (filter == RenderScalingFilter.NearestNeighbor)
            destination.GetPixel(3, 3).Should().Be(SKColors.Red);
        else
            destination.GetPixel(3, 3).Blue.Should().BeInRange((byte)1, (byte)254);
    }

    [Theory]
    [InlineData(RenderScalingFilter.Linear)]
    [InlineData(RenderScalingFilter.NearestNeighbor)]
    public void GlDrawCurrentFrameToCanvas_presents_the_gl_surface_through_the_same_transform(RenderScalingFilter filter)
    {
        //Arrange - the GPU backbuffer runs on its CPU fallback surface here; the host path is the same.
        Engine.Instance.Configuration.RenderScalingFilter = filter;
        var adapter = new TestAdapter(2, 1);
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        host.Backbuffer.Canvas.Clear(SKColors.Red);

        using (var blue = new SKPaint { Color = SKColors.Blue })
            host.Backbuffer.Canvas.DrawRect(new SKRect(1, 0, 2, 1), blue);

        adapter.Resize(8, 8);
        using var pixels = new SKBitmap(8, 8);
        using var canvas = new SKCanvas(pixels);

        //Act
        bool drawn = host.GlDrawCurrentFrameToCanvas(canvas);

        //Assert
        drawn.Should().BeTrue();
        pixels.GetPixel(4, 0).Should().Be(SKColors.Black);
        pixels.GetPixel(0, 3).Should().Be(SKColors.Red);
        pixels.GetPixel(7, 3).Should().Be(SKColors.Blue);

        if (filter == RenderScalingFilter.NearestNeighbor)
            pixels.GetPixel(3, 3).Should().Be(SKColors.Red);
        else
            pixels.GetPixel(3, 3).Blue.Should().BeInRange((byte)1, (byte)254);
    }

    [Theory]
    [InlineData(0.5f, 2f, true)]
    [InlineData(2f, 0.5f, true)]
    [InlineData(0.5f, 2f, false)]
    [InlineData(2f, 2f, false)]
    public void a_rendered_text_block_keeps_its_logical_pixels_and_bounds_across_a_presentation_resize(
        float renderScale,
        float zoom,
        bool worldMode)
    {
        //Arrange
        Engine.Instance.Configuration.RenderScale = renderScale;
        var adapter = new TestAdapter(600, 400);
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        var scene = CreateScene();
        var layer = scene.AddLayer(20, 20, width: 32, height: 32);
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];
        view.Viewport.Zoom = zoom;

        using var text = (worldMode
                ? new TextBlock(host, layer, view, new Rectangle(15, 15, 100, 70))
                : new TextBlock(host, view, new Rectangle(15, 15, 100, 70)))
            .SetText("Scale").SetFont(SKTypeface.Default, 14).SetColors(SKColors.White, SKColors.Transparent);

        var boundsBefore = text.GetDrawLocationScreen(view);
        using var before = host.GlRenderAndSnapshot();
        using var beforePixels = SKBitmap.FromImage(before!);

        //Act
        adapter.Resize(777, 333);
        using var after = host.GlRenderAndSnapshot();
        using var afterPixels = SKBitmap.FromImage(after!);

        //Assert - nothing about the rendered frame depends on the size it is presented at.
        beforePixels.Pixels.Should().Contain(pixel => pixel.Red > 0);
        text.GetDrawLocationScreen(view).Should().Be(boundsBefore);
        afterPixels.Pixels.Should().Equal(beforePixels.Pixels);
        TextBlock.ResolveTextScale(text.Mode, view.Viewport.Zoom).Should().Be(worldMode ? zoom : 1f);

        //Assert - and the presented frame still shows it, fitted into the resized surface.
        using var presented = new SKBitmap(adapter.Width, adapter.Height);
        using var canvas = new SKCanvas(presented);
        host.GlDrawCurrentFrameToCanvas(canvas).Should().BeTrue();
        presented.Pixels.Should().Contain(pixel => pixel.Red > 0);
    }

    [Fact]
    public void RequestRenderResolution_pins_the_logical_resolution_and_survives_later_adapter_resizes()
    {
        //Arrange - the port-only way a host pins a surface to a fixed resolution.
        var adapter = new TestAdapter(800, 600);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;
        var scene = CreateScene();
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];

        //Act
        host.RequestRenderResolution(1280, 720);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(1280);
        buffer.Height.Should().Be(720);
        view.Viewport.TargetRectPx.Should().Be(new Rectangle(0, 0, 1280, 720));

        //Act - a later resize presents that same image; it does not re-derive a resolution.
        adapter.Resize(1600, 900);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(1280);
        buffer.Height.Should().Be(720);
        host.PresentationScale.Should().BeApproximately(1.25f, 0.00001f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void a_view_mode_drawing_created_right_after_a_pinned_resolution_gets_the_whole_logical_surface(bool laidOut)
    {
        //Arrange - the ParticleTest order: pin the resolution, THEN build the scene, the full view
        //  and the view-mode overlays, all laid out in the pinned 1280x720 space. `laidOut` false
        //  is the same call order on a surface that has not had its first layout pass yet.
        var adapter = new TestAdapter(1024, 640, initialSizeAvailable: laidOut);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.RequestRenderResolution(1280, 720);

        if (!laidOut)
            adapter.Resize(1024, 640);

        var scene = CreateScene();
        host.Bind(scene, false);
        host.ViewManager.ConfigureSingleFullView();
        var view = host.ViewManager.Views[0];

        using var box = new DirectRectangle(Color.Red, host, view, new Rectangle(20, 504, 1240, 160))
            .SetFilled(true);

        //Act
        host.RenderToBackbuffer(0);
        host.Backbuffer.EndFrame();

        //Assert - the view covers the pinned surface, and the overlay was not clipped to the size
        //  the surface happened to have when the resolution was still being applied.
        view.Viewport.TargetRectPx.Should().Be(new Rectangle(0, 0, 1280, 720));
        host.Backbuffer.Width.Should().Be(1280);
        host.Backbuffer.Height.Should().Be(720);
        box.ScreenBounds.Should().Be(new Rectangle(20, 504, 1240, 160));

        using var image = host.Backbuffer.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        bitmap.GetPixel(640, 580).Should().Be(SKColors.Red);
        bitmap.GetPixel(640, 100).Should().NotBe(SKColors.Red);
        bitmap.GetPixel(1200, 580).Should().Be(SKColors.Red);
    }

    [Fact]
    public void RequestRenderResolution_is_deferred_until_the_adapter_has_a_usable_size()
    {
        //Arrange
        var adapter = new TestAdapter(1, 1, initialSizeAvailable: false);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;

        //Act - asked for before the first layout pass.
        host.RequestRenderResolution(640, 480);
        buffer.BeginFrame();

        //Assert - nothing to apply it against yet.
        buffer.Width.Should().Be(1);
        buffer.Height.Should().Be(1);

        //Act
        adapter.Resize(1920, 1080);
        buffer.BeginFrame();

        //Assert - the pinned resolution wins over the render scale the first layout would have used.
        buffer.Width.Should().Be(640);
        buffer.Height.Should().Be(480);
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(640, 0)]
    [InlineData(-1, -1)]
    public void RequestRenderResolution_rejects_dimensions_below_one(int width, int height)
    {
        //Arrange
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(new TestAdapter(320, 200));
        Action pin = () => host.RequestRenderResolution(width, height);

        //Act & Assert
        pin.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RequestRenderResolution_reaches_a_gpu_tier_surface_that_never_gets_a_gpu_context()
    {
        //Arrange - the GPU tier runs on its CPU fallback surface until a GRContext arrives, and on
        //  a head that never supplies one it stays there; the resolution must still be applied.
        var adapter = new TestAdapter(800, 600);
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        var buffer = host.Backbuffer;
        var scene = CreateScene();
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];

        //Act
        host.RequestRenderResolution(320, 240);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(320);
        buffer.Height.Should().Be(240);
        view.Viewport.TargetRectPx.Should().Be(new Rectangle(0, 0, 320, 240));
        host.PresentationScale.Should().Be(2.5f);
    }

    [Fact]
    public void a_deferred_first_layout_establishes_the_resolution_on_a_gpu_tier_surface_too()
    {
        //Arrange
        Engine.Instance.Configuration.RenderScale = 0.5f;
        var adapter = new TestAdapter(1, 1, initialSizeAvailable: false);
        using var host = new RenderSurfaceHost<GpuBackbuffer>(adapter);
        var buffer = host.Backbuffer;

        //Act
        adapter.Resize(1920, 1080);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(960);
        buffer.Height.Should().Be(540);
    }

    [Fact]
    public void a_pending_resolution_change_is_applied_even_after_the_bound_scene_was_disposed()
    {
        //Arrange - this port clears the host's scene to null when the bound scene is disposed,
        //  and the pending resolution is applied before the render path's null-scene guard runs.
        var adapter = new TestAdapter(800, 600);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        var buffer = host.Backbuffer;
        var scene = new Scene();
        host.Bind(scene, false);
        scene.Dispose();
        host.RequestRenderResolution(640, 480);

        //Act
        Action beginFrame = () => buffer.BeginFrame();

        //Assert
        beginFrame.Should().NotThrow();
        buffer.Width.Should().Be(640);
        buffer.Height.Should().Be(480);
    }

    [Fact]
    public void TrackAdapterSize_re_establishes_the_resolution_on_every_adapter_resize()
    {
        //Arrange - the port-only opt-in for a surface whose resolution follows its window.
        Engine.Instance.Configuration.RenderScale = 0.5f;
        var adapter = new TestAdapter(800, 600);
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter) { TrackAdapterSize = true };
        var buffer = host.Backbuffer;
        var scene = CreateScene();
        host.Bind(scene, false);
        var view = host.ViewManager.Views[0];

        //Act
        adapter.Resize(1600, 1000);
        buffer.BeginFrame();

        //Assert - the backbuffer followed the window, at the configured scale.
        buffer.Width.Should().Be(800);
        buffer.Height.Should().Be(500);
        view.Viewport.TargetRectPx.Should().Be(new Rectangle(0, 0, 800, 500));
        host.PresentationScale.Should().Be(2f);

        //Act - switching tracking off restores the default letterbox behaviour.
        host.TrackAdapterSize = false;
        adapter.Resize(800, 600);
        buffer.BeginFrame();

        //Assert
        buffer.Width.Should().Be(800);
        buffer.Height.Should().Be(500);
    }

    /// <summary>A render-surface adapter that presents nowhere, with a test-driven size.</summary>
    private sealed class TestAdapter : RenderSurfaceAdapterBase
    {
        internal TestAdapter(int width, int height, bool initialSizeAvailable = true)
            : base(width, height, initialSizeAvailable)
        {
        }

        internal void Resize(int width, int height) => SetDestinationSize(width, height);

        internal void SetLogicalSize(int width, int height) => SetBackbufferSize(width, height);

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
            => bufferImage.Dispose();
    }
}
