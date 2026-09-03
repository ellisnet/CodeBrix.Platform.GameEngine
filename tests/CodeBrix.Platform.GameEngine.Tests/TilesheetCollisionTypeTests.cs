using System;
using System.Drawing;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the collision-type metadata that regions, frames and tiles gained upstream after the
/// vendored baseline: a region default is inherited by frames that carry no explicit override, an
/// assigned frame value is always an override, a tile seeds its type from its first frame but only
/// follows later frames when asked to, and a .gts definition round-trips both forms.
/// </summary>
public class TilesheetCollisionTypeTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary directory the fixture writes its image files into.</summary>
    public TilesheetCollisionTypeTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_colltype_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
    }

    /// <summary>Clears the global sprite and scene registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();
        Scene.ClearAllScenes();
        TilesheetRegistry.Instance.Clear();

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

    [Fact]
    public void CollisionType_region_default_updates_inherited_frames_and_preserves_overrides()
    {
        //Arrange
        using var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;
        region.CollisionType = TileCollisionType.Blocking;

        var inheritedFrame = tilesheet.GetFrame(0, 0);
        var triggerFrame = tilesheet.GetFrame(1, 0);
        var explicitBlockingFrame = tilesheet.GetFrame(2, 0);

        //Act
        triggerFrame.CollisionType = TileCollisionType.Trigger;
        explicitBlockingFrame.CollisionType = TileCollisionType.Blocking;

        //Assert - an assigned value is an override even when it equals the region default.
        inheritedFrame.HasCollisionTypeOverride.Should().BeFalse();
        triggerFrame.HasCollisionTypeOverride.Should().BeTrue();
        explicitBlockingFrame.HasCollisionTypeOverride.Should().BeTrue();

        //Act
        region.CollisionType = TileCollisionType.None;

        //Assert
        inheritedFrame.CollisionType.Should().Be(TileCollisionType.None);
        triggerFrame.CollisionType.Should().Be(TileCollisionType.Trigger);
        explicitBlockingFrame.CollisionType.Should().Be(TileCollisionType.Blocking);

        //Act
        var cleared = triggerFrame.ClearCollisionTypeOverride();

        //Assert
        cleared.Should().BeTrue();
        triggerFrame.CollisionType.Should().Be(TileCollisionType.None);
    }

    [Fact]
    public void CollisionTypeByFrame_keeps_the_type_static_until_it_is_enabled()
    {
        //Arrange
        using var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;
        region.CollisionType = TileCollisionType.Blocking;
        region.SetFrameCollisionType(1, 0, TileCollisionType.Trigger);
        region.SetFrameCollisionType(2, 0, TileCollisionType.None);

        using var scene = new Scene();
        var layer = scene.AddLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);

        //Act - the first frame seeds the sprite's collision type.
        var sprite = SpriteManager.Instance.CreateSprite(
            layer,
            tilesheet.GetFrame(0, 0),
            collisionProfileName: CollisionProfileNames.Actor);

        //Assert
        var collider = sprite.Collider;
        collider.Should().NotBeNull();
        sprite.CollisionType.Should().Be(TileCollisionType.Blocking);
        sprite.CollisionsEnabled.Should().BeTrue();
        collider!.ResponseType.Should().Be(CollisionResponseType.Solid);
        collider.CollisionGroup.Should().Be(scene.CollisionGroups.Actors);
        layer.ColliderRegistry.DynamicColliders.Should().Contain(collider);

        //Act - a later frame is ignored while the tile does not follow frames.
        sprite.CurrentFrame = tilesheet.GetFrame(1, 0);

        //Assert
        sprite.CollisionType.Should().Be(TileCollisionType.Blocking);

        //Act
        sprite.CollisionTypeByFrame = true;

        //Assert
        sprite.CollisionType.Should().Be(TileCollisionType.Trigger);
        collider.ResponseType.Should().Be(CollisionResponseType.Trigger);

        //Act - a frame whose type is None disables collisions entirely.
        sprite.CurrentFrame = tilesheet.GetFrame(2, 0);

        //Assert
        sprite.CollisionType.Should().Be(TileCollisionType.None);
        sprite.CollisionsEnabled.Should().BeFalse();
        layer.ColliderRegistry.DynamicColliders.Contains(collider).Should().BeFalse();
    }

    [Fact]
    public void CollisionType_round_trips_through_gts_json_for_inherited_and_explicit_frames()
    {
        //Arrange - frame (0,0) inherits, (1,0) overrides to None, (2,0) overrides to the default.
        var imagePath = WriteImageFile("collision-types.png", 48, 16);
        var definition = new TilesheetDefinition
        {
            Name = "CollisionTypes",
            Image = new TilesheetImageDefinition { FilePath = imagePath },
            Regions =
            [
                new TilesheetRegionDefinition
                {
                    Name = TilesheetRegion.DefaultRegionName,
                    Area = new Rectangle(0, 0, 48, 16),
                    TileSize = new Size(16, 16),
                    CollisionType = TileCollisionType.Blocking,
                    Frames =
                    [
                        new TilesheetFrameDefinition { XTile = 0, YTile = 0 },
                        new TilesheetFrameDefinition
                        {
                            XTile = 1,
                            YTile = 0,
                            CollisionType = TileCollisionType.None
                        },
                        new TilesheetFrameDefinition
                        {
                            XTile = 2,
                            YTile = 0,
                            CollisionType = TileCollisionType.Blocking
                        }
                    ]
                }
            ]
        };

        //Act
        var json = TilesheetDefinitionSerializer.ToJson(definition);
        var jsonRoundTrip = TilesheetDefinitionSerializer.FromJson(json);

        //Assert - the enum is written in its string form for interchange with upstream tooling.
        json.Contains("\"Blocking\"").Should().BeTrue();

        var jsonRegion = Assert.Single(jsonRoundTrip.Regions);
        jsonRegion.CollisionType.Should().Be(TileCollisionType.Blocking);
        jsonRegion.Frames[0].CollisionType.Should().BeNull();
        jsonRegion.Frames[1].CollisionType.GetValueOrDefault().Should().Be(TileCollisionType.None);
        jsonRegion.Frames[2].CollisionType.GetValueOrDefault().Should().Be(TileCollisionType.Blocking);

        //Act
        using var tilesheet = TilesheetFactory.FromDefinition(jsonRoundTrip);
        var runtimeRegion = tilesheet.DefaultRegion;

        //Assert - the inherited/explicit distinction survived into the runtime region.
        runtimeRegion.TryGetFrameCollisionTypeOverride(0, 0, out _).Should().BeFalse();
        runtimeRegion.TryGetFrameCollisionTypeOverride(1, 0, out var noneOverride).Should().BeTrue();
        noneOverride.Should().Be(TileCollisionType.None);
        runtimeRegion.TryGetFrameCollisionTypeOverride(2, 0, out var equalOverride).Should().BeTrue();
        equalOverride.Should().Be(TileCollisionType.Blocking);

        //Act
        var serialized = TilesheetDefinitionSerializer.FromTilesheet(tilesheet);

        //Assert
        var serializedRegion = Assert.Single(serialized.Regions);
        serializedRegion.CollisionType.Should().Be(TileCollisionType.Blocking);
        serializedRegion.Frames[0].CollisionType.Should().BeNull();
        serializedRegion.Frames[1].CollisionType.GetValueOrDefault().Should().Be(TileCollisionType.None);
        serializedRegion.Frames[2].CollisionType.GetValueOrDefault().Should().Be(TileCollisionType.Blocking);
    }

    private static Tilesheet CreateRuntimeTilesheet()
    {
        var bitmap = new SKBitmap(48, 16);
        var tilesheet = TilesheetFactory.FromBitmap("CollisionTypes", bitmap);
        tilesheet.DefaultRegion.TileSize = new Size(16, 16);

        return tilesheet;
    }

    private string WriteImageFile(string fileName, int width, int height)
    {
        var path = Path.Combine(_workDirectory, fileName);

        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        File.WriteAllBytes(path, data.ToArray());

        return path;
    }
}
