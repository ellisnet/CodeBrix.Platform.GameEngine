using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json.Nodes;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
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
/// Covers the collision-adjustment metadata that regions, frames and tiles gained upstream after
/// the vendored baseline: a positive value on ANY edge insets that edge, a region default is
/// inherited by frames that carry no explicit override, an assigned frame value is always an
/// override (even when it equals the current region default), and a tile seeds its adjustment from
/// its first frame but only follows later frames when asked to.
/// </summary>
public class TilesheetCollisionAdjustTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly List<Scene> _scenes = new();
    private readonly List<Tilesheet> _tilesheets = new();

    /// <summary>Creates the temporary directory the fixture writes its image files into.</summary>
    public TilesheetCollisionAdjustTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_colladjust_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
    }

    /// <summary>Clears the global sprite and scene registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();

        foreach (var tilesheet in _tilesheets)
            tilesheet.Dispose();

        _tilesheets.Clear();

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

    private Tilesheet CreateRuntimeTilesheet()
    {
        var bitmap = new SKBitmap(32, 16);
        var tilesheet = TilesheetFactory.FromBitmap("CollisionSheet", bitmap);
        tilesheet.DefaultRegion.TileSize = new Size(16, 16);
        _tilesheets.Add(tilesheet);

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

    private SceneLayer CreateLayer(int columns = 1, int rows = 1, int tileWidth = 16, int tileHeight = 16)
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene.AddLayer(
            columnCount: columns,
            rowCount: rows,
            width: tileWidth,
            height: tileHeight,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);
    }

    [Fact]
    public void ApplyTo_positive_values_inset_all_four_edges()
    {
        //Arrange
        var adjust = new CollisionAdjust(top: 3, bottom: 2, left: 4, right: 1);

        //Act
        var result = adjust.ApplyTo(new Rectangle(10, 20, 30, 40));

        //Assert - left/top move in, right/bottom move in too: 30-4-1 wide, 40-3-2 high.
        result.Should().Be(new Rectangle(14, 23, 25, 35));
    }

    [Fact]
    public void ApplyTo_negative_values_expand_all_four_edges()
    {
        //Arrange
        var adjust = new CollisionAdjust(top: -3, bottom: -2, left: -4, right: -1);

        //Act
        var result = adjust.ApplyTo(new Rectangle(10, 20, 30, 40));

        //Assert
        result.Should().Be(new Rectangle(6, 17, 35, 45));
    }

    [Fact]
    public void ApplyTo_leaves_the_rectangle_untouched_for_None()
    {
        //Arrange
        var rectangle = new Rectangle(5, 6, 7, 8);

        //Act
        var result = CollisionAdjust.None.ApplyTo(rectangle);

        //Assert
        result.Should().Be(rectangle);
    }

    [Fact]
    public void CollisionAdjust_equality_compares_all_four_edges()
    {
        //Arrange
        var first = new CollisionAdjust(1, 2, 3, 4);
        var second = new CollisionAdjust(1, 2, 3, 4);
        var third = new CollisionAdjust(1, 2, 3, 5);

        //Act / Assert
        (first == second).Should().BeTrue();
        (first != third).Should().BeTrue();
        first.Equals((object)second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void CollisionAdjust_region_default_updates_inherited_frames_and_preserves_overrides()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;
        var initial = new CollisionAdjust(1, 1, 2, 2);
        region.CollisionAdjust = initial;

        tilesheet.GetFrame(0, 0).CollisionAdjust.Should().Be(initial);
        tilesheet.GetFrame(1, 0).CollisionAdjust.Should().Be(initial);

        //Act - the second frame takes an explicit override, then the region default changes.
        var frameOverride = new CollisionAdjust(3, 3, 4, 4);
        var secondFrame = tilesheet.GetFrame(1, 0);
        secondFrame.CollisionAdjust = frameOverride;

        var replacementDefault = new CollisionAdjust(5, 5, 6, 6);
        region.CollisionAdjust = replacementDefault;

        //Assert - the inheriting frame follows the new default; the overridden frame does not.
        tilesheet.GetFrame(0, 0).CollisionAdjust.Should().Be(replacementDefault);
        tilesheet.GetFrame(1, 0).CollisionAdjust.Should().Be(frameOverride);
        tilesheet.GetFrame(1, 0).CollisionArea
            .Should().Be(frameOverride.ApplyTo(new Rectangle(0, 0, 16, 16)));
    }

    [Fact]
    public void SetFrameCollisionAdjust_records_an_override_even_when_it_equals_the_region_default()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;
        var initialDefault = new CollisionAdjust(1, 1, 2, 2);
        region.CollisionAdjust = initialDefault;

        //Act - assign the frame the value it already inherits.
        var frame = tilesheet.GetFrame(1, 0);
        frame.CollisionAdjust = initialDefault;

        //Assert - it is now an independent override that survives a default change.
        frame.HasCollisionAdjustOverride.Should().BeTrue();

        var replacementDefault = new CollisionAdjust(3, 3, 4, 4);
        region.CollisionAdjust = replacementDefault;

        frame.CollisionAdjust.Should().Be(initialDefault);
        frame.ClearCollisionAdjustOverride().Should().BeTrue();
        frame.HasCollisionAdjustOverride.Should().BeFalse();
        frame.CollisionAdjust.Should().Be(replacementDefault);
    }

    [Fact]
    public void ClearFrameCollisionAdjustOverride_returns_false_when_the_frame_inherits()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;

        //Act
        var cleared = region.ClearFrameCollisionAdjustOverride(0, 0);

        //Assert
        cleared.Should().BeFalse();
        region.TryGetFrameCollisionAdjustOverride(0, 0, out _).Should().BeFalse();
    }

    [Fact]
    public void SetFrameCollisionAdjust_throws_for_coordinates_outside_the_region()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;

        //Act
        var act = () => region.SetFrameCollisionAdjust(9, 9, new CollisionAdjust(1, 1, 1, 1));

        //Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void AdjustCollisionAreaByFrame_keeps_the_area_static_until_it_is_enabled()
    {
        //Arrange - two frames carrying different adjustments.
        var tilesheet = CreateRuntimeTilesheet();
        var region = tilesheet.DefaultRegion;
        var firstAdjust = new CollisionAdjust(1, 1, 2, 2);
        var secondAdjust = new CollisionAdjust(3, 3, 4, 4);
        region.SetFrameCollisionAdjust(0, 0, firstAdjust);
        region.SetFrameCollisionAdjust(1, 0, secondAdjust);

        var layer = CreateLayer();
        var sprite = SpriteManager.Instance.CreateSprite(layer, tilesheet.GetFrame(0, 0));

        //Assert - the first frame seeded the tile, and by-frame mode is off by default.
        sprite.AdjustCollisionArea.Should().Be(firstAdjust);
        sprite.AdjustCollisionAreaByFrame.Should().BeFalse();

        //Act - a frame change without by-frame mode leaves the adjustment alone.
        sprite.CurrentFrame = tilesheet.GetFrame(1, 0);
        sprite.AdjustCollisionArea.Should().Be(firstAdjust);

        //Act - enabling by-frame mode adopts the current frame, and follows later frames.
        sprite.AdjustCollisionAreaByFrame = true;
        sprite.AdjustCollisionArea.Should().Be(secondAdjust);

        sprite.CurrentFrame = tilesheet.GetFrame(0, 0);

        //Assert
        sprite.AdjustCollisionArea.Should().Be(firstAdjust);
    }

    [Fact]
    public void AdjustCollisionArea_set_explicitly_is_not_overwritten_by_the_first_frame()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        tilesheet.DefaultRegion.SetFrameCollisionAdjust(0, 0, new CollisionAdjust(1, 1, 2, 2));

        var layer = CreateLayer();
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        var explicitAdjust = new CollisionAdjust(7, 7, 7, 7);

        //Act
        sprite.AdjustCollisionArea = explicitAdjust;
        sprite.CurrentFrame = tilesheet.GetFrame(0, 0);

        //Assert
        sprite.AdjustCollisionArea.Should().Be(explicitAdjust);
    }

    [Fact]
    public void CollisionArea_insets_the_draw_location_on_every_edge()
    {
        //Arrange
        var layer = CreateLayer(columns: 4, rows: 4);
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.RenderSize = new Size(layer.TileWidth, layer.TileHeight);
        var drawLocation = sprite.DrawLocationWorld;

        //Act
        sprite.AdjustCollisionArea = new CollisionAdjust(top: 2, bottom: 3, left: 4, right: 5);

        //Assert
        sprite.CollisionArea.Should().Be(
            Rectangle.FromLTRB(
                drawLocation.Left + 4,
                drawLocation.Top + 2,
                drawLocation.Right - 5,
                drawLocation.Bottom - 3));
    }

    [Fact]
    public void CopyCollisionSettingsFrom_carries_the_adjustment_onto_a_clone()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var layer = CreateLayer(columns: 4, rows: 4);
        var source = SpriteManager.Instance.CreateSprite(layer, tilesheet.GetFrame(0, 0));
        source.AdjustCollisionArea = new CollisionAdjust(1, 2, 3, 4);
        source.AdjustCollisionAreaByFrame = false;

        //Act
        var clone = SpriteManager.Instance.CloneSprite(source);

        //Assert
        clone.AdjustCollisionArea.Should().Be(new CollisionAdjust(1, 2, 3, 4));
        clone.AdjustCollisionAreaByFrame.Should().BeFalse();
    }

    [Fact]
    public void CollisionAdjust_round_trips_through_gts_json_for_the_region_and_its_frames()
    {
        //Arrange
        var regionAdjust = new CollisionAdjust(1, 1, 2, 2);
        var frameAdjust = new CollisionAdjust(3, 3, 4, 4);
        var definition = new TilesheetDefinition
        {
            Name = "Sheet",
            Image = new TilesheetImageDefinition { FilePath = "sheet.png" },
            Regions =
            [
                new TilesheetRegionDefinition
                {
                    Name = TilesheetRegion.DefaultRegionName,
                    Area = new Rectangle(0, 0, 32, 16),
                    TileSize = new Size(16, 16),
                    CollisionAdjust = regionAdjust,
                    Frames =
                    [
                        new TilesheetFrameDefinition { XTile = 0, YTile = 0, CollisionAdjust = regionAdjust },
                        new TilesheetFrameDefinition { XTile = 1, YTile = 0, CollisionAdjust = frameAdjust }
                    ]
                }
            ]
        };

        //Act
        var json = TilesheetDefinitionSerializer.ToJson(definition);
        var loaded = TilesheetDefinitionSerializer.FromJson(json);

        //Assert
        var loadedRegion = Assert.Single(loaded.Regions);
        loadedRegion.CollisionAdjust.Should().Be(regionAdjust);
        loadedRegion.Frames.Count.Should().Be(2);
        loadedRegion.Frames[1].CollisionAdjust.GetValueOrDefault().Should().Be(frameAdjust);
    }

    [Fact]
    public void CollisionAdjust_defaults_to_None_for_gts_json_written_without_collision_metadata()
    {
        //Arrange - build valid GTS JSON with the real serializer, then strip the members an
        //older definition file would not have carried.
        var definition = new TilesheetDefinition
        {
            Name = "Legacy",
            Image = new TilesheetImageDefinition { FilePath = "legacy.png" },
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

        var jsonObject = JsonNode.Parse(TilesheetDefinitionSerializer.ToJson(definition))!.AsObject();
        var regionObject = jsonObject[nameof(TilesheetDefinition.Regions)]![0]!.AsObject();
        regionObject.Remove(nameof(TilesheetRegionDefinition.CollisionAdjust));
        regionObject.Remove(nameof(TilesheetRegionDefinition.Frames));

        //Act
        var loaded = TilesheetDefinitionSerializer.FromJson(jsonObject.ToJsonString());

        //Assert
        var region = Assert.Single(loaded.Regions);
        region.CollisionAdjust.Should().Be(CollisionAdjust.None);
        region.Frames.Should().BeEmpty();
    }

    [Fact]
    public void FromDefinition_preserves_an_explicit_override_equal_to_the_region_default()
    {
        //Arrange - frame (0,0) has no value (inherits), frame (1,0) records the same value.
        var imagePath = WriteImageFile("equal-override.png", 32, 16);
        var regionAdjust = new CollisionAdjust(1, 1, 2, 2);
        var definition = new TilesheetDefinition
        {
            Name = "Sheet",
            Image = new TilesheetImageDefinition { FilePath = imagePath },
            Regions =
            [
                new TilesheetRegionDefinition
                {
                    Name = TilesheetRegion.DefaultRegionName,
                    Area = new Rectangle(0, 0, 32, 16),
                    TileSize = new Size(16, 16),
                    CollisionAdjust = regionAdjust,
                    Frames =
                    [
                        new TilesheetFrameDefinition { XTile = 0, YTile = 0 },
                        new TilesheetFrameDefinition { XTile = 1, YTile = 0, CollisionAdjust = regionAdjust }
                    ]
                }
            ]
        };

        //Act
        var tilesheet = TilesheetFactory.FromDefinition(definition);
        _tilesheets.Add(tilesheet);
        var region = tilesheet.DefaultRegion;

        //Assert
        region.TryGetFrameCollisionAdjustOverride(0, 0, out _).Should().BeFalse();
        region.TryGetFrameCollisionAdjustOverride(1, 0, out _).Should().BeTrue();

        var replacementDefault = new CollisionAdjust(3, 3, 4, 4);
        region.CollisionAdjust = replacementDefault;

        tilesheet.GetFrame(0, 0).CollisionAdjust.Should().Be(replacementDefault);
        tilesheet.GetFrame(1, 0).CollisionAdjust.Should().Be(regionAdjust);
    }

    [Fact]
    public void FromTilesheet_writes_null_for_inherited_frames_and_a_value_for_overrides()
    {
        //Arrange
        var imagePath = WriteImageFile("sheet.png", 32, 16);
        var tilesheet = TilesheetFactory.FromImageFile("Sheet", imagePath);
        _tilesheets.Add(tilesheet);

        var region = tilesheet.DefaultRegion;
        region.TileSize = new Size(16, 16);
        region.CollisionAdjust = new CollisionAdjust(1, 1, 2, 2);
        region.SetFrameCollisionAdjust(1, 0, new CollisionAdjust(3, 3, 4, 4));

        //Act
        var definition = TilesheetDefinitionSerializer.FromTilesheet(tilesheet);

        //Assert
        var serializedRegion = Assert.Single(definition.Regions);
        serializedRegion.Frames.Count.Should().Be(2);
        serializedRegion.Frames[0].CollisionAdjust.Should().BeNull();
        serializedRegion.Frames[1].CollisionAdjust.GetValueOrDefault()
            .Should().Be(region.GetFrameCollisionAdjust(1, 0));
    }

    [Fact]
    public void CollisionType_defaults_to_None_for_gts_json_written_without_collision_metadata()
    {
        //Arrange - build valid GTS JSON with the real serializer, then strip the members an
        //older definition file would not have carried.
        var definition = new TilesheetDefinition
        {
            Name = "LegacyType",
            Image = new TilesheetImageDefinition { FilePath = "legacy-type.png" },
            Regions =
            [
                new TilesheetRegionDefinition
                {
                    Name = TilesheetRegion.DefaultRegionName,
                    Area = new Rectangle(0, 0, 16, 16),
                    TileSize = new Size(16, 16),
                    CollisionType = TileCollisionType.Blocking
                }
            ]
        };

        var jsonObject = JsonNode.Parse(TilesheetDefinitionSerializer.ToJson(definition))!.AsObject();
        var regionObject = jsonObject[nameof(TilesheetDefinition.Regions)]![0]!.AsObject();
        regionObject.Remove(nameof(TilesheetRegionDefinition.CollisionAdjust));
        regionObject.Remove(nameof(TilesheetRegionDefinition.CollisionType));
        regionObject.Remove(nameof(TilesheetRegionDefinition.Frames));

        //Act
        var loaded = TilesheetDefinitionSerializer.FromJson(jsonObject.ToJsonString());

        //Assert
        var region = Assert.Single(loaded.Regions);
        region.CollisionAdjust.Should().Be(CollisionAdjust.None);
        region.CollisionType.Should().Be(TileCollisionType.None);
        region.Frames.Should().BeEmpty();
    }

    [Fact]
    public void CopyCollisionSettingsFrom_carries_the_collision_type_onto_a_clone()
    {
        //Arrange
        var tilesheet = CreateRuntimeTilesheet();
        var layer = CreateLayer(columns: 4, rows: 4);
        var source = SpriteManager.Instance.CreateSprite(layer, tilesheet.GetFrame(0, 0));
        source.CollisionType = TileCollisionType.Trigger;

        //Act
        var clone = SpriteManager.Instance.CloneSprite(source);

        //Assert - the clone collides exactly like its source, on its own collider.
        clone.CollisionType.Should().Be(TileCollisionType.Trigger);
        clone.CollisionsEnabled.Should().BeTrue();
        clone.CollisionProfileName.Should().Be(source.CollisionProfileName);
        clone.Collider!.ResponseType.Should().Be(CollisionResponseType.Trigger);
        clone.Collider.CollisionGroup.Should().Be(layer.Scene.CollisionGroups.Actors);
        layer.ColliderRegistry.DynamicColliders.Should().Contain(clone.Collider);
    }
}
