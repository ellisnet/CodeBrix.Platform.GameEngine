using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the three <see cref="DirectRectangle"/> defects fixed upstream after the vendored
/// baseline: the scene-layer constructor passed its world bounds into the screen-bounds slot (so it
/// always threw), the two cached paints were never disposed, and the pattern-fill scale was taken
/// without validation. Also covers the image-fill feature pulled from the same upstream change:
/// <see cref="DirectRectangle.SetFillImage(SKBitmap, DirectRectangle.ImageFillMode, float, SKPoint?, ImageFilterQuality)"/>,
/// its image overload, and <see cref="DirectRectangle.ClearFillImage"/>.
/// </summary>
public class DirectRectangleTests : IDisposable
{
    private readonly List<IDisposable> _created = new();
    private readonly List<Scene> _scenes = new();

    /// <summary>
    /// Disposes every drawing this fixture registered with the process-global
    /// <see cref="DirectDrawingManager"/>, then the scenes and the host.
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
                // A test that disposed its own rectangle is the normal path here.
            }
        }

        _created.Clear();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();
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

    private static SKBitmap NewPatternBitmap()
    {
        var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(new SKColor(10, 20, 30, 255));
        return bitmap;
    }

    private static IntPtr HandleOf(DirectRectangle rectangle, string fieldName)
    {
        var field = typeof(DirectRectangle).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var paint = (SKPaint)field.GetValue(rectangle)!;
        return paint.Handle;
    }

    [Fact]
    public void SceneLayer_mode_constructor_does_not_throw()
    {
        //Arrange
        var host = NewHost();
        var layer = NewLayer();

        //Act - the world bounds used to land in the screen-bounds slot, so this always threw.
        var rectangle = new DirectRectangle(
            Color.Red,
            host,
            layer,
            new Rectangle(4, 8, 32, 16),
            "scene-layer-rectangle");

        _created.Add(rectangle);

        //Assert
        rectangle.Mode.Should().Be(DirectDrawingMode.SceneLayer);
        rectangle.WorldBounds.Should().Be(new Rectangle(4, 8, 32, 16));
        ReferenceEquals(layer, rectangle.SceneLayer).Should().BeTrue();
    }

    [Fact]
    public void Dispose_releases_the_cached_paints()
    {
        //Arrange
        var host = NewHost();
        var rectangle = new DirectRectangle(
            Color.Blue,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(rectangle);
        HandleOf(rectangle, "_fillPaint").Should().NotBe(IntPtr.Zero);

        //Act
        rectangle.Dispose();

        //Assert - the two paints used to be left to the finalizer.
        HandleOf(rectangle, "_fillPaint").Should().Be(IntPtr.Zero);
        HandleOf(rectangle, "_strokePaint").Should().Be(IntPtr.Zero);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        //Arrange
        var host = NewHost();
        var rectangle = new DirectRectangle(
            Color.Blue,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(rectangle);

        //Act
        rectangle.Dispose();
        var second = () => rectangle.Dispose();

        //Assert
        second.Should().NotThrow();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void SetFillPattern_rejects_a_scale_that_is_not_finite_and_positive(float scale)
    {
        //Arrange
        var host = NewHost();
        var rectangle = new DirectRectangle(
            Color.Green,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(rectangle);
        using var bitmap = NewPatternBitmap();

        //Act
        var act = () => rectangle.SetFillPattern(bitmap, scale: scale);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFillPattern_with_a_bad_scale_leaves_the_previous_pattern_in_place()
    {
        //Arrange
        var host = NewHost();
        var rectangle = new DirectRectangle(
            Color.Green,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(rectangle);
        using var bitmap = NewPatternBitmap();
        rectangle.SetFillPattern(bitmap, scale: 2f);

        var shaderField = typeof(DirectRectangle).GetField(
            "_fillShader",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var before = shaderField.GetValue(rectangle);

        //Act
        var act = () => rectangle.SetFillPattern(bitmap, scale: 0f);

        //Assert - the rejected call must not have torn down the working shader.
        act.Should().Throw<ArgumentOutOfRangeException>();
        ReferenceEquals(before, shaderField.GetValue(rectangle)).Should().BeTrue();
    }

    [Fact]
    public void ClearFillPattern_detaches_the_shader_before_disposing_it()
    {
        //Arrange
        var host = NewHost();
        var rectangle = new DirectRectangle(
            Color.Green,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(rectangle);
        using var bitmap = NewPatternBitmap();
        rectangle.SetFillPattern(bitmap);

        //Act
        rectangle.ClearFillPattern();

        //Assert
        var shaderField = typeof(DirectRectangle).GetField(
            "_fillShader",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        shaderField.GetValue(rectangle).Should().BeNull();

        var fillPaintField = typeof(DirectRectangle).GetField(
            "_fillPaint",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        ((SKPaint)fillPaintField.GetValue(rectangle)!).Shader.Should().BeNull();
    }

    private static SKBitmap NewTwoColorBitmap()
    {
        var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Blue);
        return bitmap;
    }

    private static SKBitmap NewSolidBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        return bitmap;
    }

    private static SKBitmap DrawToBitmap(DirectRectangle rectangle, int width, int height)
    {
        using var backbuffer = new BitmapBackbuffer(width, height);
        rectangle.Draw(backbuffer, new RectangleF(0, 0, width, height));

        using var snapshot = backbuffer.Snapshot();
        return SKBitmap.FromImage(snapshot);
    }

    private DirectRectangle NewImageFillRectangle(Color color, Rectangle screenBounds)
    {
        var host = NewHost();
        var rectangle = new DirectRectangle(
            color,
            host,
            host.ViewManager.Views[0],
            screenBounds);

        _created.Add(rectangle);
        return rectangle.SetStrokeWidth(0f);
    }

    [Fact]
    public void SetFillImage_repeat_tiles_the_bitmap_across_the_rectangle()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 6, 4));
        using var source = NewTwoColorBitmap();

        //Act
        rectangle.SetFillImage(
            source,
            DirectRectangle.ImageFillMode.Repeat,
            filterQuality: ImageFilterQuality.None);

        using var result = DrawToBitmap(rectangle, 6, 4);

        //Assert
        result.GetPixel(0, 0).Should().Be(SKColors.Red);
        result.GetPixel(1, 0).Should().Be(SKColors.Blue);
        result.GetPixel(2, 0).Should().Be(SKColors.Red);
        result.GetPixel(5, 3).Should().Be(SKColors.Blue);
    }

    [Fact]
    public void SetFillImage_stretch_fills_the_rectangle_without_repeating()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 4));
        using var source = NewTwoColorBitmap();

        //Act
        rectangle.SetFillImage(
            source,
            DirectRectangle.ImageFillMode.Stretch,
            filterQuality: ImageFilterQuality.None);

        using var result = DrawToBitmap(rectangle, 8, 4);

        //Assert
        result.GetPixel(0, 2).Should().Be(SKColors.Red);
        result.GetPixel(3, 2).Should().Be(SKColors.Red);
        result.GetPixel(4, 2).Should().Be(SKColors.Blue);
        result.GetPixel(7, 2).Should().Be(SKColors.Blue);
    }

    [Fact]
    public void SetFillImage_renders_an_image_source_the_same_way_as_a_bitmap()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 4));
        using var bitmap = NewTwoColorBitmap();
        using var source = SKImage.FromBitmap(bitmap);

        //Act
        rectangle.SetFillImage(
            source,
            DirectRectangle.ImageFillMode.Stretch,
            filterQuality: ImageFilterQuality.None);

        using var result = DrawToBitmap(rectangle, 8, 4);

        //Assert
        result.GetPixel(0, 2).Should().Be(SKColors.Red);
        result.GetPixel(7, 2).Should().Be(SKColors.Blue);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void SetFillImage_rejects_a_scale_that_is_not_finite_and_positive(float scale)
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.Green, new Rectangle(0, 0, 8, 4));
        using var source = NewTwoColorBitmap();

        //Act
        var act = () => rectangle.SetFillImage(source, DirectRectangle.ImageFillMode.Repeat, scale);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFillImage_rejects_a_mode_that_is_not_defined()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.Green, new Rectangle(0, 0, 8, 4));
        using var source = NewTwoColorBitmap();

        //Act
        var act = () => rectangle.SetFillImage(source, (DirectRectangle.ImageFillMode)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetFillImage_replaces_a_previously_configured_pattern_fill()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 4));
        using var pattern = NewPatternBitmap();
        using var source = NewTwoColorBitmap();
        rectangle.SetFillPattern(pattern);

        //Act
        rectangle.SetFillImage(
            source,
            DirectRectangle.ImageFillMode.Stretch,
            filterQuality: ImageFilterQuality.None);

        //Assert - the pattern shader is gone, and the image is what actually renders.
        var shaderField = typeof(DirectRectangle).GetField(
            "_fillShader",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        shaderField.GetValue(rectangle).Should().BeNull();

        using var result = DrawToBitmap(rectangle, 8, 4);
        result.GetPixel(0, 2).Should().Be(SKColors.Red);
        result.GetPixel(7, 2).Should().Be(SKColors.Blue);
    }

    [Theory]
    [InlineData(DirectRectangle.ImageFillMode.Center, 2, 3, true)]
    [InlineData(DirectRectangle.ImageFillMode.Center, 1, 3, false)]
    [InlineData(DirectRectangle.ImageFillMode.Center, 2, 2, false)]
    [InlineData(DirectRectangle.ImageFillMode.Fit, 0, 2, true)]
    [InlineData(DirectRectangle.ImageFillMode.Fit, 0, 1, false)]
    [InlineData(DirectRectangle.ImageFillMode.Fill, 0, 0, true)]
    [InlineData(DirectRectangle.ImageFillMode.Fill, 7, 7, true)]
    [InlineData(DirectRectangle.ImageFillMode.PixelPerfect, 0, 2, true)]
    [InlineData(DirectRectangle.ImageFillMode.PixelPerfect, 0, 1, false)]
    public void SetFillImage_places_the_image_according_to_the_fill_mode(
        DirectRectangle.ImageFillMode mode,
        int x,
        int y,
        bool expectPainted)
    {
        //Arrange - a 4x2 source in an 8x8 rectangle, so every mode lands on whole pixels.
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 8));
        using var source = NewSolidBitmap(4, 2, SKColors.Red);

        //Act
        rectangle.SetFillImage(source, mode, filterQuality: ImageFilterQuality.None);

        using var result = DrawToBitmap(rectangle, 8, 8);

        //Assert
        if (expectPainted)
            result.GetPixel(x, y).Should().Be(SKColors.Red);
        else
            result.GetPixel(x, y).Should().NotBe(SKColors.Red);
    }

    [Fact]
    public void SetFillImage_clips_the_image_to_the_rounded_corners()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 8));
        using var source = NewPatternBitmap();
        var sourceColor = new SKColor(10, 20, 30, 255);

        //Act
        rectangle
            .SetCornerRadius(4f)
            .SetFillImage(
                source,
                DirectRectangle.ImageFillMode.Stretch,
                filterQuality: ImageFilterQuality.None);

        using var result = DrawToBitmap(rectangle, 8, 8);

        //Assert - the middle is painted, the corner is outside the rounded clip.
        result.GetPixel(4, 4).Should().Be(sourceColor);
        result.GetPixel(0, 0).Should().NotBe(sourceColor);
    }

    [Fact]
    public void ClearFillImage_returns_the_rectangle_to_its_solid_color_fill()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.Lime, new Rectangle(0, 0, 8, 4));
        using var source = NewTwoColorBitmap();

        rectangle.SetFillImage(
            source,
            DirectRectangle.ImageFillMode.Stretch,
            filterQuality: ImageFilterQuality.None);

        //Act
        rectangle.ClearFillImage();

        using var result = DrawToBitmap(rectangle, 8, 4);

        //Assert
        var lime = new SKColor(0, 255, 0, 255);
        result.GetPixel(0, 2).Should().Be(lime);
        result.GetPixel(7, 2).Should().Be(lime);
    }

    [Fact]
    public void Dispose_releases_the_image_fill_paint_and_drops_the_caller_owned_source()
    {
        //Arrange
        var rectangle = NewImageFillRectangle(Color.White, new Rectangle(0, 0, 8, 4));
        using var source = NewTwoColorBitmap();

        rectangle.SetFillImage(source, DirectRectangle.ImageFillMode.Stretch);
        HandleOf(rectangle, "_imagePaint").Should().NotBe(IntPtr.Zero);

        //Act
        rectangle.Dispose();

        //Assert - the paint is released, and the caller's bitmap is not disposed with it.
        HandleOf(rectangle, "_imagePaint").Should().Be(IntPtr.Zero);

        var imageFillField = typeof(DirectRectangle).GetField(
            "_imageFillBitmap",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        imageFillField.GetValue(rectangle).Should().BeNull();
        source.Handle.Should().NotBe(IntPtr.Zero);
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
