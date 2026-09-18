using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the post-tile overlay pass of <see cref="BackbufferBase"/>: fog, grid lines and collision
/// boxes. The orthogonal grid outline takes a rectangle fast path instead of building and
/// transforming a polygon, so these tests fence the pixels each overlay combination produces and
/// the early-out that runs when a tile asks for no overlay at all.
/// </summary>
public class BackbufferBaseTests
{
    private const int TileSize = 16;
    private const int SurfaceWidth = 64;
    private const int SurfaceHeight = 48;

    [Fact]
    public void DrawDrawables_draws_no_overlay_when_the_layer_asks_for_none()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize);
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.Black);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert - no fog, no grid lines, no collision boxes: the cleared surface is untouched.
        using var bitmap = SnapshotOf(backbuffer);
        CountOfPixelsThatAreNot(bitmap, SKColors.Black).Should().Be(0);
    }

    [Fact]
    public void DrawDrawables_outlines_an_orthogonal_cell_without_painting_its_interior()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize);
        layer.ShowGridLines = true;
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.Black);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert - the cell spanning (16,16)-(32,32) is outlined on all four edges; a one-pixel
        //  stroke straddles the boundary, so accept either pixel either side of it.
        using var bitmap = SnapshotOf(backbuffer);
        HasColorInColumns(bitmap, SKColors.White, firstX: 15, lastX: 16, y: 24).Should().BeTrue();
        HasColorInColumns(bitmap, SKColors.White, firstX: 31, lastX: 32, y: 24).Should().BeTrue();
        HasColorInRows(bitmap, SKColors.White, firstY: 15, lastY: 16, x: 24).Should().BeTrue();
        HasColorInRows(bitmap, SKColors.White, firstY: 31, lastY: 32, x: 24).Should().BeTrue();
        bitmap.GetPixel(24, 24).Should().Be(SKColors.Black);
    }

    [Fact]
    public void DrawDrawables_draws_grid_lines_for_a_non_orthogonal_layer()
    {
        //Arrange - hexagonal cells are not rectangles, so the polygon outline path is the one
        //  that has to keep working.
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize,
            coordinateSystem: CoordinateSystemTypes.HexAxialFlatTop);
        layer.ShowGridLines = true;
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.Black);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert
        using var bitmap = SnapshotOf(backbuffer);
        CountOfPixelsThatAreNot(bitmap, SKColors.Black).Should().BeGreaterThan(0);
    }

    [Fact]
    public void DrawDrawables_draws_collision_boxes_alongside_orthogonal_grid_lines()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize);
        layer.ShowGridLines = true;
        layer.ShowCollisionBoxes = true;
        layer[1, 1]!.CollisionsEnabled = true;
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.Black);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert - the grid fast path still has to draw the collision box it would otherwise skip.
        using var bitmap = SnapshotOf(backbuffer);
        CountOfColor(bitmap, SKColors.Green).Should().BeGreaterThan(0);
    }

    [Fact]
    public void DrawDrawables_draws_collision_boxes_when_grid_lines_are_off()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize);
        layer.ShowCollisionBoxes = true;
        layer[1, 1]!.CollisionsEnabled = true;
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.Black);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert
        using var bitmap = SnapshotOf(backbuffer);
        CountOfColor(bitmap, SKColors.Green).Should().BeGreaterThan(0);
        CountOfColor(bitmap, SKColors.White).Should().Be(0);
    }

    [Fact]
    public void DrawDrawables_fogs_the_tile_that_enables_fog()
    {
        //Arrange
        using var scene = new Scene();
        var layer = scene.AddLayer(4, 3, TileSize, TileSize);
        layer[1, 1]!.EnableFog = true;
        var view = CreateView(scene);
        using var backbuffer = CreateClearedBackbuffer(SKColors.White);

        //Act
        DrawLayer(backbuffer, view, layer);

        //Assert - fog is a half-transparent black polygon over the fogged cell only.
        using var bitmap = SnapshotOf(backbuffer);
        SKColor fogged = bitmap.GetPixel(24, 24);
        fogged.Should().NotBe(SKColors.White);
        fogged.Red.Should().BeInRange((byte)100, (byte)160);
        bitmap.GetPixel(8, 8).Should().Be(SKColors.White);
    }

    private static BitmapBackbuffer CreateClearedBackbuffer(SKColor clearColor)
    {
        var backbuffer = new BitmapBackbuffer(SurfaceWidth, SurfaceHeight)
        {
            ClearColor = clearColor
        };

        backbuffer.BeginFrame();
        backbuffer.ClearRect(new Rectangle(0, 0, SurfaceWidth, SurfaceHeight));

        return backbuffer;
    }

    private static void DrawLayer(BackbufferBase backbuffer, View view, SceneLayer layer)
    {
        backbuffer.DrawDrawables(view, TilesOf(layer), new Rectangle(0, 0, SurfaceWidth, SurfaceHeight));
        backbuffer.EndFrame();
    }

    private static List<IDrawable> TilesOf(SceneLayer layer)
    {
        var drawables = new List<IDrawable>();

        for (int row = 0; row < layer.GridRowCount; row++)
        {
            for (int column = 0; column < layer.GridColumnCount; column++)
            {
                var tile = layer[column, row];

                if (tile is not null)
                    drawables.Add(tile);
            }
        }

        return drawables;
    }

    private static View CreateView(Scene scene)
    {
        var viewport = new Viewport
        {
            TargetRectPx = new Rectangle(0, 0, SurfaceWidth, SurfaceHeight),
            Zoom = 1f
        };

        var camera = new Camera(scene);
        var view = new View(camera, viewport);
        camera.SnapTo(System.Drawing.PointF.Empty);

        return view;
    }

    private static SKBitmap SnapshotOf(BackbufferBase backbuffer)
    {
        using var image = backbuffer.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static bool HasColorInColumns(SKBitmap bitmap, SKColor color, int firstX, int lastX, int y)
    {
        for (int x = firstX; x <= lastX; x++)
        {
            if (bitmap.GetPixel(x, y) == color)
                return true;
        }

        return false;
    }

    private static bool HasColorInRows(SKBitmap bitmap, SKColor color, int firstY, int lastY, int x)
    {
        for (int y = firstY; y <= lastY; y++)
        {
            if (bitmap.GetPixel(x, y) == color)
                return true;
        }

        return false;
    }

    private static int CountOfColor(SKBitmap bitmap, SKColor color)
    {
        int count = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == color)
                    count++;
            }
        }

        return count;
    }

    private static int CountOfPixelsThatAreNot(SKBitmap bitmap, SKColor color)
    {
        int count = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != color)
                    count++;
            }
        }

        return count;
    }
}
