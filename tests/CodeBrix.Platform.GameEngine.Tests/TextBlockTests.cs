using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Verifies that <see cref="TextBlock"/> follows the engine's conventional zoom contract: values above
/// one zoom world-space content in, while view-space text stays expressed directly in screen pixels.
/// Also covers the fluent <see cref="TextBlock.SetPadding"/> and <see cref="TextBlock.SetSize"/> helpers
/// and the <see cref="DirectDrawingBase.CancelReveal"/> primitive.
/// </summary>
public class TextBlockTests : IDisposable
{
    private readonly List<IDisposable> _created = new();

    /// <summary>Disposes everything this fixture registered with the process-global registries.</summary>
    public void Dispose()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
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
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void ResolveTextScale_scene_layer_mode_uses_the_viewport_zoom(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.SceneLayer, viewportZoom);

        //Assert
        scale.Should().Be(viewportZoom);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void ResolveTextScale_view_mode_remains_screen_sized(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.View, viewportZoom);

        //Assert
        scale.Should().Be(1f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ResolveTextScale_invalid_scene_layer_zoom_falls_back_to_one(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.SceneLayer, viewportZoom);

        //Assert
        scale.Should().Be(1f);
    }

    [Fact]
    public void SetPadding_applies_both_values_and_marks_the_layout_dirty()
    {
        //Arrange
        TextBlock block = NewViewTextBlock(new Rectangle(0, 0, 200, 60));
        SetLayoutDirty(block, false);

        //Act
        TextBlock returned = block.SetPadding(7f, 3.5f);

        //Assert
        ReferenceEquals(block, returned).Should().BeTrue();
        block.HorizontalPadding.Should().Be(7f);
        block.VerticalPadding.Should().Be(3.5f);
        IsLayoutDirty(block).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void SetPadding_rejects_negative_or_non_finite_values(float horizontal, float vertical)
    {
        //Arrange
        TextBlock block = NewViewTextBlock(new Rectangle(0, 0, 200, 60));

        //Act
        Action act = () => block.SetPadding(horizontal, vertical);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        block.HorizontalPadding.Should().Be(0f);
        block.VerticalPadding.Should().Be(0f);
    }

    [Fact]
    public void SetSize_view_mode_resizes_the_screen_bounds_and_keeps_the_location()
    {
        //Arrange
        TextBlock block = NewViewTextBlock(new Rectangle(12, 18, 200, 60));
        SetLayoutDirty(block, false);

        //Act
        TextBlock returned = block.SetSize(new Size(120, 40));

        //Assert
        ReferenceEquals(block, returned).Should().BeTrue();
        block.ScreenBounds.Should().Be(new Rectangle(12, 18, 120, 40));
        IsLayoutDirty(block).Should().BeTrue();
    }

    [Fact]
    public void SetSize_scene_layer_mode_resizes_the_world_bounds_and_keeps_the_location()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        var block = new TextBlock(host, layer, view: null, new Rectangle(32, 48, 200, 60), "world-text");
        _created.Add(block);
        SetLayoutDirty(block, false);

        //Act
        TextBlock returned = block.SetSize(new Size(96, 24));

        //Assert
        ReferenceEquals(block, returned).Should().BeTrue();
        block.WorldBounds.Should().Be(new Rectangle(32, 48, 96, 24));
        IsLayoutDirty(block).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-4, 10)]
    public void SetSize_rejects_sizes_that_are_not_positive(int width, int height)
    {
        //Arrange
        TextBlock block = NewViewTextBlock(new Rectangle(0, 0, 200, 60));

        //Act
        Action act = () => block.SetSize(new Size(width, height));

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        block.ScreenBounds.Should().Be(new Rectangle(0, 0, 200, 60));
    }

    [Fact]
    public void CancelReveal_freezes_the_reveal_at_its_current_progress()
    {
        //Arrange
        TextBlock block = NewViewTextBlock(new Rectangle(0, 0, 200, 60));
        long baseTick = HighResTimer.GetCurrentTick();
        block.SetReveal(0f);
        block.RevealTo(1f, durationSec: 1f);
        SetLastTick(block, baseTick);
        block.Update(baseTick + (HighResTimer.TicksPerSecond / 4));
        float progressWhenCancelled = RevealProgress(block);
        progressWhenCancelled.Should().BeApproximately(0.25f, 0.02f);

        //Act
        DirectDrawingBase returned = block.CancelReveal();
        block.Update(baseTick + ((HighResTimer.TicksPerSecond * 3) / 4));

        //Assert
        ReferenceEquals(block, returned).Should().BeTrue();
        IsRevealAnimating(block).Should().BeFalse();
        RevealProgress(block).Should().Be(progressWhenCancelled);
    }

    private TestRenderSurfaceHost NewHost()
    {
        var host = new TestRenderSurfaceHost();
        _created.Add(host);
        return host;
    }

    private static SceneLayer NewLayer(TestRenderSurfaceHost host) =>
        host.Scene.AddLayer(
            columnCount: 8,
            rowCount: 8,
            width: 32,
            height: 32,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

    private TextBlock NewViewTextBlock(Rectangle screenBounds)
    {
        var host = NewHost();
        host.ViewManager.AddView(screenBounds, zOrder: 0);
        View view = host.ViewManager.Views.Single(v => v.Viewport.TargetRectPx == screenBounds);

        var block = new TextBlock(host, view, screenBounds, "view-text");
        _created.Add(block);
        return block;
    }

    private static FieldInfo PrivateField(Type declaringType, string name) =>
        declaringType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SetLayoutDirty(TextBlock block, bool value) =>
        PrivateField(typeof(TextBlock), "_layoutDirty").SetValue(block, value);

    private static bool IsLayoutDirty(TextBlock block) =>
        (bool)PrivateField(typeof(TextBlock), "_layoutDirty").GetValue(block)!;

    private static void SetLastTick(DirectDrawingBase drawing, long tick) =>
        PrivateField(typeof(DirectDrawingBase), "_lastTick").SetValue(drawing, tick);

    private static bool IsRevealAnimating(DirectDrawingBase drawing) =>
        (bool)PrivateField(typeof(DirectDrawingBase), "_revealAnimating").GetValue(drawing)!;

    private static float RevealProgress(DirectDrawingBase drawing) =>
        (float)PrivateField(typeof(DirectDrawingBase), "_revealT").GetValue(drawing)!;
}
