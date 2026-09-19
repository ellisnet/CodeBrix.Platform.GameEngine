using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the whole way round: registering Kenney bundles with the engine and then loading one asset
/// of every kind through the engine's own asset provider registry, the way game code does it. Also
/// covers registering twice, registering a second provider, and what the registry refuses.
/// </summary>
public class EngineKenneyAssetsExtensionsTests : IDisposable
{
    private const string SimulatedSlug = "simulated-bundle";
    private const string PuzzleSlug = "puzzle-pack";
    private const string BlockySlug = "blocky-characters";
    private const string AnimatedModelKey = "kenney:blocky-characters/Models/GLB format/character-a";

    private readonly GameAssetProviderRegistry _registry = Engine.Instance.Managers.AssetProviders;
    private readonly List<string> _audioKeys = [];
    private readonly List<string> _fontKeys = [];
    private readonly List<Scene> _scenes = [];

    /// <summary>
    /// Starts every test with an empty provider registry and an empty tilesheet registry, both of which
    /// are process-global; this assembly runs its tests serially.
    /// </summary>
    public EngineKenneyAssetsExtensionsTests()
    {
        _registry.Clear();
        TilesheetRegistry.Instance.Clear();
    }

    /// <summary>
    /// Unregisters the providers this fixture registered - which closes their archives - and gives back
    /// everything they materialized into the engine's process-global registries.
    /// </summary>
    public void Dispose()
    {
        _registry.Clear();

        foreach (Scene scene in _scenes) { scene.Dispose(); }

        Scene.ClearAllScenes();
        TilesheetRegistry.Instance.Clear();

        foreach (string key in _audioKeys) { AudioResourceManager.Instance.Unload(key); }
        foreach (string key in _fontKeys) { FontManager.Instance.Remove(key); }

        AudioSystem.Shutdown();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UseKenneyAssets_registers_a_provider_the_engine_resolves_keys_through()
    {
        //Arrange & Act
        KenneyGameAssetProvider provider = Register(TestFixtures.SimulatedBundleFileName);

        //Assert
        _registry.Count.Should().Be(1);
        _registry.Providers.Should().ContainSingle();
        _registry.Providers[0].Should().BeSameAs(provider);
        _registry.TryFind($"kenney:{SimulatedSlug}/Audio/radar2", out IGameAssetProvider? found)
            .Should().BeTrue();
        found.Should().BeSameAs(provider);
        _registry.TryDescribe(
            $"kenney:{SimulatedSlug}/Tiled/tilemap-example-a", out GameAssetDescriptor? map)
            .Should().BeTrue();
        map!.Kind.Should().Be(GameAssetKind.TiledMap);
        _registry.Describe(new GameAssetQuery { Kind = GameAssetKind.Font }).Count.Should().Be(3);
    }

    [Fact]
    public void LoadTilesheet_materializes_an_image_and_a_sprite_atlas_through_the_registry()
    {
        //Arrange
        Register(TestFixtures.PuzzlePackFileName);
        string imageKey = $"kenney:{PuzzleSlug}/PNG/Double/ballBlue";
        string atlasKey = $"kenney:{PuzzleSlug}/Spritesheet/spritesheet_default";

        //Act
        Tilesheet image = _registry.LoadTilesheet(imageKey);
        Tilesheet atlas = _registry.LoadTilesheet(atlasKey);
        Tilesheet again = _registry.LoadTilesheet(imageKey);

        //Assert - a loose image is one whole-image tile
        image.Name.Should().Be(imageKey);
        image[0, 0].SkBitmap!.Width.Should().Be(44);
        //Assert - an atlas is one named region per frame
        atlas.Name.Should().Be(atlasKey);
        atlas.GetRegion("ballBlue").Should().NotBeNull();
        atlas["ballBlue", 0, 0].SkBitmap!.Width.Should().Be(22);
        //Assert - the first materialization of a key wins
        again.Should().BeSameAs(image);
    }

    [Fact]
    public void LoadTilesheet_rasterizes_a_vector_at_the_size_asked_for()
    {
        //Arrange
        Register(TestFixtures.PuzzlePackFileName);
        string key = $"kenney:{PuzzleSlug}/Vector/puzzleAssets_vector";

        //Act
        Tilesheet sheet = _registry.LoadTilesheet(
            key, new TilesheetMaterializeOptions { VectorRasterSize = new Size(64, 48) });

        //Assert
        sheet.SkBitmap.Width.Should().Be(64);
        sheet.SkBitmap.Height.Should().Be(48);
        sheet.DefaultRegion.TileSize.Should().Be(new Size(64, 48));
    }

    [Fact]
    public void LoadAudio_materializes_a_sound_through_the_registry()
    {
        //Arrange
        Register(TestFixtures.SimulatedBundleFileName);
        string key = AudioKey($"kenney:{SimulatedSlug}/Audio/radar2");

        //Act
        AudioResource resource = _registry.LoadAudio(key, volume: 0.5f, pan: -0.25f);
        AudioResource again = _registry.LoadAudio(key);

        //Assert
        resource.Key.Should().Be(key);
        resource.SourceExtension.Should().Be(".ogg");
        resource.Volume.Should().BeApproximately(0.5f, 0.0001f);
        resource.Pan.Should().BeApproximately(-0.25f, 0.0001f);
        AudioResourceManager.Instance.TryGet(key, out AudioResource? registered).Should().BeTrue();
        registered.Should().BeSameAs(resource);
        //The first materialization of a key wins, so the second call's volume is not applied
        again.Should().BeSameAs(resource);
        again.Volume.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public void LoadFont_materializes_a_typeface_through_the_registry()
    {
        //Arrange
        Register(TestFixtures.SimulatedBundleFileName);
        string key = FontKey($"kenney:{SimulatedSlug}/Fonts/Kenney Space");

        //Act
        SKTypeface typeface = _registry.LoadFont(key);

        //Assert
        typeface.GlyphCount.Should().BeGreaterThan(0);
        typeface.FamilyName.Should().Contain("Kenney");
        FontManager.Instance.TryGet(key, out SKTypeface? registered).Should().BeTrue();
        registered.Should().BeSameAs(typeface);
    }

    [Fact]
    public void ImportTiledMap_imports_a_map_into_a_scene_through_the_registry()
    {
        //Arrange
        Register(TestFixtures.SimulatedBundleFileName);
        string key = $"kenney:{SimulatedSlug}/Tiled/tilemap-example-a";
        Scene scene = NewScene();

        //Act
        TiledMapImport import = _registry.ImportTiledMap(
            key, scene, new TiledMapImportOptions { ZOrderBase = 5 });

        //Assert
        import.Scene.Should().BeSameAs(scene);
        import.Layers.Count.Should().Be(4);
        import.Layers[0].ZOrder.Should().Be(5);
        import.TileSize.Should().Be(new Size(18, 18));
        import.MapSizePx.Should().Be(new Size(468, 270));
        import.Tilesheets.Count.Should().Be(2);
        import.Tilesheets.Should().AllSatisfy(sheet => sheet.Name.Should().StartWith(key + "#"));
        TilesheetRegistry.Instance.TryGet($"{key}#tileset-tiles", out Tilesheet? tiles).Should().BeTrue();
        tiles!.GetRegion("tiles").Should().NotBeNull();
    }

    [Fact]
    public void LoadModel_and_LoadModelAnimation_deliver_model_data_through_the_registry()
    {
        //Arrange
        Register(TestFixtures.BlockyCharactersFileName);

        //Act
        GameModel model = _registry.LoadModel(AnimatedModelKey);
        GameModelAnimationClip clip = _registry.LoadModelAnimation(
            AnimatedModelKey, "walk", framesPerSecond: 6);

        //Assert - nothing is baked unless the caller asks, but the names are always there
        model.Meshes.Should().NotBeEmpty();
        model.TriangleCount.Should().BeGreaterThan(0);
        model.AnimationNames.Should().Contain("walk");
        model.Animations.Should().BeEmpty();
        model.BoundsRadius.Should().BeGreaterThan(0f);

        //Assert - a clip baked on demand fits the model the same asset materializes to
        clip.Name.Should().Be("walk");
        clip.FrameRate.Should().Be(6);
        clip.Duration.Should().BeGreaterThan(0f);
        clip.FrameCount.Should().BeGreaterThan(1);
        clip.IsCompatibleWith(model).Should().BeTrue();
    }

    [Fact]
    public void LoadModel_bakes_the_animations_the_options_name()
    {
        //Arrange
        Register(TestFixtures.BlockyCharactersFileName);

        //Act
        GameModel model = _registry.LoadModel(
            AnimatedModelKey,
            new ModelMaterializeOptions { AnimationNames = ["walk"], AnimationFramesPerSecond = 6 });

        //Assert
        model.Animations.Should().ContainSingle();
        model.TryGetAnimation("WALK", out GameModelAnimationClip? clip).Should().BeTrue();
        clip!.Name.Should().Be("walk");
        clip.IsCompatibleWith(model).Should().BeTrue();
    }

    [Fact]
    public void LoadTilesheet_pre_renders_a_model_into_sprite_frames()
    {
        //Arrange
        Register(TestFixtures.BlockyCharactersFileName);
        TilesheetMaterializeOptions options = new()
        {
            ModelRender = new ModelRenderOptions
            {
                FrameSize = new Size(24, 24),
                Directions = 2,
                Supersample = 1,
                AnimationNames = ["walk"],
                AnimationFramesPerSecond = 6,
            },
        };

        //Act
        Tilesheet sheet = _registry.LoadTilesheet(AnimatedModelKey, options);

        //Assert - one uniform-grid region per animation, plus the rest pose; columns are frames and
        //  rows are camera directions
        sheet.Name.Should().Be(AnimatedModelKey);
        TilesheetRegion rest = sheet.GetRegion(ModelRenderOptions.RestPoseRegionName)!;
        rest.Should().NotBeNull();
        rest.Columns.Should().Be(1);
        rest.Rows.Should().Be(2);
        TilesheetRegion walk = sheet.GetRegion("walk")!;
        walk.Should().NotBeNull();
        walk.Rows.Should().Be(2);
        walk.Columns.Should().BeGreaterThan(1);
        walk.TileSize.Should().Be(new Size(24, 24));
        //Something was drawn: the character is not an empty cell
        HasAnyVisiblePixel(sheet["walk", 0, 0]).Should().BeTrue();
    }

    [Fact]
    public void The_registry_refuses_an_asset_the_provider_only_lists()
    {
        //Arrange
        Register(TestFixtures.BlockyCharactersFileName, TestFixtures.SimulatedBundleFileName);
        string fbx = $"kenney:{BlockySlug}/Models/FBX format/character-a.fbx";
        string licence = $"kenney:{BlockySlug}/License.txt";
        string image = $"kenney:{SimulatedSlug}/PNG/tile_0079";

        //Act
        Action modelData = () => _registry.LoadModel(fbx);
        Action modelSprites = () => _registry.LoadTilesheet(fbx);
        Action documentAsAudio = () => _registry.LoadAudio(licence);
        Action imageAsFont = () => _registry.LoadFont(image);
        Action unknownKey = () => _registry.LoadTilesheet("kenney:no-pack/nothing");

        //Assert
        modelData.Should().Throw<UnsupportedGameAssetException>()
            .Which.Message.Should().Contain(UnsupportedGameAssetException.DefaultMessage);
        modelSprites.Should().Throw<UnsupportedGameAssetException>();
        documentAsAudio.Should().Throw<UnsupportedGameAssetException>()
            .Which.Kind.Should().Be(GameAssetKind.Document);
        imageAsFont.Should().Throw<UnsupportedGameAssetException>()
            .Which.Kind.Should().Be(GameAssetKind.Image);
        unknownKey.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Calling_UseKenneyAssets_again_adds_sources_to_the_same_provider()
    {
        //Arrange
        KenneyGameAssetProvider first = Register(TestFixtures.SimulatedBundleFileName);

        //Act
        KenneyGameAssetProvider second = Register(TestFixtures.PuzzlePackFileName);

        //Assert
        second.Should().BeSameAs(first);
        _registry.Count.Should().Be(1);
        second.Packs.Count.Should().Be(2);
        _registry.TryDescribe($"kenney:{SimulatedSlug}/Audio/radar2", out _).Should().BeTrue();
        _registry.TryDescribe($"kenney:{PuzzleSlug}/PNG/Double/ballBlue", out _).Should().BeTrue();
    }

    [Fact]
    public void An_identifier_of_its_own_gives_a_second_set_of_assets_a_second_provider()
    {
        //Arrange
        KenneyGameAssetProvider game = Register(TestFixtures.SimulatedBundleFileName);

        //Act
        KenneyGameAssetProvider mods = Engine.Instance.UseKenneyAssets(new KenneyAssetsOptions
        {
            ProviderId = "mods",
            Sources = [TestFixtures.BundlePath(TestFixtures.PuzzlePackFileName)],
        });

        //Assert
        mods.Should().NotBeSameAs(game);
        _registry.Count.Should().Be(2);
        _registry.TryDescribe($"mods:{PuzzleSlug}/PNG/Double/ballBlue", out _).Should().BeTrue();
        //Each provider answers only for its own prefix
        game.TryDescribe($"mods:{PuzzleSlug}/PNG/Double/ballBlue", out _).Should().BeFalse();
        _registry.Describe().Count.Should().Be(game.AssetCount + mods.AssetCount);
    }

    [Fact]
    public void UseKenneyAssets_refuses_an_identifier_another_kind_of_provider_holds()
    {
        //Arrange
        using OtherGameAssetProvider other = new("kenney");
        _registry.Register(other);

        //Act
        Action act = () => Engine.Instance.UseKenneyAssets(
            TestFixtures.BundlePath(TestFixtures.SimulatedBundleFileName));

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*kenney*");
        //The provider that was there is still there, and still its own
        _registry.Providers.Should().ContainSingle();
        _registry.Providers[0].Should().BeSameAs(other);
        other.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void UseKenneyAssets_validates_its_arguments()
    {
        //Arrange
        Engine engine = Engine.Instance;

        //Act
        Action nullEngine = () => EngineKenneyAssetsExtensions.UseKenneyAssets(null!, "one.zip");
        Action nullPaths = () => engine.UseKenneyAssets((string[])null!);
        Action nullOptions = () => engine.UseKenneyAssets((KenneyAssetsOptions)null!);
        Action badIdentifier = () => engine.UseKenneyAssets(
            new KenneyAssetsOptions { ProviderId = "ken:ney" });

        //Assert
        nullEngine.Should().Throw<ArgumentNullException>();
        nullPaths.Should().Throw<ArgumentNullException>();
        nullOptions.Should().Throw<ArgumentNullException>();
        badIdentifier.Should().Throw<ArgumentException>().WithMessage("*colon*");
        _registry.Count.Should().Be(0);
    }

    [Fact]
    public void Unregistering_the_provider_closes_its_archives()
    {
        //Arrange
        KenneyGameAssetProvider provider = Register(TestFixtures.SimulatedBundleFileName);
        string key = $"kenney:{SimulatedSlug}/PNG/tile_0079";
        Tilesheet sheet = _registry.LoadTilesheet(key);

        //Act
        _registry.Unregister(provider.ProviderId).Should().BeTrue();

        //Assert - the provider was disposed with its registration
        Action materialize = () => provider.MaterializeTilesheet(
            Describe(provider, $"kenney:{SimulatedSlug}/PNG/tile_0055"));
        materialize.Should().Throw<ObjectDisposedException>();
        //What the engine was already given stays registered
        TilesheetRegistry.Instance.TryGet(key, out Tilesheet? stillThere).Should().BeTrue();
        stillThere.Should().BeSameAs(sheet);
    }

    private static GameAssetDescriptor Describe(KenneyGameAssetProvider provider, string key)
    {
        provider.TryDescribe(key, out GameAssetDescriptor? descriptor).Should().BeTrue();

        return descriptor!;
    }

    private static bool HasAnyVisiblePixel(Frame frame)
    {
        SKBitmap? bitmap = frame.SkBitmap;

        if (bitmap is null) { return false; }

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0) { return true; }
            }
        }

        return false;
    }

    private KenneyGameAssetProvider Register(params string[] bundleFileNames) =>
        Engine.Instance.UseKenneyAssets(
            [.. bundleFileNames.Select(TestFixtures.BundlePath)]);

    private string AudioKey(string key)
    {
        _audioKeys.Add(key);

        return key;
    }

    private string FontKey(string key)
    {
        _fontKeys.Add(key);

        return key;
    }

    private Scene NewScene()
    {
        Scene scene = new();
        _scenes.Add(scene);

        return scene;
    }

    //A provider of some other kind, to stand in an identifier's way
    private sealed class OtherGameAssetProvider(string providerId) : IGameAssetProvider
    {
        public string ProviderId { get; } = providerId;

        public IReadOnlySet<GameAssetKind> SupportedKinds { get; } = new HashSet<GameAssetKind>();

        public bool IsDisposed { get; private set; }

        public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null) => [];

        public bool TryDescribe(string key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor)
        {
            descriptor = null;

            return false;
        }

        public Stream OpenRaw(GameAssetDescriptor descriptor) =>
            throw new KeyNotFoundException("This provider holds nothing.");

        public void Dispose() => IsDisposed = true;
    }
}
