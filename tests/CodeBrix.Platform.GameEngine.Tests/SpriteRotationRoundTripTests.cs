using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Guards the save-graph contract for <see cref="Sprite.Rotation"/>: it is a public
/// read/write property, so the engine's save contract resolver persists it, while the
/// derived <see cref="Sprite.VisualBoundsWorld"/> is getter-only and stays out of the file.
/// </summary>
public class SpriteRotationRoundTripTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary working directory and clears the engine registries.</summary>
    public SpriteRotationRoundTripTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_rotation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        ClearAllEngineState();
    }

    /// <summary>Clears the engine registries and removes the temporary working directory.</summary>
    public void Dispose()
    {
        ClearAllEngineState();

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            /* best effort */
        }

        GC.SuppressFinalize(this);
    }

    private static void ClearAllEngineState()
    {
        Assets.AssetsFile.ClearAll();
        TilesheetRegistry.Instance.Clear();
        Cycle.ClearAllAnimationCycles();
        Scene.ClearAllScenes();
        SpriteManager.Instance.ClearImmediate();
        AudioResourceManager.Instance.Clear();
    }

    [Fact]
    public void Rotation_survives_a_save_and_load_cycle()
    {
        //Arrange - one layer, one rotated sprite, one unrotated sprite.
        var imagePath = WriteTilesheetPng("sheet.png", tileSize: 16, columns: 2, rows: 2);
        var savePath = Path.Combine(_workDirectory, "rotation.json");

        var sheet = TilesheetRegistry.Instance.LoadFromImageFile("rotation_sheet", imagePath);
        sheet.DefaultRegion.TileSize = new Size(16, 16);

        var scene = new Scene { ID = "scene-rotation" };
        var layer = scene.AddLayer(
            columnCount: 4,
            rowCount: 4,
            width: 16,
            height: 16,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        var turned = SpriteManager.Instance.CreateSprite(layer, new Frame(sheet, 0, 0), "turned");
        turned.SetPosition(new Vector2(1f, 2f));
        turned.Rotation = 33.5f;

        var straight = SpriteManager.Instance.CreateSprite(layer, new Frame(sheet, 1, 0), "straight");
        straight.SetPosition(new Vector2(2f, 2f));

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        EngineState.LoadFromFile(savePath);

        //Assert - the angle came back, and the loaded sprite's visual bounds follow from it.
        var loadedTurned = SpriteManager.Instance.AllSprites.FirstOrDefault(s => s.Nickname == "turned");
        loadedTurned.Should().NotBeNull();
        loadedTurned!.Rotation.Should().BeApproximately(33.5f, 0.0001f);
        loadedTurned.VisualBoundsWorld.Contains(loadedTurned.DrawLocationWorld).Should().BeTrue();
        (loadedTurned.VisualBoundsWorld.Width > loadedTurned.DrawLocationWorld.Width).Should().BeTrue();

        var loadedStraight = SpriteManager.Instance.AllSprites.FirstOrDefault(s => s.Nickname == "straight");
        loadedStraight.Should().NotBeNull();
        loadedStraight!.Rotation.Should().Be(0f);
        loadedStraight.VisualBoundsWorld.Should().Be(loadedStraight.DrawLocationWorld);
    }

    [Fact]
    public void Save_file_carries_Rotation_but_not_the_derived_visual_bounds()
    {
        //Arrange
        var imagePath = WriteTilesheetPng("sheet.png", tileSize: 16, columns: 2, rows: 2);
        var savePath = Path.Combine(_workDirectory, "rotation-members.json");

        var sheet = TilesheetRegistry.Instance.LoadFromImageFile("rotation_sheet", imagePath);
        sheet.DefaultRegion.TileSize = new Size(16, 16);

        var scene = new Scene { ID = "scene-rotation-members" };
        var layer = scene.AddLayer(columnCount: 2, rowCount: 2, width: 16, height: 16);

        var turned = SpriteManager.Instance.CreateSprite(layer, new Frame(sheet, 0, 0), "turned");
        turned.Rotation = 12f;

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        var json = File.ReadAllText(savePath);

        //Assert
        json.Contains("\"Rotation\"").Should().BeTrue();
        json.Contains("VisualBoundsWorld").Should().BeFalse();
    }

    private string WriteTilesheetPng(string fileName, int tileSize, int columns, int rows)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using var bitmap = new SKBitmap(tileSize * columns, tileSize * rows);

        using (var canvas = new SKCanvas(bitmap))
        {
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    using var paint = new SKPaint();
                    paint.Color = new SKColor((byte)((40 * x) + 40), (byte)((40 * y) + 40), 200);
                    canvas.DrawRect(x * tileSize, y * tileSize, tileSize, tileSize, paint);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }
}
