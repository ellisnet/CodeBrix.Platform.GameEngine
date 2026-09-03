using System;
using System.Drawing;
using System.IO;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers tilesheet image persistence: a bitmap-only tilesheet can be promoted to a file-backed
/// one with <see cref="Tilesheet.PersistImageToFile"/>, saving such a sheet as a .gts file does
/// that promotion automatically using a sibling PNG, an already file-backed sheet is left alone,
/// and a masked sheet persists its untransformed source bitmap so the stored mask is applied
/// exactly once when the definition is loaded again.
/// </summary>
public class TilesheetDefinitionSerializerTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary directory the fixture writes its image and .gts files into.</summary>
    public TilesheetDefinitionSerializerTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_gtspersist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
    }

    /// <summary>Removes the temporary directory.</summary>
    public void Dispose()
    {
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

    private string CreateImageFile(string fileName, int width, int height)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(40, 80, 120, 255));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        File.WriteAllBytes(path, data.ToArray());

        return path;
    }

    [Fact]
    public void PersistImageToFile_promotes_a_bitmap_backed_tilesheet_to_file_backed()
    {
        //Arrange
        var imagePath = Path.Combine(_workDirectory, "persisted", "generated.png");
        var bitmap = new SKBitmap(24, 12);
        bitmap.Erase(new SKColor(10, 20, 30, 255));

        using var tilesheet = TilesheetFactory.FromBitmap("Generated", bitmap);

        //Act
        tilesheet.PersistImageToFile(imagePath);

        //Assert
        tilesheet.ImageFilePath.Should().Be(Path.GetFullPath(imagePath));
        tilesheet.AssetIdentifier.Should().BeNull();
        File.Exists(imagePath).Should().BeTrue();

        using var persistedBitmap = SKBitmap.Decode(imagePath);
        persistedBitmap.Should().NotBeNull();
        persistedBitmap.Width.Should().Be(24);
        persistedBitmap.Height.Should().Be(12);

        var definition = TilesheetDefinitionSerializer.FromTilesheet(tilesheet);
        definition.Image.FilePath.Should().Be(Path.GetFullPath(imagePath).Replace('\\', '/'));
    }

    [Fact]
    public void Save_persists_a_sibling_image_for_a_bitmap_backed_tilesheet()
    {
        //Arrange
        var gtsPath = Path.Combine(_workDirectory, "generated", "terrain.gts");
        var expectedImagePath = Path.ChangeExtension(gtsPath, ".png");

        var bitmap = new SKBitmap(32, 16);
        bitmap.Erase(new SKColor(40, 80, 120, 255));

        using var tilesheet = TilesheetFactory.FromBitmap("Terrain", bitmap);
        tilesheet.DefaultRegion.TileSize = new Size(16, 16);

        //Act
        TilesheetDefinitionSerializer.Save(gtsPath, tilesheet);

        //Assert
        File.Exists(gtsPath).Should().BeTrue();
        File.Exists(expectedImagePath).Should().BeTrue();
        tilesheet.ImageFilePath.Should().Be(Path.GetFullPath(expectedImagePath));

        var definition = TilesheetDefinitionSerializer.Load(gtsPath);
        definition.Image.FilePath.Should().Be("terrain.png");

        using var restored = TilesheetFactory.FromDefinition(definition, Path.GetDirectoryName(gtsPath));
        restored.Name.Should().Be(tilesheet.Name);
        restored.DefaultRegion.TileSize.Should().Be(tilesheet.DefaultRegion.TileSize);
    }

    [Fact]
    public void Save_does_not_create_a_sibling_image_for_a_file_backed_tilesheet()
    {
        //Arrange
        var sourceImagePath = CreateImageFile("existing-source.png", 16, 16);
        var gtsPath = Path.Combine(_workDirectory, "definitions", "sheet.gts");
        var siblingImagePath = Path.ChangeExtension(gtsPath, ".png");

        using var tilesheet = TilesheetFactory.FromImageFile("Existing Source", sourceImagePath);

        //Act
        TilesheetDefinitionSerializer.Save(gtsPath, tilesheet);

        //Assert
        File.Exists(siblingImagePath).Should().BeFalse();
        tilesheet.ImageFilePath.Should().Be(Path.GetFullPath(sourceImagePath));

        var definition = TilesheetDefinitionSerializer.Load(gtsPath);
        definition.Image.FilePath.Should().Be(
            Path.GetRelativePath(Path.GetDirectoryName(gtsPath)!, sourceImagePath).Replace('\\', '/'));
    }

    [Fact]
    public void PersistImageToFile_writes_the_original_source_bitmap_for_a_masked_tilesheet()
    {
        //Arrange
        var imagePath = Path.Combine(_workDirectory, "masked-source.png");
        var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.White);

        using var tilesheet = TilesheetFactory.FromBitmap("Masked", bitmap);
        tilesheet.ApplyMask(SKColors.White, tolerance: 0);
        tilesheet.SkBitmap.GetPixel(0, 0).Alpha.Should().Be(0);

        //Act
        tilesheet.PersistImageToFile(imagePath);

        //Assert - the persisted file carries the unmasked source, so loading the definition
        //re-applies the stored mask exactly once.
        using var persistedBitmap = SKBitmap.Decode(imagePath);
        persistedBitmap.Should().NotBeNull();
        persistedBitmap.GetPixel(0, 0).Alpha.Should().Be(255);
    }
}
