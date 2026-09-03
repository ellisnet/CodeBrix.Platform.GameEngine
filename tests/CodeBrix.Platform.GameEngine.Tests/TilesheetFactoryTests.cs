using System;
using System.Drawing;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the tilesheet-definition parity fixes made upstream after the vendored baseline: an
/// asset-backed sheet took its runtime name from the asset ENTRY rather than from the definition,
/// and definitions that named two mutually exclusive image sources (or an assets file with no
/// entry name) were accepted by undocumented precedence instead of being rejected.
/// </summary>
public class TilesheetFactoryTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary directory the fixture writes its image and asset files into.</summary>
    public TilesheetFactoryTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_tsfactory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        AssetsFile.ClearAll();
    }

    /// <summary>Clears the global assets registry and removes the temporary directory.</summary>
    public void Dispose()
    {
        AssetsFile.ClearAll();

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

    private static MemoryStream CreateImageStream(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(40, 80, 120, 200));

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public void FromDefinition_uses_the_definition_name_rather_than_the_asset_entry_name()
    {
        //Arrange
        var assetsPath = Path.Combine(_workDirectory, "tiles.gaf");
        using var assetsFile = AssetsFile.LoadOrCreate(assetsPath);
        using var imageStream = CreateImageStream(16, 16);
        assetsFile.Add(AssetTypes.Image, "images/player.png", imageStream);

        var definition = new TilesheetDefinition
        {
            Name = "Player Sprites",
            Image = new TilesheetImageDefinition
            {
                AssetEntryName = "images/player.png"
            },
            Regions =
            [
                new TilesheetRegionDefinition
                {
                    Name = TilesheetRegion.DefaultRegionName,
                    Area = new Rectangle(0, 0, 16, 16),
                    TileSize = new Size(16, 16)
                }
            ]
        };

        //Act
        using var tilesheet = TilesheetFactory.FromDefinition(definition, defaultAssetsFile: assetsFile);

        //Assert - the runtime name used to be "images/player.png".
        tilesheet.Name.Should().Be("Player Sprites");
        tilesheet.AssetIdentifier.Should().NotBeNull();
        tilesheet.AssetIdentifier!.AssetName.Should().Be("images/player.png");
        ReferenceEquals(assetsFile, tilesheet.AssetIdentifier.AssetsFile).Should().BeTrue();
    }

    [Fact]
    public void FromDefinition_rejects_a_definition_with_both_file_and_asset_image_sources()
    {
        //Arrange
        var definition = new TilesheetDefinition
        {
            Name = "Ambiguous",
            Image = new TilesheetImageDefinition
            {
                FilePath = "sheet.png",
                AssetsFilePath = "tiles.gaf",
                AssetEntryName = "sheet.png"
            }
        };

        //Act
        var act = () => TilesheetFactory.FromDefinition(definition, _workDirectory);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ambiguous*");
    }

    [Fact]
    public void FromDefinition_rejects_an_assets_file_without_an_entry_name()
    {
        //Arrange
        var definition = new TilesheetDefinition
        {
            Name = "Incomplete",
            Image = new TilesheetImageDefinition
            {
                AssetsFilePath = "tiles.gaf"
            }
        };

        //Act
        var act = () => TilesheetFactory.FromDefinition(definition, _workDirectory);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(TilesheetImageDefinition.AssetEntryName)}*");
    }
}
