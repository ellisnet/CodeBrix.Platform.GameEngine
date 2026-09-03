using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Effects;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Verifies that both render paths — the dirty-rectangle CPU path and the full-frame GL path —
/// composite view and scene-layer effect opacity into the backbuffer.
/// </summary>
public class EffectsRenderingTests
{
    /// <summary>Binds the engine dispatcher to the test thread so posted work runs inline.</summary>
    public EffectsRenderingTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
    }

    [Fact]
    public void RenderToBackbuffer_composites_view_opacity_on_the_bitmap_path()
    {
        AssertViewOpacityIsComposited<BitmapBackbuffer>();
    }

    [Fact]
    public void RenderToBackbuffer_composites_view_opacity_on_the_full_frame_path()
    {
        // GpuBackbuffer uses its CPU fallback surface until a GRContext is attached,
        // while still exercising the GL-thread/full-frame host path.
        AssertViewOpacityIsComposited<GpuBackbuffer>();
    }

    [Fact]
    public void RenderToBackbuffer_composites_scene_layer_opacity_on_the_bitmap_path()
    {
        AssertSceneLayerOpacityIsComposited<BitmapBackbuffer>();
    }

    [Fact]
    public void RenderToBackbuffer_composites_scene_layer_opacity_on_the_full_frame_path()
    {
        AssertSceneLayerOpacityIsComposited<GpuBackbuffer>();
    }

    private static void AssertViewOpacityIsComposited<TBackbuffer>()
        where TBackbuffer : BackbufferBase
    {
        //Arrange
        using var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter(100, 50);
        using var host = new RenderSurfaceHost<TBackbuffer>(adapter);
        host.Bind(scene, limitCameraToWorldBoundPx: false);

        View view = host.ViewManager.Views[0];
        using var rectangle = new DirectRectangle(
            Color.Red,
            host,
            view,
            new Rectangle(0, 0, 100, 50),
            nickname: $"effects-render-{typeof(TBackbuffer).Name}")
            .SetFilled(true);

        //Act - a half-completed fade-out over an opaque red view.
        host.Effects.Run(view, new FadeOutEffect(1f));
        host.Effects.Advance(0.5f);

        host.RenderToBackbuffer(tick: 0);
        host.Backbuffer.EndFrame();

        using SKImage image = host.Backbuffer.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        SKColor pixel = bitmap.GetPixel(50, 25);

        //Assert - red at roughly half strength over the cleared (black) background.
        pixel.Red.Should().BeGreaterThanOrEqualTo((byte)120);
        pixel.Red.Should().BeLessThanOrEqualTo((byte)135);
        pixel.Green.Should().Be((byte)0);
        pixel.Blue.Should().Be((byte)0);
        pixel.Alpha.Should().Be((byte)255);

        host.Backbuffer.BeginFrame();
    }

    private static void AssertSceneLayerOpacityIsComposited<TBackbuffer>()
        where TBackbuffer : BackbufferBase
    {
        //Arrange
        using var scene = new Scene();
        SceneLayer layer = scene.AddLayer(4, 2, width: 25, height: 25);
        using var adapter = new FakeRenderSurfaceAdapter(100, 50);
        using var host = new RenderSurfaceHost<TBackbuffer>(adapter);
        host.Bind(scene, limitCameraToWorldBoundPx: false);

        using var rectangle = new DirectRectangle(
            Color.Red,
            host,
            layer,
            new Rectangle(0, 0, 100, 50),
            nickname: $"effects-layer-render-{typeof(TBackbuffer).Name}")
            .SetFilled(true);

        //Act - a half-completed fade-out over an opaque red layer.
        host.Effects.Run(layer, new FadeOutEffect(1f));
        host.Effects.Advance(0.5f);

        host.RenderToBackbuffer(tick: 0);
        host.Backbuffer.EndFrame();

        using SKImage image = host.Backbuffer.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        SKColor pixel = bitmap.GetPixel(50, 25);

        //Assert - red at roughly half strength over the cleared (black) background.
        pixel.Red.Should().BeGreaterThanOrEqualTo((byte)120);
        pixel.Red.Should().BeLessThanOrEqualTo((byte)135);
        pixel.Green.Should().Be((byte)0);
        pixel.Blue.Should().Be((byte)0);
        pixel.Alpha.Should().Be((byte)255);

        host.Backbuffer.BeginFrame();
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter(int width, int height) : base(width, height) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
