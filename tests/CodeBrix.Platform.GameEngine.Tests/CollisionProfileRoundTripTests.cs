using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Save-graph guards for the collision profile system: the scene's profile registry, the layer's
/// default tile profile and the per-tile collision type survive a save/load cycle, a save written
/// before collision types existed still loads its enabled tiles as
/// <see cref="TileCollisionType.Blocking"/>, and a saved <see cref="TileCollisionType.Trigger"/>
/// is not downgraded by the order the members happen to appear in the file.
/// </summary>
public class CollisionProfileRoundTripTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Creates the temporary working directory and clears the engine registries.</summary>
    public CollisionProfileRoundTripTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_collprofile_{Guid.NewGuid():N}");
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
    public void Collision_profiles_and_types_survive_a_save_and_load_cycle()
    {
        //Arrange
        var sheet = LoadSheet("profile_sheet");
        var savePath = Path.Combine(_workDirectory, "profiles.json");

        var scene = new Scene { ID = "scene-profiles" };
        scene.CollisionGroups.Define("Enemies");
        scene.CollisionProfiles.Define("Enemy", "Enemies", new[] { "Actors", "Projectiles" });

        var layer = scene.AddLayer(columnCount: 2, rowCount: 2, width: 16, height: 16);
        layer.DefaultTileCollisionProfile = CollisionProfileNames.Sensor;

        var tile = layer[1, 1]!;
        tile.CurrentFrame = new Frame(sheet, 1, 0);
        tile.CollisionType = TileCollisionType.Trigger;

        var shot = SpriteManager.Instance.CreateSprite(
            layer,
            new Frame(sheet, 0, 0),
            "shot",
            CollisionProfileNames.Projectile);

        shot.SetPosition(new Vector2(1f, 0f));

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        EngineState.LoadFromFile(savePath);

        //Assert - the scene's own registry came back, standard profiles included.
        var loadedScene = Scene.GetSceneByID("scene-profiles");
        loadedScene.Should().NotBeNull();
        loadedScene!.CollisionProfiles.Should().NotBeNull();

        var loadedEnemy = loadedScene.CollisionProfiles.Get("Enemy");
        loadedEnemy.CollisionGroup.Should().Be("Enemies");
        loadedEnemy.ResolveCollidesWith(loadedScene.CollisionGroups)
            .Should().Be(loadedScene.CollisionGroups.Actors | loadedScene.CollisionGroups.Projectiles);
        loadedScene.CollisionProfiles.Get(CollisionProfileNames.World).CollisionGroup
            .Should().Be("WorldStatic");

        //Assert - the layer's default profile came back and is applied to its tiles.
        var loadedLayer = loadedScene.First();
        loadedLayer.DefaultTileCollisionProfile.Should().Be(CollisionProfileNames.Sensor);

        var loadedTile = loadedLayer[1, 1]!;
        loadedTile.CollisionProfileName.Should().Be(CollisionProfileNames.Sensor);
        loadedTile.CollisionType.Should().Be(TileCollisionType.Trigger);
        loadedTile.CollisionsEnabled.Should().BeTrue();
        loadedTile.Collider!.ResponseType.Should().Be(CollisionResponseType.Trigger);
        loadedTile.Collider.CollisionGroup.Should().Be(loadedScene.CollisionGroups.Triggers);
        loadedLayer.ColliderRegistry.StaticColliders.Should().Contain(loadedTile.Collider);

        //Assert - the sprite kept the profile it was created with rather than the default.
        var loadedShot = SpriteManager.Instance.AllSprites.FirstOrDefault(s => s.Nickname == "shot");
        loadedShot.Should().NotBeNull();
        loadedShot!.CollisionProfileName.Should().Be(CollisionProfileNames.Projectile);
        loadedShot.Collider!.CollisionGroup.Should().Be(loadedScene.CollisionGroups.Projectiles);
        loadedShot.Collider.CollidesWith
            .Should().Be(loadedScene.CollisionGroups.WorldStatic | loadedScene.CollisionGroups.Actors);
    }

    [Fact]
    public void CollisionType_is_written_to_the_save_file_in_its_string_form()
    {
        //Arrange
        var sheet = LoadSheet("string_form_sheet");
        var savePath = Path.Combine(_workDirectory, "string-form.json");

        var scene = new Scene { ID = "scene-string-form" };
        var layer = scene.AddLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);
        layer[0, 0]!.CurrentFrame = new Frame(sheet, 0, 0);
        layer[0, 0]!.CollisionType = TileCollisionType.Trigger;

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        var json = File.ReadAllText(savePath);

        //Assert
        json.Contains("\"CollisionType\": \"Trigger\"").Should().BeTrue();
        json.Contains("\"CollisionProfileName\"").Should().BeTrue();
    }

    [Fact]
    public void A_save_without_a_collision_type_loads_an_enabled_tile_as_blocking()
    {
        //Arrange - save a normal enabled tile, then strip the members an older save could not
        //have carried.
        var sheet = LoadSheet("legacy_sheet");
        var savePath = Path.Combine(_workDirectory, "legacy.json");

        var scene = new Scene { ID = "scene-legacy" };
        var layer = scene.AddLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);
        layer[0, 0]!.CurrentFrame = new Frame(sheet, 0, 0);
        layer[0, 0]!.CollisionsEnabled = true;

        Engine.Instance.State.SaveToFile(savePath);

        var root = JsonNode.Parse(File.ReadAllText(savePath))!;
        RemoveProperties(root, "CollisionType", "CollisionTypeByFrame", "CollisionProfileName", "DefaultTileCollisionProfile", "CollisionProfiles");
        File.WriteAllText(savePath, root.ToJsonString());

        //Act
        EngineState.LoadFromFile(savePath);

        //Assert - the enabled flag alone still produces a registered, blocking tile.
        var loadedScene = Scene.GetSceneByID("scene-legacy");
        loadedScene.Should().NotBeNull();
        loadedScene!.CollisionProfiles.Should().NotBeNull();

        var loadedLayer = loadedScene.First();
        loadedLayer.DefaultTileCollisionProfile.Should().Be(CollisionProfileNames.World);

        var loadedTile = loadedLayer[0, 0]!;
        loadedTile.CollisionsEnabled.Should().BeTrue();
        loadedTile.CollisionType.Should().Be(TileCollisionType.Blocking);
        loadedTile.Collider!.ResponseType.Should().Be(CollisionResponseType.Solid);
        loadedTile.Collider.CollisionGroup.Should().Be(loadedScene.CollisionGroups.WorldStatic);
        loadedLayer.ColliderRegistry.StaticColliders.Should().Contain(loadedTile.Collider);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_saved_Trigger_is_not_reset_to_blocking_by_member_order(bool enabledFlagFirst)
    {
        //Arrange - a trigger tile saved, then re-written with CollisionsEnabled deliberately
        //placed before or after CollisionType.
        var sheet = LoadSheet("order_sheet");
        var savePath = Path.Combine(_workDirectory, $"order-{enabledFlagFirst}.json");

        var scene = new Scene { ID = "scene-order" };
        var layer = scene.AddLayer(columnCount: 1, rowCount: 1, width: 16, height: 16);
        layer[0, 0]!.CurrentFrame = new Frame(sheet, 0, 0);
        layer[0, 0]!.CollisionType = TileCollisionType.Trigger;

        Engine.Instance.State.SaveToFile(savePath);

        var root = JsonNode.Parse(File.ReadAllText(savePath))!;
        MoveProperty(root, "CollisionsEnabled", toFront: enabledFlagFirst);
        File.WriteAllText(savePath, root.ToJsonString());

        //Act
        EngineState.LoadFromFile(savePath);

        //Assert
        var loadedTile = Scene.GetSceneByID("scene-order")!.First()[0, 0]!;
        loadedTile.CollisionType.Should().Be(TileCollisionType.Trigger);
        loadedTile.CollisionsEnabled.Should().BeTrue();
        loadedTile.Collider!.ResponseType.Should().Be(CollisionResponseType.Trigger);
    }

    /// <summary>
    /// Removes the named members from every object in the document, so a current save file reads
    /// like one written before those members existed.
    /// </summary>
    private static void RemoveProperties(JsonNode node, params string[] names)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in names)
                    obj.Remove(name);

                foreach (var child in obj.ToList())
                {
                    if (child.Value is not null)
                        RemoveProperties(child.Value, names);
                }

                break;

            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    if (item is not null)
                        RemoveProperties(item, names);
                }

                break;
        }
    }

    /// <summary>
    /// Rewrites every object that carries the named member so that the member comes first or last,
    /// which is the order the deserializer assigns properties in.
    /// </summary>
    private static void MoveProperty(JsonNode node, string name, bool toFront)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var child in obj.ToList())
                {
                    if (child.Value is not null)
                        MoveProperty(child.Value, name, toFront);
                }

                if (obj.ContainsKey(name))
                {
                    var pairs = obj.ToList();
                    var moved = pairs.Single(pair => pair.Key == name);
                    var rest = pairs.Where(pair => pair.Key != name).ToList();
                    var ordered = new List<KeyValuePair<string, JsonNode?>>();

                    if (toFront)
                    {
                        ordered.Add(moved);
                        ordered.AddRange(rest);
                    }
                    else
                    {
                        ordered.AddRange(rest);
                        ordered.Add(moved);
                    }

                    obj.Clear();

                    foreach (var pair in ordered)
                        obj.Add(pair.Key, pair.Value);
                }

                break;

            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    if (item is not null)
                        MoveProperty(item, name, toFront);
                }

                break;
        }
    }

    private Tilesheet LoadSheet(string name)
    {
        var imagePath = WriteTilesheetPng($"{name}.png", tileSize: 16, columns: 2, rows: 2);
        var sheet = TilesheetRegistry.Instance.LoadFromImageFile(name, imagePath);
        sheet.DefaultRegion.TileSize = new Size(16, 16);

        return sheet;
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
