using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="DrawListDrawing"/>: which snapshot each render tier paints, the dirty marking for the CPU tier's
/// dirty-rectangle path, and the pixels of each command kind (fit, rotation, alpha, outlines, text, clipping and the
/// world-to-screen mapping of a scene-layer drawing on a pixel layer).
/// </summary>
public class DrawListDrawingTests : IDisposable
{
    private const int Size = 64;

    private static readonly SKColor Background = SKColors.Black;

    private readonly FakeRenderSurfaceAdapter _adapter = new();
    private readonly RenderSurfaceHost<BitmapBackbuffer> _host;
    private readonly Scene _scene = new();
    private readonly List<IDisposable> _created = new();
    private readonly List<string> _fontKeys = new();
    private long _tick = HighResTimer.GetCurrentTick();

    /// <summary>
    /// Builds a host with one full view, and makes this thread the engine thread so refresh-queue marking runs inline.
    /// </summary>
    public DrawListDrawingTests()
    {
        Engine.Instance.EngineDispatcher.BindToCurrentThread();
        Engine.Instance.EngineDispatcher.Drain();
        _host = new RenderSurfaceHost<BitmapBackbuffer>(_adapter);
        _host.ViewManager.ConfigureSingleFullView();
    }

    /// <summary>Disposes every drawing, the scene, the host and the fonts, and releases the engine-thread identity.</summary>
    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
            _created[i].Dispose();

        foreach (var key in _fontKeys)
            FontManager.Instance.Remove(key);

        _scene.Dispose();
        Scene.ClearAllScenes();
        _host.Dispose();
        _adapter.Dispose();

        var releaser = new Thread(() => Engine.Instance.EngineDispatcher.BindToCurrentThread());
        releaser.Start();
        releaser.Join();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void the_cpu_tier_paints_the_snapshot_taken_by_Update_not_a_newer_one()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(32, 32, 20, 20, SKColors.Red);
        list.Publish();

        //Act
        var beforeUpdate = PixelAt(drawing, 32, 32);
        Update(drawing);
        var afterUpdate = PixelAt(drawing, 32, 32);

