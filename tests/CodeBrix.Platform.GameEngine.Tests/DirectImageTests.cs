using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="DirectImage"/> resource ownership: the drawing owns its cached paint, the
/// caller owns the image it hands in.
/// </summary>
public class DirectImageTests : IDisposable
{
    private readonly List<IDisposable> _created = new();

    /// <summary>
    /// Disposes every drawing this fixture registered with the process-global
    /// <see cref="DirectDrawingManager"/>, then the host.
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
                // A test that disposed its own drawing is the normal path here.
            }
        }

        _created.Clear();
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

    private static SKImage NewImage()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(new SKColor(10, 20, 30, 255));
        return SKImage.FromBitmap(bitmap);
    }

    private static IntPtr PaintHandleOf(DirectImage image)
    {
        var field = typeof(DirectImage).GetField(
            "_paint",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var paint = (SKPaint)field.GetValue(image)!;
        return paint.Handle;
    }

    [Fact]
    public void Dispose_releases_the_cached_paint_but_not_the_caller_owned_image()
    {
        //Arrange
        var host = NewHost();
        using var source = NewImage();
        var image = new DirectImage(
            source,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(image);
        PaintHandleOf(image).Should().NotBe(IntPtr.Zero);

        //Act
        image.Dispose();

        //Assert - the paint used to be left to the finalizer; the image is still the caller's.
        PaintHandleOf(image).Should().Be(IntPtr.Zero);
        source.Handle.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        //Arrange
        var host = NewHost();
        using var source = NewImage();
        var image = new DirectImage(
            source,
            host,
            host.ViewManager.Views[0],
            new Rectangle(0, 0, 16, 16));

        _created.Add(image);
        image.Dispose();

        //Act
        var secondDispose = () => image.Dispose();

        //Assert
        secondDispose.Should().NotThrow();
        PaintHandleOf(image).Should().Be(IntPtr.Zero);
    }

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
