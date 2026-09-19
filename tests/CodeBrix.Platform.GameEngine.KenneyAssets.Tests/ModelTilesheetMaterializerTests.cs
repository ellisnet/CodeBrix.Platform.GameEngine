using System;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Models;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers turning a glTF model into an engine tilesheet: the region names and grid the output layout
/// contract prescribes, the cells actually carrying pixels, the size guard, idempotence by key, and
/// the refusal of a model file that is not glTF.
/// </summary>
public class ModelTilesheetMaterializerTests : IDisposable
{
    private const string AnimatedKey = "blocky-characters/Models/GLB format/character-a";
    private const string StaticKey = "brick-kit/Models/GLB format/bevel-hq-brick-1x1";

    private readonly ModelTilesheetMaterializer _materializer;
    private readonly GltfModelReader _reader = new();
    private readonly TestModelSources _sources = new();

    /// <summary>Creates the materializer under test.</summary>
    public ModelTilesheetMaterializerTests()
    {
        _materializer = new ModelTilesheetMaterializer(_reader);
    }

    /// <summary>
    /// Empties the engine's process-global tilesheet registry, closes the fixture sources and
    /// releases the reader's cached documents.
    /// </summary>
    public void Dispose()
    {
        TilesheetRegistry.Instance.Clear();
        _reader.Dispose();
        _sources.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Materialize_lays_a_static_model_out_as_one_rest_pose_column_per_direction()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, StaticKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(32, 32),
            Directions = 4,
        });

        //Act
        Tilesheet sheet = _materializer.Materialize(entry, "model-static", options);
        TilesheetRegion? rest = sheet.GetRegion(ModelRenderOptions.RestPoseRegionName);

        //Assert
        sheet.Name.Should().Be("model-static");
        sheet.SkBitmap.Width.Should().Be(32);
        sheet.SkBitmap.Height.Should().Be(128);
        rest.Should().NotBeNull();
        rest!.Columns.Should().Be(1);
        rest.Rows.Should().Be(4);
        rest.TileSize.Should().Be(new Size(32, 32));
    }

    [Fact]
    public void Materialize_gives_each_animation_a_region_of_frames_by_directions()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, AnimatedKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(24, 24),
            Directions = 2,
            AnimationNames = ["walk"],
            AnimationFramesPerSecond = 6,
            Supersample = 1,
        });

        //Act
        Tilesheet sheet = _materializer.Materialize(entry, "model-walk", options);
        TilesheetRegion? walk = sheet.GetRegion("walk");

        //Assert - the walk cycle lasts two thirds of a second, which is four columns at 6 fps
        walk.Should().NotBeNull();
        walk!.Columns.Should().Be(4);
        walk.Rows.Should().Be(2);
        walk.TileSize.Should().Be(new Size(24, 24));
        sheet.GetRegion(ModelRenderOptions.RestPoseRegionName).Should().NotBeNull();
        sheet.SkBitmap.Width.Should().Be(96);
        sheet.SkBitmap.Height.Should().Be(96);
    }

    [Fact]
    public void Materialize_addresses_a_frame_by_region_column_and_row()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, AnimatedKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(24, 24),
            Directions = 2,
            AnimationNames = ["walk"],
            AnimationFramesPerSecond = 6,
            Supersample = 1,
        });

        //Act
        Tilesheet sheet = _materializer.Materialize(entry, "model-addressing", options);
        Frame first = sheet["walk", 0, 0];
        Frame last = sheet["walk", 3, 1];

        //Assert
        first.TileSize.Should().Be(new Size(24, 24));
        first.SkBitmap.Should().NotBeNull();
        first.SkBitmap!.Width.Should().Be(24);
        first.SkBitmap.Height.Should().Be(24);
        last.SkBitmap.Should().NotBeNull();
        last.SkBitmap!.Width.Should().Be(24);
    }

    [Fact]
    public void Materialize_draws_the_model_into_every_cell_of_every_region()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, AnimatedKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(24, 24),
            Directions = 4,
            AnimationNames = ["walk"],
            AnimationFramesPerSecond = 6,
            Supersample = 1,
        });

        //Act
        Tilesheet sheet = _materializer.Materialize(entry, "model-cells", options);

        //Assert - every cell of every region carries a drawn character
        foreach (string regionName in new[] { ModelRenderOptions.RestPoseRegionName, "walk" })
        {
            TilesheetRegion region = sheet[regionName];

            for (int row = 0; row < region.Rows; row++)
            {
                for (int column = 0; column < region.Columns; column++)
                {
                    using SKBitmap? cell = sheet[regionName, column, row].SkBitmap;
                    cell.Should().NotBeNull();
                    TestGltfModels.Measure(cell!).Coverage.Should().BeGreaterThan(0.05d);
                }
            }
        }
    }

    [Fact]
    public void Materialize_returns_the_sheet_already_registered_under_the_key()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, StaticKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(16, 16),
            Directions = 2,
        });

        //Act
        Tilesheet first = _materializer.Materialize(entry, "model-idempotent", options);
        Tilesheet second = _materializer.Materialize(entry, "model-idempotent", options);

        //Assert - the second call must not re-register, which would dispose the first sheet
        second.Should().BeSameAs(first);
        first.SkBitmap.IsNull.Should().BeFalse();
        TilesheetRegistry.Instance.Names.Count(name => name == "model-idempotent").Should().Be(1);
    }

    [Fact]
    public void Materialize_renders_a_second_variant_under_another_key()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, StaticKey);

        //Act - the caller resolves TilesheetMaterializeOptions.RegisterAs into the key it passes
        Tilesheet standard = _materializer.Materialize(
            entry, StaticKey, Options(new ModelRenderOptions { FrameSize = new Size(16, 16), Directions = 2 }));

        Tilesheet variant = _materializer.Materialize(
            entry, "brick-front-only", Options(new ModelRenderOptions
            {
                FrameSize = new Size(16, 16),
                Directions = 1,
            }));

        //Assert
        variant.Should().NotBeSameAs(standard);
        standard.SkBitmap.Height.Should().Be(32);
        variant.SkBitmap.Height.Should().Be(16);
        TilesheetRegistry.Instance.TryGet(StaticKey, out _).Should().BeTrue();
        TilesheetRegistry.Instance.TryGet("brick-front-only", out _).Should().BeTrue();
    }

    [Fact]
    public void Materialize_uses_the_provider_defaults_when_no_model_options_are_given()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, StaticKey);

        //Act
        Tilesheet sheet = _materializer.Materialize(entry, "model-defaults");

        //Assert - eight directions of the rest pose at 128 pixels a cell
        sheet.SkBitmap.Width.Should().Be(128);
        sheet.SkBitmap.Height.Should().Be(1024);
        sheet[ModelRenderOptions.RestPoseRegionName].Rows.Should().Be(8);
    }

    [Fact]
    public void Materialize_refuses_a_sheet_that_would_be_too_large()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, AnimatedKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(1024, 1024),
            Directions = 8,
            AnimationNames = ["walk"],
            AnimationFramesPerSecond = 6,
        });

        //Act
        Action act = () => _materializer.Materialize(entry, "model-toolarge", options);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*8192*");
        TilesheetRegistry.Instance.TryGet("model-toolarge", out _).Should().BeFalse();
    }

    [Fact]
    public void Materialize_rejects_an_animation_the_model_does_not_have()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, AnimatedKey);
        TilesheetMaterializeOptions options = Options(new ModelRenderOptions
        {
            FrameSize = new Size(16, 16),
            Directions = 1,
            AnimationNames = ["moonwalk"],
        });

        //Act
        Action act = () => _materializer.Materialize(entry, "model-unknownclip", options);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*moonwalk*");
    }

    [Fact]
    public void Materialize_refuses_a_model_file_that_is_not_glTF()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(
            TestFixtures.BlockyCharactersFileName,
            "blocky-characters/Models/OBJ format/character-a.obj");

        //Act
        Action act = () => _materializer.Materialize(entry, "model-obj");

        //Assert
        UnsupportedGameAssetException failure = act.Should().Throw<UnsupportedGameAssetException>().Which;
        failure.Kind.Should().Be(GameAssetKind.Model3D);
        failure.Key.Should().Be("model-obj");
    }

    [Fact]
    public void Materialize_rejects_a_blank_key()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, StaticKey);

        //Act
        Action act = () => _materializer.Materialize(entry, "   ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    private static TilesheetMaterializeOptions Options(ModelRenderOptions modelRender) =>
        new() { ModelRender = modelRender };
}