        //Assert
        beforeUpdate.Should().Be(Background);
        afterUpdate.Should().Be(SKColors.Red);
        drawing.Current.Should().BeSameAs(list.Published);
    }

    [Fact]
    public void the_gpu_tier_paints_the_latest_published_snapshot()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(32, 32, 20, 20, SKColors.Red);
        list.Publish();
        using var gpu = new GpuBackbuffer(Size, Size);

        //Act - no Update: the UI thread reads the newest snapshot directly
        var pixel = PixelAt(drawing, gpu, 32, 32);

        //Assert
        pixel.Should().Be(SKColors.Red);
    }

    [Fact]
    public void Update_marks_the_drawing_dirty_only_when_a_new_snapshot_was_published()
    {
        //Arrange
        var layer = _scene.AddPixelLayer(Size, Size);
        var list = new DrawList();
        var drawing = Track(new DrawListDrawing(_host, layer, new Rectangle(0, 0, Size, Size), list));
        layer.RefreshQueue.ClearRefreshQueue();

        //Act
        Update(drawing);
        var dirtyWithNothingNew = layer.RefreshQueue.IsDirty;
        list.Publish();
        Update(drawing);
        var dirtyAfterPublish = layer.RefreshQueue.IsDirty;
        layer.RefreshQueue.ClearRefreshQueue();
        Update(drawing);
        var dirtyAgain = layer.RefreshQueue.IsDirty;

        //Assert
        dirtyWithNothingNew.Should().BeFalse();
        dirtyAfterPublish.Should().BeTrue();
        dirtyAgain.Should().BeFalse();
    }

    [Fact]
    public void a_scene_layer_drawing_on_a_pixel_layer_maps_world_pixels_into_the_destination()
    {
        //Arrange - the drawing covers world (100, 100)-(164, 164) and is drawn into the top-left 64 x 64 pixels
        var layer = _scene.AddPixelLayer(400, 400);
        var list = new DrawList();
        var drawing = Track(new DrawListDrawing(_host, layer, new Rectangle(100, 100, Size, Size), list));
        list.Rectangle(132, 132, 8, 8, SKColors.Red);
        list.Publish();
        Update(drawing);

        //Act
        var center = PixelAt(drawing, 32, 32);
        var corner = PixelAt(drawing, 4, 4);

        //Assert
        center.Should().Be(SKColors.Red);
        corner.Should().Be(Background);
    }

    [Fact]
    public void a_destination_larger_than_the_bounds_scales_the_list_with_it()
    {
        //Arrange - bounds are 64 x 64, the destination 128 x 128 (a 2x camera zoom)
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(16, 16, 8, 8, SKColors.Red);
        list.Publish();
        Update(drawing);
        using var backbuffer = new BitmapBackbuffer(128, 128);

        //Act
        var scaled = PixelAt(drawing, backbuffer, 32, 32, new RectangleF(0, 0, 128, 128));
        var outside = PixelAt(drawing, backbuffer, 16, 16, new RectangleF(0, 0, 128, 128));

        //Assert
        scaled.Should().Be(SKColors.Red);
        outside.Should().Be(Background);
    }

    [Fact]
    public void painting_is_clipped_to_the_destination()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(32, 32, 200, 200, SKColors.Red);
        list.Publish();
        Update(drawing);
        using var backbuffer = new BitmapBackbuffer(Size, Size);

        //Act
        var inside = PixelAt(drawing, backbuffer, 10, 10, new RectangleF(0, 0, 32, 32));
        var outside = PixelAt(drawing, backbuffer, 48, 48, new RectangleF(0, 0, 32, 32));

        //Assert
        inside.Should().Be(SKColors.Red);
        outside.Should().Be(Background);
    }

    [Fact]
    public void an_image_keeps_its_aspect_inside_its_box_unless_stretched()
    {
        //Arrange - a 2:1 green picture in a 32 x 32 box centred at (32, 32): contained it spans y 24..40
        using var bitmap = new SKBitmap(8, 4);
        bitmap.Erase(SKColors.Lime);
        using var image = SKImage.FromBitmap(bitmap);
        var list = new DrawList();
        var drawing = NewViewDrawing(list);

        //Act
        list.Image(image, 32, 32, 32, 32);
        list.Publish();
        Update(drawing);
        var containedAbove = PixelAt(drawing, 32, 20);
        var containedMiddle = PixelAt(drawing, 32, 32);
        list.Clear();
        list.Image(image, 32, 32, 32, 32, fit: DrawImageFit.Stretch);
        list.Publish();
        Update(drawing);
        var stretchedAbove = PixelAt(drawing, 32, 20);

        //Assert
        containedAbove.Should().Be(Background);
        containedMiddle.Should().Be(SKColors.Lime);
        stretchedAbove.Should().Be(SKColors.Lime);
    }

    [Fact]
    public void rotation_turns_a_command_about_its_centre()
    {
        //Arrange - a 40 x 4 bar at the centre, turned 90 degrees, becomes a 4 x 40 bar
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(32, 32, 40, 4, SKColors.Red, rotation: 90);
        list.Publish();
        Update(drawing);

        //Act
        var alongNewLength = PixelAt(drawing, 32, 16);
        var alongOldLength = PixelAt(drawing, 16, 32);

        //Assert
        alongNewLength.Should().Be(SKColors.Red);
        alongOldLength.Should().Be(Background);
    }

    [Fact]
    public void alpha_fades_a_command_over_what_is_under_it()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Rectangle(32, 32, 20, 20, SKColors.White, alpha: 0.5);
        list.Publish();
        Update(drawing);

        //Act
        var pixel = PixelAt(drawing, 32, 32);

        //Assert
        ((int)pixel.Red).Should().BeInRange(120, 135);
    }

    [Fact]
    public void a_circle_with_only_an_outline_leaves_its_middle_empty()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Circle(32, 32, 20, SKColors.Transparent, SKColors.Red, strokeWidth: 4);
        list.Publish();
        Update(drawing);

        //Act
        var middle = PixelAt(drawing, 32, 32);
        var rim = PixelAt(drawing, 52, 32);

        //Assert
        middle.Should().Be(Background);
        rim.Red.Should().BeGreaterThan((byte)200);
    }

    [Fact]
    public void text_is_drawn_on_the_side_of_its_anchor_the_alignment_says()
    {
        //Arrange
        var typeface = RegisterFont("draw-list-drawing-text");
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        list.Text("MMMM", 32, 32, "draw-list-drawing-text", 16, SKColors.White, SKTextAlign.Left);
        list.Publish();
        Update(drawing);

        //Act
        var (left, right) = LitColumnsEitherSideOf(drawing, 32);

        //Assert
        typeface.Should().NotBeNull();
        left.Should().Be(0);
        right.Should().BeGreaterThan(0);
    }

    [Fact]
    public void many_text_sizes_keep_the_font_cache_bounded()
    {
        //Arrange
        var list = new DrawList();
        var drawing = NewViewDrawing(list);
        for (var size = 1; size <= 100; size++)
            list.Text("x", 32, 32, SKTypeface.Default, size, SKColors.White);
        list.Publish();
        Update(drawing);

        //Act
        PixelAt(drawing, 0, 0);

        //Assert
        FontCacheCount(drawing).Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public void a_source_returning_null_paints_nothing()
    {
        //Arrange
        var drawing = Track(new DrawListDrawing(_host, _host.ViewManager.Views[0], new Rectangle(0, 0, Size, Size),
            () => null));

        //Act
        Update(drawing);
        var pixel = PixelAt(drawing, 32, 32);

        //Assert
        drawing.Current.Should().BeSameAs(DrawListSnapshot.Empty);
        pixel.Should().Be(Background);
    }

    [Fact]
    public void a_source_function_can_hand_over_a_snapshot_the_game_made_itself()
    {
        //Arrange
        var own = new DrawListSnapshot(new[] { DrawCommand.ForRectangle(32, 32, 10, 10, SKColors.Blue) });
        var drawing = Track(new DrawListDrawing(_host, _host.ViewManager.Views[0], new Rectangle(0, 0, Size, Size),
            () => own));

        //Act
        Update(drawing);
        var pixel = PixelAt(drawing, 32, 32);

        //Assert
        pixel.Should().Be(SKColors.Blue);
    }

    [Fact]
    public void the_constructors_reject_a_null_list_or_source_without_registering_anything()
    {
        //Arrange
        var view = _host.ViewManager.Views[0];
        var bounds = new Rectangle(0, 0, Size, Size);
        Action nullList = () => _ = new DrawListDrawing(_host, view, bounds, (DrawList)null!);
        Action nullSource = () => _ = new DrawListDrawing(_host, view, bounds, (Func<DrawListSnapshot?>)null!);
        var registered = DirectDrawingManager.Instance.Count;

        //Act + Assert
        nullList.Should().Throw<ArgumentNullException>();
        nullSource.Should().Throw<ArgumentNullException>();
        DirectDrawingManager.Instance.Count.Should().Be(registered);
    }

    [Fact]
    public void FilterQuality_defaults_to_linear_sampling_without_mipmaps()
    {
        //Arrange
        var drawing = NewViewDrawing(new DrawList());

        //Act
        var quality = drawing.FilterQuality;

        //Assert
        quality.Should().Be(ImageFilterQuality.Low);
    }

    private DrawListDrawing NewViewDrawing(DrawList list) =>
        Track(new DrawListDrawing(_host, _host.ViewManager.Views[0], new Rectangle(0, 0, Size, Size), list));

    private T Track<T>(T disposable) where T : IDisposable
    {
        _created.Add(disposable);
        return disposable;
    }

    private void Update(DrawListDrawing drawing)
    {
        _tick += HighResTimer.TicksPerSecond / 60;
        drawing.Update(_tick);
    }

    private static SKColor PixelAt(DrawListDrawing drawing, int x, int y)
    {
        using var backbuffer = new BitmapBackbuffer(Size, Size);
        return PixelAt(drawing, backbuffer, x, y, new RectangleF(0, 0, Size, Size));
    }

    private static SKColor PixelAt(DrawListDrawing drawing, BackbufferBase backbuffer, int x, int y) =>
        PixelAt(drawing, backbuffer, x, y, new RectangleF(0, 0, Size, Size));

    private static SKColor PixelAt(DrawListDrawing drawing, BackbufferBase backbuffer, int x, int y, RectangleF destination)
    {
        using var bitmap = Render(drawing, backbuffer, destination);
        return bitmap.GetPixel(x, y);
    }

    private static SKBitmap Render(DrawListDrawing drawing, BackbufferBase backbuffer, RectangleF destination)
    {
        backbuffer.Canvas.Clear(Background);
        drawing.Draw(backbuffer, destination);
        using var snapshot = backbuffer.Snapshot();
        return SKBitmap.FromImage(snapshot);
    }

    private static (int Left, int Right) LitColumnsEitherSideOf(DrawListDrawing drawing, int anchorX)
    {
        using var backbuffer = new BitmapBackbuffer(Size, Size);
        using var bitmap = Render(drawing, backbuffer, new RectangleF(0, 0, Size, Size));
        var left = 0;
        var right = 0;
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                if (bitmap.GetPixel(x, y).Red < 64)
                    continue;

                if (x < anchorX - 1)
                    left++;
                else if (x > anchorX)
                    right++;
            }
        }

        return (left, right);
    }

    private static int FontCacheCount(DrawListDrawing drawing)
    {
        var field = typeof(DrawListDrawing).GetField("_fonts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(drawing)!).Count;
    }

    private SKTypeface RegisterFont(string key)
    {
        byte[]? bytes = null;
        try
        {
            using var fontStream = SKTypeface.Default.OpenStream(out _);
            if (fontStream is not null && fontStream.Length > 0)
            {
                using var data = SKData.Create(fontStream);
                bytes = data?.ToArray();
            }
        }
        catch (Exception)
        {
            bytes = null;
        }

        if (bytes is not { Length: > 0 })
            Assert.Skip("No font data is available on this machine.");

        _fontKeys.Add(key);
        return FontManager.Instance.LoadFromBytes(key, bytes!);
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(Size, Size) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
