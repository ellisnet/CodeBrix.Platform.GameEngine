using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the port-native <see cref="HealthBar"/>: where it parks itself above its target, how the fill
/// tracks <see cref="HealthBar.Value"/>, the opt-in threshold colours, show/hide, and the two lifetime
/// rules that make it safe in a game loop - it follows the sprite and it dies with the sprite.
/// </summary>
public class HealthBarTests : IDisposable
{
    private const int TileSize = 16;

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
                // A test that disposed its own bar is the normal path here.
            }
        }

        _created.Clear();
        SpriteManager.Instance.ClearImmediate();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private TestRenderSurfaceHost NewHost()
    {
        var host = new TestRenderSurfaceHost();
        _created.Add(host);
        return host;
    }

    private static SceneLayer NewLayer(TestRenderSurfaceHost host) =>
        host.Scene.AddLayer(
            columnCount: 10,
            rowCount: 10,
            width: TileSize,
            height: TileSize,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

    private static Sprite NewSprite(SceneLayer layer, Vector2 gridPosition, string nickname)
    {
        Sprite sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.Nickname = nickname;
        sprite.RenderSize = new Size(TileSize, TileSize);
        sprite.SetPosition(gridPosition);
        return sprite;
    }

    private HealthBar NewBar(TestRenderSurfaceHost host, Sprite sprite, float maxValue = 100f, Size? size = null)
    {
        var bar = new HealthBar(host, sprite, maxValue, size ?? new Size(64, 9), nickname: $"{sprite.Nickname}-bar");
        _created.Add(bar);
        return bar;
    }

    [Fact]
    public void the_bar_starts_full_and_centered_above_its_target()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 3), "centered");

        //Act
        HealthBar bar = NewBar(host, sprite);

        //Assert - centred on a 16 px sprite at world (32, 48), six pixels above it
        bar.Value.Should().Be(100f);
        bar.Fraction.Should().Be(1f);
        bar.TrackBoundsWorld.Should().Be(new Rectangle(8, 33, 64, 9));
        bar.FillBoundsWorld.Should().Be(new Rectangle(10, 35, 60, 5));
        bar.Mode.Should().Be(DirectDrawingMode.SceneLayer);
    }

    [Fact]
    public void the_bar_follows_the_target_when_the_sprite_moves()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 3), "follower");
        HealthBar bar = NewBar(host, sprite);

        //Act
        sprite.SetPosition(new Vector2(4, 5));

        //Assert - the sprite now draws at world (64, 80)
        bar.TrackBoundsWorld.Should().Be(new Rectangle(40, 65, 64, 9));
        bar.FillBoundsWorld.Location.Should().Be(new Point(42, 67));
    }

    [Fact]
    public void OffsetPx_shifts_the_bar_from_its_centered_position()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 3), "offset");
        HealthBar bar = NewBar(host, sprite);

        //Act
        bar.OffsetPx = new Point(5, -4);

        //Assert
        bar.TrackBoundsWorld.Should().Be(new Rectangle(13, 29, 64, 9));
    }

    [Fact]
    public void Value_shrinks_the_fill_and_hides_it_when_empty()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 3), "shrinking");
        HealthBar bar = NewBar(host, sprite);
        bar.Show();

        //Act
        bar.SetValue(50f);

        //Assert - half of the 60 px inner width, and the fill stays anchored inside the track
        bar.FillBoundsWorld.Should().Be(new Rectangle(10, 35, 30, 5));
        bar.Children[1].Visible.Should().BeTrue();

        //Act
        bar.SetValue(0f);

        //Assert - an empty bar draws no fill at all
        bar.FillBoundsWorld.Width.Should().Be(0);
        bar.Children[1].Visible.Should().BeFalse();
    }

    [Fact]
    public void Value_is_clamped_to_the_configured_range()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "clamped");
        HealthBar bar = NewBar(host, sprite);

        //Act + Assert
        bar.SetValue(500f).Value.Should().Be(100f);
        bar.SetValue(-25f).Value.Should().Be(0f);
    }

    [Fact]
    public void MaxValue_reclamps_the_current_value()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "remaxed");
        HealthBar bar = NewBar(host, sprite);

        //Act
        bar.MaxValue = 40f;

        //Assert
        bar.Value.Should().Be(40f);
        bar.Fraction.Should().Be(1f);
    }

    [Fact]
    public void Hide_and_Show_toggle_the_whole_bar()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "toggled");
        HealthBar bar = NewBar(host, sprite);

        //Act
        bar.Hide();

        //Assert
        bar.Visible.Should().BeFalse();

        //Act
        bar.Show();

        //Assert
        bar.Visible.Should().BeTrue();
        bar.Children[1].Visible.Should().BeTrue();
    }

    [Fact]
    public void SetThresholdColors_repaints_the_fill_as_the_bar_empties()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "threshold");
        HealthBar bar = NewBar(host, sprite);
        using var backbuffer = new BitmapBackbuffer(64, 16);

        bar.SetThresholdColors(
            Color.FromArgb(255, 240, 190, 60),
            Color.FromArgb(255, 235, 70, 60));

        //Act - a full bar keeps the normal (green) fill
        SKColor full = DrawFillAndSampleCenter(bar, backbuffer);

        bar.SetValue(10f);

        //Act - a nearly empty bar switches to the critical (red) fill
        SKColor critical = DrawFillAndSampleCenter(bar, backbuffer);

        //Assert
        full.Green.Should().BeGreaterThan(full.Red);
        critical.Red.Should().BeGreaterThan(critical.Green);
    }

    [Fact]
    public void the_bar_disposes_itself_when_its_target_sprite_is_disposed()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 2), "doomed");
        HealthBar bar = NewBar(host, sprite);

        DirectDrawingManager.Instance.GetDirectDrawing("doomed-bar").Should().NotBeNull();

        //Act
        sprite.Dispose();
        SpriteManager.Instance.ClearImmediate();

        //Assert - the bar unregistered itself and took its two rectangles with it
        DirectDrawingManager.Instance.GetDirectDrawing("doomed-bar").Should().BeNull();
        DirectDrawingManager.Instance.GetDirectDrawing("doomed-bar-track").Should().BeNull();
        DirectDrawingManager.Instance.GetDirectDrawing("doomed-bar-fill").Should().BeNull();
        bar.Children.Count.Should().Be(0);
    }

    [Fact]
    public void Dispose_is_idempotent_and_stops_following_the_target()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 2), "detached");
        HealthBar bar = NewBar(host, sprite);
        Rectangle before = bar.TrackBoundsWorld;

        //Act
        bar.Dispose();
        var second = () => bar.Dispose();
        sprite.SetPosition(new Vector2(6, 6));

        //Assert
        second.Should().NotThrow();
        bar.TrackBoundsWorld.Should().Be(before);
    }

    [Fact]
    public void the_constructor_rejects_a_max_value_that_is_not_positive()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "invalid-max");

        //Act
        var act = () => new HealthBar(host, sprite, 0f);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        DirectDrawingManager.Instance.GetDirectDrawing("invalid-max-health").Should().BeNull();
    }

    [Fact]
    public void the_constructor_rejects_a_bar_too_small_to_draw()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "invalid-size");

        //Act
        var act = () => new HealthBar(host, sprite, 10f, new Size(4, 9));

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_layer_constructor_rejects_a_layer_the_target_does_not_belong_to()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        SceneLayer otherLayer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(1, 1), "wrong-layer");

        //Act
        var act = () => new HealthBar(host, otherLayer, sprite, 10f, width: 40, height: 9);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_layer_constructor_places_the_bar_with_plain_pixel_values()
    {
        //Arrange
        var host = NewHost();
        SceneLayer layer = NewLayer(host);
        Sprite sprite = NewSprite(layer, new Vector2(2, 3), "pixel-ctor");

        //Act
        var bar = new HealthBar(host, layer, sprite, 50f, width: 40, height: 9, offsetY: -2, nickname: "pixel-bar");
        _created.Add(bar);

        //Assert
        bar.BarSize.Should().Be(new Size(40, 9));
        bar.TrackBoundsWorld.Should().Be(new Rectangle(20, 31, 40, 9));
    }

    private static SKColor DrawFillAndSampleCenter(HealthBar bar, BitmapBackbuffer backbuffer)
    {
        Rectangle fill = bar.FillBoundsWorld;

        backbuffer.Canvas.Clear(SKColors.White);
        bar.Children[1].Draw(backbuffer, new RectangleF(0f, 0f, fill.Width, fill.Height));

        using SKImage snapshot = backbuffer.Snapshot();
        using SKBitmap result = SKBitmap.FromImage(snapshot);

        return result.GetPixel(fill.Width / 2, fill.Height / 2);
    }
}
