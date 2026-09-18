using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Rendering behaviour on periodic layers: layer-bound content repeats across the view on both
/// the dirty-rectangle bitmap path and the full-frame GL path, hiding the canonical drawing clears
/// every copy, view-bound content never repeats, and tile fog and collision outlines follow each
/// translated tile instance.
/// </summary>
public class WrappedRenderingTests : IDisposable
{
    private readonly List<Scene> _scenes = new();

    /// <summary>Binds the engine dispatcher to the test thread so posted work runs inline.</summary>
    public WrappedRenderingTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
    }

    /// <summary>Clears the global scene registry this fixture populated.</summary>
    public void Dispose()
    {
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
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RenderToBackbuffer_repeats_layer_drawings_and_keeps_view_content_fixed_on_the_bitmap_path(
        bool horizontal,
        bool vertical)
    {
        AssertLayerDrawingRepeats<BitmapBackbuffer>(horizontal, vertical);
    }

    [Fact]
    public void RenderToBackbuffer_repeats_layer_drawings_and_keeps_view_content_fixed_on_the_full_frame_path()
    {
        // GpuBackbuffer uses its CPU fallback surface until a GRContext is attached,
        // while still exercising the GL-thread/full-frame host path.
        AssertLayerDrawingRepeats<GpuBackbuffer>(horizontal: true, vertical: true);
    }

    [Fact]
    public void DrawDrawables_translates_fog_and_collision_outlines_with_each_tile_instance()
    {
        //Arrange - a single 16 px tile repeated across a 64 px view.
        var scene = CreateScene();
        var layer = scene.AddLayer(1, 1, 16, 16);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        layer.ShowCollisionBoxes = true;
        layer[0, 0]!.EnableFog = true;
        layer[0, 0]!.CollisionsEnabled = true;

        var camera = new Camera(scene);
        var view = new View(camera, new Viewport { TargetRectPx = new Rectangle(0, 0, 64, 64) });

        using var buffer = new BitmapBackbuffer(64, 64);
        buffer.FogPaint.Color = SKColors.Red;

        //Act
        buffer.DrawDrawables(
            view,
            layer.GetDrawablesInWorldRect(new Rectangle(0, 0, 64, 64)),
            new Rectangle(0, 0, 64, 64));

        //Assert - fog covers the canonical cell AND its repeats, and the collision outline follows.
        using var image = buffer.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        bitmap.GetPixel(40, 40).Should().Be(SKColors.Red);
        bitmap.GetPixel(8, 8).Should().Be(SKColors.Red);
        (bitmap.GetPixel(32, 40).Green > 0).Should().BeTrue();
    }

    private void AssertLayerDrawingRepeats<TBackbuffer>(bool horizontal, bool vertical)
        where TBackbuffer : BackbufferBase
    {
        //Arrange - a 32 px period layer under a 128 px surface, with the camera one period back.
        var scene = CreateScene();
        var layer = scene.AddLayer(2, 2, 16, 16);
        layer.WrapHorizontally = horizontal;
        layer.WrapVertically = vertical;

        using var adapter = new FakeRenderSurfaceAdapter(128, 128);
        using var host = new RenderSurfaceHost<TBackbuffer>(adapter);
        host.Bind(scene, limitCameraToWorldBoundPx: false);

        View view = host.ViewManager.Views[0];
        view.Camera.SnapTo(new PointF(-32, -32));

        using var drawing = new DirectRectangle(Color.Red, host, layer, new Rectangle(2, 2, 8, 8)).SetFilled(true);
        using var overlay = new DirectRectangle(Color.Blue, host, view, new Rectangle(0, 0, 4, 4)).SetFilled(true);

        //Act
        host.RenderToBackbuffer(0);
        host.Backbuffer.EndFrame();

        //Assert - the layer drawing repeats on enabled axes only; the view overlay stays put.
        using (var image = host.Backbuffer.Snapshot())
        using (var bitmap = SKBitmap.FromImage(image))
        {
            bitmap.GetPixel(1, 1).Should().Be(SKColors.Blue);

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    bitmap.GetPixel(x * 32 + 5, y * 32 + 5).Should().Be(
                        (horizontal || x == 1) && (vertical || y == 1) ? SKColors.Red : SKColors.Black);
                }
            }

            bitmap.GetPixel(33, 33).Should().Be(SKColors.Black);
        }

        //Act - hiding the canonical drawing must clear every repeated copy.
        host.Backbuffer.BeginFrame();
        drawing.Visible = false;
        host.RenderToBackbuffer(1);
        host.Backbuffer.EndFrame();

        //Assert
        using (var image = host.Backbuffer.Snapshot())
        using (var bitmap = SKBitmap.FromImage(image))
        {
            bitmap.GetPixel(1, 1).Should().Be(SKColors.Blue);

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                    bitmap.GetPixel(x * 32 + 5, y * 32 + 5).Should().Be(SKColors.Black);
            }
        }

        host.Backbuffer.BeginFrame();
    }

    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter(int width, int height) : base(width, height)
        {
        }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
