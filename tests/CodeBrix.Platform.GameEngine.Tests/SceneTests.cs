using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="Scene.AddPixelLayer"/>: a layer with no tile grid, sized in world pixels, that carries
/// sprites and scene-layer direct drawings for a game without a tile map. Also covers the global scene
/// registry: the shared <see cref="Scene.Empty"/> placeholder is never listed or saved.
/// </summary>
public class SceneTests : IDisposable
{
    private readonly TestRenderSurfaceHost _host = new();
    private readonly string _savePath = Path.Combine(Path.GetTempPath(), $"ge_scene_{Guid.NewGuid():N}.json");

    /// <summary>Clears the process-global registries this fixture populated.</summary>
    public void Dispose()
    {
        _host.Dispose();
        File.Delete(_savePath);
        SpriteManager.Instance.ClearImmediate();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddPixelLayer_adds_a_layer_as_large_as_the_given_pixel_size()
    {
        //Act
        SceneLayer layer = _host.Scene.AddPixelLayer(1280, 720, zOrder: 3, parallax: 0.5f);

        //Assert
        layer.IsPixelLayer.Should().BeTrue();
        layer.Scene.Should().BeSameAs(_host.Scene);
        _host.Scene.SceneLayers.Should().Contain(layer);
        layer.ZOrder.Should().Be(3);
        layer.Parallax.Should().Be(0.5f);
        layer.ShowGridLines.Should().BeFalse();
        layer.GetLayerBoundsPx().Should().Be(new RectangleF(0, 0, 1280, 720));
        _host.Scene.GetWorldBoundsPx().Should().Be(new RectangleF(0, 0, 1280, 720));
    }

    [Fact]
    public void AddLayer_still_makes_ordinary_tile_layers() =>
        _host.Scene.AddLayer(4, 4, 16, 16).IsPixelLayer.Should().BeFalse();

    [Fact]
    public void AddPixelLayer_rejects_a_size_that_is_not_positive()
    {
        //Act
        Action noWidth = () => _host.Scene.AddPixelLayer(0, 720);
        Action negativeHeight = () => _host.Scene.AddPixelLayer(1280, -1);

        //Assert
        noWidth.Should().Throw<ArgumentOutOfRangeException>();
        negativeHeight.Should().Throw<ArgumentOutOfRangeException>();
        _host.Scene.Count.Should().Be(0);
    }

    [Fact]
    public void a_pixel_layer_draws_no_tiles_but_its_drawings()
    {
        //Arrange
        SceneLayer layer = _host.Scene.AddPixelLayer(320, 200);
        var rectangle = new DirectRectangle(Color.Red, _host, layer, new Rectangle(10, 10, 20, 20), "on-pixel-layer");
        rectangle.Visible = true;

        try
        {
            //Act
            var drawables = layer.GetDrawablesInWorldRect(new Rectangle(0, 0, 320, 200));

            //Assert
            drawables.OfType<SceneLayerTile>().Should().BeEmpty();
            drawables.Should().Contain(rectangle);
        }
        finally
        {
            rectangle.Dispose();
        }
    }

    [Fact]
    public void a_left_top_aligned_sprite_is_placed_by_world_pixels_on_a_pixel_layer()
    {
        //Arrange
        SceneLayer layer = _host.Scene.AddPixelLayer(320, 200);
        Sprite sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.RenderSize = new Size(16, 16);
        sprite.HorizAlign = HorizontalAlignment.Left;
        sprite.VertAlign = VerticalAlignment.Top;

        //Act
        sprite.SetPosition(layer.WorldPxToGrid(new PointF(160f, 100f)).ToVector2());

        //Assert
        layer.GridToWorldPx(PointF.Empty).Should().Be(PointF.Empty);
        sprite.DrawLocationWorld.Location.Should().Be(new Point(160, 100));
    }

    [Fact]
    public void particles_and_health_bars_live_on_a_pixel_layer()
    {
        //Arrange
        SceneLayer layer = _host.Scene.AddPixelLayer(320, 200);

        //Act
        using var particles = new ParticleSurface(_host, layer, new Rectangle(0, 0, 320, 200), "pixel-particles");
        using var bar = new HealthBar(_host, layer, new PointF(160f, 40f), 10f, nickname: "pixel-bar");

        //Assert
        particles.SceneLayer.Should().BeSameAs(layer);
        bar.SceneLayer.Should().BeSameAs(layer);
    }

    [Fact]
    public void GetAllScenes_never_lists_the_empty_placeholder_scene()
    {
        //Act
        var empty = Scene.Empty;

        //Assert
        Scene.GetAllScenes().Should().NotContain(empty);
        Scene.GetAllSceneIDs().Should().NotContain(empty.ID);
    }

    [Fact]
    public void a_save_and_load_carries_only_the_games_own_scenes()
    {
        //Arrange - the fixture's host scene is cleared so only one scene of the game's own remains.
        Scene.ClearAllScenes();
        var scene = new Scene { ID = "scene-only" };
        scene.AddLayer(columnCount: 2, rowCount: 2, width: 8, height: 8);

        //Act
        Engine.Instance.State.SaveToFile(_savePath);
        EngineState.LoadFromFile(_savePath);

        //Assert
        Scene.GetAllSceneIDs().Should().Equal("scene-only");
    }
}
