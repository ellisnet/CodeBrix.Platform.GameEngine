using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.ImageLayer;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the scene-layer mode added to <see cref="ImageInstanceLayer"/> upstream: the two
/// world-space constructors, the hook bounds (world in scene-layer mode, screen in view mode),
/// the destination-rectangle mapping in <c>OnDraw</c>, and the refresh-rectangle routing. The
/// view-mode drawing is asserted as well, because the same mapping now runs for it.
/// </summary>
public class ImageInstanceLayerTests : IDisposable
{
    private readonly List<IDisposable> _created = new();
    private readonly List<Scene> _scenes = new();

    /// <summary>
    /// Makes this thread the engine thread so the refresh queues run their work inline
    /// (no engine loop is running during the test).
    /// </summary>
    public ImageInstanceLayerTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
    }

    /// <summary>
    /// Disposes everything this fixture registered with the process-global
    /// <see cref="DirectDrawingManager"/>, then the scenes and the host, and finally hands the
    /// engine-thread identity to a thread that has already exited.
    /// </summary>
    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            try
            {
                _created[i].Dispose();
            }
            catch (ObjectDisposedException)
            {
                // A test that disposed its own layer is the normal path here.
            }
        }

        _created.Clear();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();

        var releaser = new Thread(() => Engine.Instance.EngineDispatcher.BindToCurrentThread());
        releaser.Start();
        releaser.Join();

        GC.SuppressFinalize(this);
    }

    private RenderSurfaceHost<BitmapBackbuffer> NewHost()
    {
        var adapter = new FakeRenderSurfaceAdapter();
        var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.ConfigureSingleFullView();
        _created.Add(host);
        _created.Add(adapter);
        return host;
    }

    private SceneLayer NewLayer()
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene.AddLayer(
            columnCount: 4,
            rowCount: 4,
            width: 16,
            height: 16,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);
    }

    private static SKBitmap NewSolidBitmap(SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        return bitmap;
    }

    private static SKBitmap DrawToBitmap(ImageInstanceLayer layer, int width, int height, RectangleF destRectScreen)
    {
        using var backbuffer = new BitmapBackbuffer(width, height);
        layer.Draw(backbuffer, destRectScreen);

        using var snapshot = backbuffer.Snapshot();
        return SKBitmap.FromImage(snapshot);
    }

    [Fact]
    public void OnDraw_view_mode_places_instances_unchanged_when_the_destination_matches_the_bounds()
    {
        //Arrange
        var host = NewHost();
        using var bitmap = NewSolidBitmap(SKColors.Red);

        var layer = new ImageInstanceLayer(
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 8, 8),
            "view-layer");

        _created.Add(layer);
        layer.Instances.Add(new ImageInstance { Bitmap = bitmap, Bounds = new RectangleF(2, 2, 4, 4) });

        //Act - the scale factors are 1 here, so the output must match the pre-change behaviour.
        using var result = DrawToBitmap(layer, 8, 8, new RectangleF(0, 0, 8, 8));

        //Assert
        result.GetPixel(2, 2).Should().Be(SKColors.Red);
        result.GetPixel(5, 5).Should().Be(SKColors.Red);
        result.GetPixel(1, 1).Should().NotBe(SKColors.Red);
        result.GetPixel(6, 6).Should().NotBe(SKColors.Red);
    }

    [Fact]
    public void OnDraw_view_mode_maps_instances_into_an_offset_and_scaled_destination()
    {
        //Arrange
        var host = NewHost();
        using var bitmap = NewSolidBitmap(SKColors.Red);

        var layer = new ImageInstanceLayer(
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 8, 8),
            "view-layer");

        _created.Add(layer);
        layer.Instances.Add(new ImageInstance { Bitmap = bitmap, Bounds = new RectangleF(2, 2, 4, 4) });

        //Act - destination is twice the size and shifted right by 8, so the instance lands at (12,4)-(20,12).
        using var result = DrawToBitmap(layer, 32, 16, new RectangleF(8, 0, 16, 16));

        //Assert
        result.GetPixel(12, 4).Should().Be(SKColors.Red);
        result.GetPixel(19, 11).Should().Be(SKColors.Red);
        result.GetPixel(11, 3).Should().NotBe(SKColors.Red);
        result.GetPixel(20, 12).Should().NotBe(SKColors.Red);
    }

    [Fact]
    public void OnDraw_scene_layer_mode_maps_world_bounds_into_the_destination_rectangle()
    {
        //Arrange
        var host = NewHost();
        var sceneLayer = NewLayer();
        using var bitmap = NewSolidBitmap(SKColors.Red);

        var layer = new ImageInstanceLayer(
            host,
            sceneLayer,
            new Rectangle(0, 0, 4, 4),
            "world-layer");

        _created.Add(layer);
        layer.Instances.Add(new ImageInstance { Bitmap = bitmap, Bounds = new RectangleF(0, 0, 2, 2) });

        //Act - world bounds 4x4 drawn into an 8x8 destination, so the scale factors are 2.
        using var result = DrawToBitmap(layer, 8, 8, new RectangleF(0, 0, 8, 8));

        //Assert
        result.GetPixel(0, 0).Should().Be(SKColors.Red);
        result.GetPixel(3, 3).Should().Be(SKColors.Red);
        result.GetPixel(4, 4).Should().NotBe(SKColors.Red);
    }

    [Fact]
    public void InitializeInstances_hands_the_world_bounds_to_the_initializer_in_scene_layer_mode()
    {
        //Arrange
        var host = NewHost();
        var sceneLayer = NewLayer();
        var worldBounds = new Rectangle(4, 8, 32, 16);
        Rectangle? seen = null;

        //Act
        var layer = new ImageInstanceLayer(
            host,
            sceneLayer,
            worldBounds,
            initializer: (bounds, _) =>
            {
                seen = bounds;
                return Array.Empty<ImageInstance>();
            },
            nickname: "world-layer");

        _created.Add(layer);

        //Assert
        layer.Mode.Should().Be(DirectDrawingMode.SceneLayer);
        layer.WorldBounds.Should().Be(worldBounds);
        ReferenceEquals(sceneLayer, layer.SceneLayer).Should().BeTrue();
        seen.Should().Be(worldBounds);
    }

    [Fact]
    public void InitializeInstances_hands_the_screen_bounds_to_the_initializer_in_view_mode()
    {
        //Arrange
        var host = NewHost();
        var screenBounds = new Rectangle(2, 4, 16, 8);
        Rectangle? seen = null;

        //Act
        var layer = new ImageInstanceLayer(
            host,
            host.ViewManager.Views[0],
            screenBounds,
            initializer: (bounds, _) =>
            {
                seen = bounds;
                return Array.Empty<ImageInstance>();
            },
            nickname: "view-layer");

        _created.Add(layer);

        //Assert
        layer.Mode.Should().Be(DirectDrawingMode.View);
        seen.Should().Be(screenBounds);
    }

    [Fact]
    public void Update_scene_layer_mode_enqueues_the_dirty_rectangle_on_its_own_layer()
    {
        //Arrange
        var host = NewHost();
        var sceneLayer = NewLayer();
        using var bitmap = NewSolidBitmap(SKColors.Red);

        var layer = new ImageInstanceLayer(
            host,
            sceneLayer,
            new Rectangle(0, 0, 32, 32),
            "world-layer");

        _created.Add(layer);

        layer.Instances.Add(new ImageInstance
        {
            Bitmap = bitmap,
            Bounds = new RectangleF(0, 0, 8, 8),
            VelocityX = 16f
        });

        var tick = HighResTimer.GetCurrentTick();
        layer.Update(tick);
        sceneLayer.RefreshQueue.ClearRefreshQueue();
        sceneLayer.RefreshQueue.IsDirty.Should().BeFalse();

        //Act - the scene layer is not bound to the host, so the old view-mode routing would find nothing.
        layer.Update(tick + (HighResTimer.TicksPerSecond / 60));

        //Assert
        sceneLayer.RefreshQueue.IsDirty.Should().BeTrue();
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(64, 64) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
