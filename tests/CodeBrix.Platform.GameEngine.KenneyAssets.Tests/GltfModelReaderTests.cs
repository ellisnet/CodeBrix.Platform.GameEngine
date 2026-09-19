using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Models;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers reading the glTF models of a Kenney pack into the engine's model contract: the geometry and
/// materials that come out, strict resolution of the textures that sit beside a model, the animation
/// names a model offers, and the clips baked from them.
/// </summary>
public class GltfModelReaderTests : IDisposable
{
    private const string BlockyCharacterKey = "blocky-characters/Models/GLB format/character-a";
    private const string BlockyCharacterPath = "Models/GLB format/character-a.glb";
    private const string BlockyTexturePath = "Models/GLB format/Textures/texture-a.png";
    private const string BrickKey = "brick-kit/Models/GLB format/bevel-hq-brick-1x1";

    private readonly GltfModelReader _reader = new();
    private readonly TestModelSources _sources = new();

    /// <summary>Closes the fixture sources and releases the reader's cached documents.</summary>
    public void Dispose()
    {
        _reader.Dispose();
        _sources.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Read_returns_geometry_and_materials_for_a_static_model()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, BrickKey);

        //Act
        GameModel model = _reader.Read(entry);

        //Assert
        model.Meshes.Count.Should().Be(1);
        model.Materials.Count.Should().Be(1);
        model.TriangleCount.Should().BeGreaterThan(0);
        model.VertexCount.Should().BeGreaterThan(0);
        model.Meshes[0].Normals.Length.Should().Be(model.Meshes[0].Positions.Length);
        model.Meshes[0].TexCoords.Length.Should().Be(model.Meshes[0].VertexCount * 2);
        model.Meshes[0].Indices.Length.Should().Be(model.TriangleCount * 3);
        model.Meshes[0].MaterialIndex.Should().Be(0);
        model.AnimationNames.Should().BeEmpty();
        model.Animations.Should().BeEmpty();
    }

    [Fact]
    public void Read_reports_sane_bounds_and_a_pivot_inside_them()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, BrickKey);

        //Act
        GameModel model = _reader.Read(entry);

        //Assert
        model.BoundsMax.X.Should().BeGreaterThan(model.BoundsMin.X);
        model.BoundsMax.Y.Should().BeGreaterThan(model.BoundsMin.Y);
        model.BoundsRadius.Should().BeGreaterThan(0f);
        model.Pivot.Should().NotBeNull();
        model.Pivot!.Value.X.Should().BeInRange(model.BoundsMin.X, model.BoundsMax.X);
        model.Pivot.Value.Y.Should().BeInRange(model.BoundsMin.Y, model.BoundsMax.Y);
        model.Pivot.Value.Z.Should().BeInRange(model.BoundsMin.Z, model.BoundsMax.Z);
    }

    [Fact]
    public void Read_decodes_the_base_color_texture_beside_the_model()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        GameModel model = _reader.Read(entry);
        GameModelMaterial material = model.Materials[0];

        //Assert - the blocky characters share one 1024 by 1024 texture per character
        material.BaseColorTextureWidth.Should().Be(1024);
        material.BaseColorTextureHeight.Should().Be(1024);
        material.BaseColorTextureRgba.Should().NotBeNull();
        material.BaseColorTextureRgba!.Length.Should().Be(1024 * 1024 * 4);
        material.AlphaMode.Should().Be(GameModelAlphaMode.Opaque);
    }

    [Fact]
    public void Read_resolves_a_shared_texture_from_the_model_s_own_folder()
    {
        //Arrange - the brick kit holds three Textures/colormap.png, one beside each model format
        KenneyPackIndex index = _sources.Index(TestFixtures.BrickKitFileName);
        KenneyAssetEntry entry = TestModelSources.Entry(index, BrickKey);

        //Act
        GameModel model = _reader.Read(entry);
        string? resolved = entry.Pack.Archive.ResolveDependencyPath(
            entry.Path, "Textures/colormap.png", strict: true);

        //Assert
        resolved.Should().Be("Models/GLB format/Textures/colormap.png");
        model.Materials[0].BaseColorTextureWidth.Should().Be(512);
        model.Materials[0].BaseColorTextureHeight.Should().Be(512);
    }

    [Fact]
    public void Read_fails_naming_the_path_when_a_texture_is_missing()
    {
        //Arrange - the same model bytes, repackaged without the texture that sits beside them
        byte[] glb = _sources.ReadFromBundle(TestFixtures.BlockyCharactersFileName, BlockyCharacterPath);
        string bundle = _sources.BuildBundle("missingtexture", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Missing Texture", "1.0")),
            [BlockyCharacterPath] = glb,
        });

        KenneyAssetEntry entry = _sources.Entry(bundle, "missing-texture/Models/GLB format/character-a");

        //Act
        Action act = () => _reader.Read(entry);

        //Assert
        FileNotFoundException failure = act.Should().Throw<FileNotFoundException>().Which;
        failure.Message.Should().Contain("Textures/texture-a.png");
        failure.Message.Should().Contain(BlockyCharacterPath);
    }

    [Fact]
    public void Read_lists_every_animation_of_an_animated_model_without_baking_any()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        GameModel model = _reader.Read(entry);

        //Assert - 27 clips, from "static" through the holding and attack poses
        model.AnimationNames.Count.Should().Be(27);
        model.AnimationNames.Should().Contain("idle");
        model.AnimationNames.Should().Contain("walk");
        model.AnimationNames.Should().Contain("sprint");
        model.Animations.Should().BeEmpty();
    }

    [Fact]
    public void Read_bakes_only_the_animations_the_options_asked_for()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);
        ModelMaterializeOptions options = new()
        {
            AnimationNames = ["WALK", "walk", "idle"],
            AnimationFramesPerSecond = 12,
        };

        //Act
        GameModel model = _reader.Read(entry, options);

        //Assert - asked for twice in different case, baked once, under the model's own spelling
        model.Animations.Count.Should().Be(2);
        model.Animations.Select(clip => clip.Name).Should().BeEquivalentTo(["walk", "idle"]);
        model.Animations.Should().AllSatisfy(clip => clip.FrameRate.Should().Be(12));
        model.Animations.Should().AllSatisfy(clip => clip.IsCompatibleWith(model).Should().BeTrue());
    }

    [Fact]
    public void Read_bakes_every_animation_when_asked_to()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);
        ModelMaterializeOptions options = new() { BakeAllAnimations = true, AnimationFramesPerSecond = 8 };

        //Act
        GameModel model = _reader.Read(entry, options);

        //Assert
        model.Animations.Count.Should().Be(model.AnimationNames.Count);
        model.Animations.Should().AllSatisfy(clip => clip.IsCompatibleWith(model).Should().BeTrue());
    }

    [Fact]
    public void BakeClip_samples_the_clip_at_the_requested_rate()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        GameModelAnimationClip clip = _reader.BakeClip(entry, "walk", 24);

        //Assert - the walk cycle lasts two thirds of a second, so 24 fps covers it in 16 frames
        clip.Name.Should().Be("walk");
        clip.FrameRate.Should().Be(24);
        clip.Duration.Should().BeApproximately(0.6667f, 0.001f);
        clip.FrameCount.Should().Be(16);
        clip.Frames.Should().AllSatisfy(frame => frame.Meshes.Count.Should().Be(1));
    }

    [Fact]
    public void BakeClip_produces_frames_aligned_with_the_model_it_was_read_from()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);
        GameModel model = _reader.Read(entry);

        //Act
        GameModelAnimationClip clip = _reader.BakeClip(entry, "idle", 12);

        //Assert
        clip.IsCompatibleWith(model).Should().BeTrue();
        clip.Frames[0].Meshes[0].Positions.Length
            .Should().Be(model.Meshes[0].Positions.Length);
        clip.Frames[0].Meshes[0].Normals.Length
            .Should().Be(model.Meshes[0].Normals.Length);
    }

    [Fact]
    public void BakeClip_frames_differ_from_each_other_over_a_moving_animation()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        GameModelAnimationClip clip = _reader.BakeClip(entry, "walk", 12);

        //Assert - a walk cycle moves, so at least one pose differs from the first. The half-way pose
        //  is deliberately NOT the one compared: this cycle swaps left and right limbs at its
        //  midpoint, which lands the vertices back where they started.
        float[] first = clip.Frames[0].Meshes[0].Positions;
        float largestDelta = 0f;

        foreach (GameModelAnimationFrame frame in clip.Frames)
        {
            float[] positions = frame.Meshes[0].Positions;
            for (int i = 0; i < first.Length; i++)
            {
                largestDelta = MathF.Max(largestDelta, MathF.Abs(first[i] - positions[i]));
            }
        }

        largestDelta.Should().BeGreaterThan(0.01f);
    }

    [Fact]
    public void BakeClip_rejects_an_unknown_animation_name_and_lists_the_real_ones()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        Action act = () => _reader.BakeClip(entry, "moonwalk");

        //Assert
        ArgumentException failure = act.Should().Throw<ArgumentException>().Which;
        failure.Message.Should().Contain("moonwalk");
        failure.Message.Should().Contain("walk");
        failure.Message.Should().Contain("sprint");
    }

    [Fact]
    public void BakeClip_rejects_a_frame_rate_below_one()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);

        //Act
        Action act = () => _reader.BakeClip(entry, "walk", 0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Read_caches_the_document_of_an_animated_model_but_not_of_a_static_one()
    {
        //Arrange
        KenneyAssetEntry animated =
            _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);
        KenneyAssetEntry staticModel = _sources.Entry(TestFixtures.BrickKitFileName, BrickKey);

        //Act
        _reader.Read(staticModel);
        int afterStatic = _reader.CachedDocumentCount;
        _reader.Read(animated);
        _reader.BakeClip(animated, "walk", 8);
        int afterAnimated = _reader.CachedDocumentCount;
        _reader.Dispose();

        //Assert
        afterStatic.Should().Be(0);
        afterAnimated.Should().Be(1);
        _reader.CachedDocumentCount.Should().Be(0);
    }

    [Fact]
    public void Read_reads_the_same_model_from_a_zip_and_from_an_extracted_folder()
    {
        //Arrange
        string folder = _sources.Extract(TestFixtures.BlockyCharactersFileName, "blockyfolder");
        KenneyAssetEntry fromZip =
            _sources.Entry(TestFixtures.BlockyCharactersFileName, BlockyCharacterKey);
        KenneyAssetEntry fromFolder = _sources.Entry(folder, BlockyCharacterKey);

        //Act
        GameModel zipModel = _reader.Read(fromZip);
        GameModel folderModel = _reader.Read(fromFolder);

        //Assert
        folderModel.VertexCount.Should().Be(zipModel.VertexCount);
        folderModel.TriangleCount.Should().Be(zipModel.TriangleCount);
        folderModel.AnimationNames.Count.Should().Be(zipModel.AnimationNames.Count);
        folderModel.Materials[0].BaseColorTextureWidth
            .Should().Be(zipModel.Materials[0].BaseColorTextureWidth);
    }

    [Fact]
    public void Read_and_BakeClip_handle_a_skinned_model()
    {
        //Arrange - no Kenney fixture is skinned, so the skinned path needs a built model
        string bundle = _sources.BuildBundle("skinned", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Skinned Sample", "1.0")),
            ["Models/GLB format/arm.glb"] = TestGltfModels.BuildSkinnedGlb("bend"),
        });

        KenneyAssetEntry entry = _sources.Entry(bundle, "skinned-sample/Models/GLB format/arm");

        //Act
        GameModel model = _reader.Read(entry);
        GameModelAnimationClip clip = _reader.BakeClip(entry, "bend", 10);

        //Assert
        model.AnimationNames.Should().Contain("bend");
        model.TriangleCount.Should().Be(2);
        clip.IsCompatibleWith(model).Should().BeTrue();

        //Skinning moves the far end of the arm, so the last pose is not the first pose
        float[] first = clip.Frames[0].Meshes[0].Positions;
        float[] last = clip.Frames[^1].Meshes[0].Positions;
        bool moved = false;
        for (int i = 0; i < first.Length; i++)
        {
            if (MathF.Abs(first[i] - last[i]) > 1e-3f) { moved = true; break; }
        }

        moved.Should().BeTrue();
    }

    [Fact]
    public void Read_reads_a_gltf_document_whose_buffer_sits_beside_it()
    {
        //Arrange - the JSON form of glTF, with its geometry in a satellite .bin file
        Dictionary<string, byte[]> entries = new()
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Satellite Sample", "1.0")),
        };

        foreach ((string name, byte[] bytes) in TestGltfModels.BuildSatelliteGltf())
        {
            entries[$"Models/glTF format/{name}"] = bytes;
        }

        string bundle = _sources.BuildBundle("satellite", entries);
        KenneyAssetEntry entry = _sources.Entry(bundle, "satellite-sample/Models/glTF format/triangle");

        //Act
        GameModel model = _reader.Read(entry);

        //Assert
        entry.Extension.Should().Be("gltf");
        model.TriangleCount.Should().Be(1);
        model.VertexCount.Should().Be(3);
        model.BoundsMax.Y.Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void Read_refuses_a_model_file_that_is_not_glTF()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(
            TestFixtures.BlockyCharactersFileName,
            "blocky-characters/Models/FBX format/character-a.fbx");

        //Act
        Action act = () => _reader.Read(entry);

        //Assert
        UnsupportedGameAssetException failure = act.Should().Throw<UnsupportedGameAssetException>().Which;
        failure.Kind.Should().Be(GameAssetKind.Model3D);
        failure.Key.Should().Contain("character-a.fbx");
    }

    [Fact]
    public void Read_refuses_a_document_that_is_not_a_model_at_all()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(
            TestFixtures.BlockyCharactersFileName, "blocky-characters/License.txt");

        //Act
        Action act = () => _reader.Read(entry);

        //Assert
        act.Should().Throw<UnsupportedGameAssetException>();
    }

    [Fact]
    public void Read_fails_with_a_clear_message_for_a_file_that_is_not_a_glTF_document()
    {
        //Arrange
        string bundle = _sources.BuildBundle("notamodel", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Not A Model", "1.0")),
            ["Models/broken.glb"] = Encoding.UTF8.GetBytes("this is not a glTF binary at all"),
        });

        KenneyAssetEntry entry = _sources.Entry(bundle, "not-a-model/Models/broken");

        //Act
        Action act = () => _reader.Read(entry);

        //Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*broken.glb*");
    }

    [Fact]
    public void Read_after_Dispose_throws()
    {
        //Arrange
        KenneyAssetEntry entry = _sources.Entry(TestFixtures.BrickKitFileName, BrickKey);
        _reader.Dispose();

        //Act
        Action act = () => _reader.Read(entry);

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}
