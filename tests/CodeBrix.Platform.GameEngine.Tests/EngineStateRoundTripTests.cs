using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Xunit;

// ReSharper disable RedundantSuppressNullableWarningExpression
// ReSharper disable UnusedVariable

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Populated-graph save/load round-trips through the System.Text.Json +
/// CodeBrix.Json.Extensions pipeline: scenes/layers/tile grids, frames resolved via the
/// tilesheet registry, sprites with shared layer references, cycles, and audio specs.
/// </summary>
public class EngineStateRoundTripTests : IDisposable
{
    private readonly string _workDirectory;

    public EngineStateRoundTripTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), $"ge_roundtrip_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDirectory);
        ClearAllEngineState();
    }

    public void Dispose()
    {
        ClearAllEngineState();

        // Loading a resource makes the process-wide shared output adopt a sample rate; left alone
        // it outlives these tests and fails later ones whose source has a different one.
        AudioSystem.Shutdown();

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            /* best effort */
        }
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
    public void Populated_graph_roundtrips_scenes_layers_tiles_sprites_cycles_and_audio()
    {
        //Arrange - a small but representative world.
        var imagePath = WriteTilesheetPng("sheet.png", tileSize: 16, columns: 4, rows: 4);
        var wavPath = WriteWav("blip.wav");
        var savePath = Path.Combine(_workDirectory, "save1.json");

        var sheet = TilesheetRegistry.Instance.LoadFromImageFile("world_sheet", imagePath);
        sheet.DefaultRegion.TileSize = new Size(16, 16);

        var scene = new Scene { ID = "scene-main" };
        var ground = scene.AddLayer(columnCount: 4, rowCount: 3, width: 16, height: 16, zOrder: 0, parallax: 1f);
        var sky = scene.AddLayer(columnCount: 2, rowCount: 2, width: 32, height: 32, zOrder: -1, parallax: 0.5f);

        ground.WrapHorizontally = true;
        ground.OriginPx = new Point(8, 4);
        ground[1, 2]!.CurrentFrame = new Frame(sheet, 2, 3);
        ground[0, 0]!.CurrentFrame = new Frame(sheet, 1, 1);
        ground[0, 0]!.Visible = false;
        ground[3, 1]!.CurrentFrame = new Frame(sheet, 0, 2);
        ground[3, 1]!.CollisionsEnabled = true;
        sheet.DefaultRegion.SetFrameCollisionAdjust(0, 2, new CollisionAdjust(1, 2, 3, 4));
        ground[3, 1]!.AdjustCollisionAreaByFrame = true;
        ground[1, 2]!.AdjustCollisionArea = new CollisionAdjust(5, 6, 7, 8);

        var sequence = new FrameSequence(new Frame(sheet, 0, 0)) { SequenceCycleType = CycleType.PingPong };
        sequence.AddFrame(sheet, 1, 0);
        sequence.AddFrame(sheet, 2, 0);
        _ = new Cycle(sequence, 0.25, "walk_cycle"); // self-registers

        var hero = SpriteManager.Instance.CreateSprite(ground, new Frame(sheet, 3, 3), "hero");
        hero.SetPosition(new Vector2(2f, 1f));
        hero.Visible = true;
        hero.AdjustCollisionArea = new CollisionAdjust(9, 10, 11, 12);

        var blip = AudioResourceManager.Instance.LoadFromFile("blip", wavPath, volume: 0.75f, pan: -0.5f);
        blip.IsLooping = true;
        blip.PlaybackSpeed = 1.5f;

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        EngineState.LoadFromFile(savePath);

        //Assert - scene + layers
        var loadedScene = Scene.GetSceneByID("scene-main");
        loadedScene.Should().NotBeNull();
        loadedScene!.Count().Should().Be(2);

        var loadedGround = loadedScene!.First(layer => layer.ZOrder == 0);
        var loadedSky = loadedScene!.First(layer => layer.ZOrder == -1);
        loadedGround.WrapHorizontally.Should().BeTrue();
        loadedGround.OriginPx.Should().Be(new Point(8, 4));
        loadedGround.TileWidth.Should().Be(16);
        (Math.Abs(loadedSky.Parallax - 0.5f) < 0.0001f).Should().BeTrue();
        loadedSky.TileWidth.Should().Be(32);
        ReferenceEquals(loadedGround.Scene, loadedScene).Should().BeTrue();

        //Assert - tile state survived (frames by tilesheet cell, flags, colliders rebuilt)
        var loadedTile = loadedGround[1, 2]!;
        loadedTile.CurrentFrame.Tilesheet!.Name.Should().Be("world_sheet");
        loadedTile.CurrentFrame.XTile.Should().Be(2);
        loadedTile.CurrentFrame.YTile.Should().Be(3);
        loadedGround[0, 0]!.Visible.Should().BeFalse();
        loadedGround[3, 1]!.CollisionsEnabled.Should().BeTrue();
        loadedGround[3, 1]!.Collider.Should().NotBeNull();

        //Assert - collision adjustment: the by-frame tile re-derives from its frame, the static
        //tile keeps the value it was saved with.
        loadedGround[3, 1]!.AdjustCollisionAreaByFrame.Should().BeTrue();
        loadedGround[3, 1]!.AdjustCollisionArea.Should().Be(new CollisionAdjust(1, 2, 3, 4));
        loadedGround[1, 2]!.AdjustCollisionAreaByFrame.Should().BeFalse();
        loadedGround[1, 2]!.AdjustCollisionArea.Should().Be(new CollisionAdjust(5, 6, 7, 8));
        ReferenceEquals(loadedGround[0, 0]!.SceneLayer, loadedGround).Should().BeTrue();

        //Assert - sprite with shared layer reference and live wiring
        var loadedHero = SpriteManager.Instance.AllSprites.FirstOrDefault(s => s.Nickname == "hero");
        loadedHero.Should().NotBeNull();
        ReferenceEquals(loadedHero!.SceneLayer, loadedGround).Should().BeTrue();
        loadedHero.SceneLayerCoordinates.X.Should().Be(2f);
        loadedHero.SceneLayerCoordinates.Y.Should().Be(1f);
        loadedHero.CurrentFrame.XTile.Should().Be(3);
        loadedHero.AdjustCollisionArea.Should().Be(new CollisionAdjust(9, 10, 11, 12));
        loadedHero.TileAnimator.Should().NotBeNull();
        loadedHero.Movement.Should().NotBeNull();

        //Assert - cycle registry
        var loadedCycle = Cycle.GetAnimationCycle("walk_cycle");
        loadedCycle.Should().NotBeNull();
        loadedCycle.Sequence.FrameCount.Should().Be(3);
        loadedCycle.Sequence.SequenceCycleType.Should().Be(CycleType.PingPong);
        (Math.Abs(loadedCycle.ThrottleTime - 0.25) < 0.0001).Should().BeTrue();
        ReferenceEquals(loadedCycle.NextCycle, loadedCycle).Should().BeTrue();

        //Assert - audio spec rehydrated into the manager from its loose file
        AudioResourceManager.Instance.TryGet("blip", out var loadedBlip).Should().BeTrue();
        (Math.Abs(loadedBlip!.Volume - 0.75f) < 0.0001f).Should().BeTrue();
        (Math.Abs(loadedBlip.Pan - (-0.5f)) < 0.0001f).Should().BeTrue();
        loadedBlip.PlaybackSpeed.Should().Be(1.5f);
        loadedBlip.IsLooping.Should().BeTrue();
    }

    [Fact]
    public void Wrapped_layer_flags_and_tiles_survive_the_roundtrip_and_stay_operational()
    {
        //Arrange - a layer that repeats on both axes, with tile state that must not be replaced.
        var savePath = Path.Combine(_workDirectory, "save_wrap.json");

        var scene = new Scene { ID = "scene-wrap" };
        var layer = scene.AddLayer(columnCount: 4, rowCount: 4, width: 16, height: 16, zOrder: 0, parallax: 1f);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        layer.OriginPx = new Point(7, 9);
        layer[2, 3]!.Visible = false;
        layer[3, 1]!.CollisionsEnabled = true;

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        EngineState.LoadFromFile(savePath);

        //Assert - both flags round-trip and the restored layer wraps on both axes.
        var loadedLayer = Scene.GetSceneByID("scene-wrap")!.First();
        loadedLayer.WrapHorizontally.Should().BeTrue();
        loadedLayer.WrapVertically.Should().BeTrue();
        loadedLayer.OriginPx.Should().Be(new Point(7, 9));
        loadedLayer.WrapGrid(new PointF(-1, -1)).Should().Be(new PointF(3, 3));
        loadedLayer.ResolveWrappedTile(-1, -1).Should().BeSameAs(loadedLayer[3, 3]);

        //Assert - loaded tiles keep their own state and back-reference instead of being replaced.
        loadedLayer[2, 3]!.Visible.Should().BeFalse();
        ReferenceEquals(loadedLayer[2, 3]!.SceneLayer, loadedLayer).Should().BeTrue();

        //Assert - the rebuilt collider registry knows its layer, so periodic queries still work.
        var loadedTile = loadedLayer[3, 1]!;
        loadedTile.Collider.Should().NotBeNull();
        loadedTile.Collider!.CollisionGroup = 1;
        loadedTile.Collider.CollidesWith = 1;

        var bounds = loadedTile.Collider.BoundsWorldPx;
        var query = new Aabb(bounds.MinX + 64, bounds.MinY + 64, bounds.MinX + 66, bounds.MinY + 66);
        var instances = new List<ColliderInstance>();
        loadedLayer.ColliderRegistry.QueryInstances(query, 1, 1, instances);

        instances.Any(instance => ReferenceEquals(instance.Collider, loadedTile.Collider)).Should().BeTrue();
    }

    [Fact]
    public void Asset_pack_audio_roundtrips_with_saved_settings()
    {
        //Arrange - audio delivered via an AssetsFile pack (no loose source file for the spec).
        var wavPath = WriteWav("pack_blip.wav");
        var packPath = Path.Combine(_workDirectory, "assets.pack");
        var savePath = Path.Combine(_workDirectory, "save_pack.json");

        var pack = Assets.AssetsFile.LoadOrCreate(packPath);
        pack.Add(Assets.AssetTypes.Audio, wavPath);
        pack.Save();

        var loaded = AudioResourceManager.Instance.LoadFromEngineAssetsFile(pack);
        loaded.Count.Should().Be(1);
        var resource = loaded[0];
        resource.Volume = 0.6f;
        resource.Pan = 0.25f;
        resource.PlaybackSpeed = 0.75f;
        resource.IsLooping = true;
        var key = resource.Key;

        //Act
        Engine.Instance.State.SaveToFile(savePath);
        EngineState.LoadFromFile(savePath);

        //Assert - the pack re-loaded and the spec's settings were applied to its resource.
        AudioResourceManager.Instance.TryGet(key, out var reloaded).Should().BeTrue();
        (Math.Abs(reloaded!.Volume - 0.6f) < 0.0001f).Should().BeTrue();
        (Math.Abs(reloaded.Pan - 0.25f) < 0.0001f).Should().BeTrue();
        reloaded.PlaybackSpeed.Should().Be(0.75f); // applied by EngineState.MergeAudio
        reloaded.IsLooping.Should().BeTrue();
    }

    [Fact]
    public void Compressed_save_files_roundtrip()
    {
        //Arrange
        var savePath = Path.Combine(_workDirectory, "save_gz.json.gz");
        var scene = new Scene { ID = "scene-gz" };
        scene.AddLayer(columnCount: 2, rowCount: 2, width: 8, height: 8);

        //Act
        Engine.Instance.State.SaveToFile(savePath, compress: true);
        EngineState.LoadFromFile(savePath, compressed: true);

        //Assert
        var loaded = Scene.GetSceneByID("scene-gz");
        loaded.Should().NotBeNull();
        loaded!.Count().Should().Be(1);
    }

    [Fact]
    public void MergeFromFile_preserves_existing_scenes_when_not_overwriting()
    {
        //Arrange - save one scene, then create a different one and merge the save back in.
        var savePath = Path.Combine(_workDirectory, "save_merge.json");
        _ = new Scene { ID = "scene-saved" };
        Engine.Instance.State.SaveToFile(savePath);

        Scene.ClearAllScenes();
        var liveScene = new Scene { ID = "scene-live" };

        //Act
        EngineState.MergeFromFile(savePath, overwriteExisting: false);

        //Assert - both scenes present, the live one untouched.
        ReferenceEquals(Scene.GetSceneByID("scene-live"), liveScene).Should().BeTrue();
        Scene.GetSceneByID("scene-saved").Should().NotBeNull();
    }

    [Fact]
    public void Save_files_written_before_by_frame_collision_mode_still_load()
    {
        //Arrange - a save whose tile carries only the pre-existing AdjustCollisionArea member.
        var imagePath = WriteTilesheetPng("legacy_sheet.png", tileSize: 16, columns: 2, rows: 2);
        var savePath = Path.Combine(_workDirectory, "save_legacy.json");

        var sheet = TilesheetRegistry.Instance.LoadFromImageFile("legacy_sheet", imagePath);
        sheet.DefaultRegion.TileSize = new Size(16, 16);
        sheet.DefaultRegion.SetFrameCollisionAdjust(0, 0, new CollisionAdjust(6, 6, 6, 6));

        var scene = new Scene { ID = "scene-legacy" };
        var layer = scene.AddLayer(columnCount: 2, rowCount: 2, width: 16, height: 16, zOrder: 0, parallax: 1f);
        layer[0, 0]!.CurrentFrame = new Frame(sheet, 0, 0);
        layer[0, 0]!.AdjustCollisionArea = new CollisionAdjust(1, 2, 3, 4);

        Engine.Instance.State.SaveToFile(savePath);

        //Act - strip every AdjustCollisionAreaByFrame member, as a pre-#246 save file would be.
        var document = JsonNode.Parse(File.ReadAllText(savePath))!;
        RemoveMember(document, "AdjustCollisionAreaByFrame");
        File.WriteAllText(savePath, document.ToJsonString());

        ClearAllEngineState();
        TilesheetRegistry.Instance.LoadFromImageFile("legacy_sheet", imagePath).DefaultRegion.TileSize =
            new Size(16, 16);
        EngineState.LoadFromFile(savePath);

        //Assert - the saved adjustment survives and by-frame mode defaults to off.
        var loadedLayer = Scene.GetSceneByID("scene-legacy")!.First();
        loadedLayer[0, 0]!.AdjustCollisionArea.Should().Be(new CollisionAdjust(1, 2, 3, 4));
        loadedLayer[0, 0]!.AdjustCollisionAreaByFrame.Should().BeFalse();
    }

    private static void RemoveMember(JsonNode node, string memberName)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                jsonObject.Remove(memberName);

                foreach (var property in jsonObject.ToList())
                {
                    if (property.Value is not null)
                        RemoveMember(property.Value, memberName);
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray.ToList())
                {
                    if (item is not null)
                        RemoveMember(item, memberName);
                }

                break;
        }
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
                    paint.Color = new SKColor((byte)(40 * x + 40), (byte)(40 * y + 40), 200);
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

    private string WriteWav(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        const int sampleRate = 8000;
        const int sampleCount = 80;
        int dataLength = sampleCount * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int i = 0; i < sampleCount; i++)
        {
            writer.Write((short)(i * 100));
        }

        return path;
    }
}
