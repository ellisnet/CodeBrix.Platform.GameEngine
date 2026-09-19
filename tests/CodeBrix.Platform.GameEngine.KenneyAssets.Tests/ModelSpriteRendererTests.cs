using System;
using System.Drawing;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Models;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the software rasterizer that turns a model into sprite frames. Nothing here pins a pixel
/// value: the assertions are about structure - how much of a cell is covered, where the silhouette
/// sits, which colour dominates, which surface wins a depth test, and how two directions relate.
/// </summary>
public class ModelSpriteRendererTests
{
    //Light straight at the camera at yaw 0, so a surface facing the viewer is lit in full and the
    //  colour that comes out is the material's own
    private static readonly Vector3 FrontLight = new(0f, 0f, 1f);

    [Fact]
    public void RenderFrame_draws_the_model_and_leaves_the_cell_corners_transparent()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions() with { FitPadding = 0.1f });
        GameModel model = TestGltfModels.Quad();
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        RenderedFrameStats stats = TestGltfModels.Measure(frame);

        //Assert
        stats.Coverage.Should().BeGreaterThan(0.4d);
        frame.GetPixel(0, 0).Alpha.Should().Be(0);
        frame.GetPixel(frame.Width - 1, 0).Alpha.Should().Be(0);
        frame.GetPixel(0, frame.Height - 1).Alpha.Should().Be(0);
        frame.GetPixel(frame.Width - 1, frame.Height - 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void RenderFrame_fits_the_model_into_the_cell_leaving_the_padding_empty()
    {
        //Arrange
        ModelRenderOptions options = FlatOptions() with { FrameSize = new Size(64, 64), FitPadding = 0.1f };
        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.Quad();
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        RenderedFrameStats stats = TestGltfModels.Measure(frame);

        //Assert - a square model in a square cell fills all of it but the 10 per cent padding
        stats.DrawnWidth.Should().BeInRange(48, 54);
        stats.DrawnHeight.Should().BeInRange(48, 54);
        stats.MinX.Should().BeGreaterThan(3);
        stats.MinY.Should().BeGreaterThan(3);
        stats.MaxX.Should().BeLessThan(60);
        stats.MaxY.Should().BeLessThan(60);
    }

    [Fact]
    public void RenderFrame_paints_the_material_base_color_when_the_light_faces_the_surface()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel model = TestGltfModels.Quad(new Vector4(1f, 0f, 0f, 1f));
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        RenderedFrameStats stats = TestGltfModels.Measure(frame);

        //Assert
        stats.AverageRed.Should().BeGreaterThan(230);
        stats.AverageGreen.Should().BeLessThan(10);
        stats.AverageBlue.Should().BeLessThan(10);
    }

    [Fact]
    public void RenderFrame_falls_back_to_the_ambient_term_where_the_light_does_not_reach()
    {
        //Arrange - the light now points away from the surface, so only ambient survives
        ModelRenderOptions options = FlatOptions() with
        {
            LightDirection = new Vector3(0f, 0f, -1f),
            AmbientLight = 0.45f,
        };

        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.Quad(new Vector4(1f, 0f, 0f, 1f));
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        RenderedFrameStats stats = TestGltfModels.Measure(frame);

        //Assert - 45 per cent of full red, and still fully opaque
        stats.AverageRed.Should().BeInRange(100, 130);
        frame.GetPixel(frame.Width / 2, frame.Height / 2).Alpha.Should().Be(255);
    }

    [Fact]
    public void RenderFrame_lets_the_nearer_surface_win_the_depth_test()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel model = TestGltfModels.OverlappingQuads(
            farColor: new Vector4(1f, 0f, 0f, 1f), nearColor: new Vector4(0f, 1f, 0f, 1f));

        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        SKColor centre = frame.GetPixel(frame.Width / 2, frame.Height / 2);

        //Assert - the green quad is nearer the camera, so the overlap shows green
        centre.Green.Should().BeGreaterThan(200);
        centre.Red.Should().BeLessThan(10);
    }

    [Fact]
    public void RenderFrame_culls_a_back_face_unless_the_material_is_double_sided()
    {
        //Arrange - two directions, so direction 1 looks at the back of a quad that faces +Z
        ModelRenderOptions options = FlatOptions() with { Directions = 2 };
        ModelSpriteRenderer renderer = new(options);
        GameModel single = TestGltfModels.Quad();
        GameModel doubleSided = DoubleSided(TestGltfModels.Quad());

        //Act
        using SKBitmap culled = renderer.RenderFrame(single, null, 1, renderer.ComputeFit(single, null));
        using SKBitmap drawn =
            renderer.RenderFrame(doubleSided, null, 1, renderer.ComputeFit(doubleSided, null));

        //Assert
        TestGltfModels.Measure(culled).CoveredPixels.Should().Be(0);
        TestGltfModels.Measure(drawn).Coverage.Should().BeGreaterThan(0.4d);
    }

    [Fact]
    public void RenderFrame_changes_the_silhouette_from_one_direction_to_another()
    {
        //Arrange
        ModelRenderOptions options = FlatOptions() with { Directions = 8, FrameSize = new Size(48, 48) };
        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.SymmetricWedge();
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap front = renderer.RenderFrame(model, null, 0, fit);
        using SKBitmap side = renderer.RenderFrame(model, null, 2, fit);
        RenderedFrameStats frontStats = TestGltfModels.Measure(front);
        RenderedFrameStats sideStats = TestGltfModels.Measure(side);

        //Assert - seen from the side the wedge is one slab deep, not two slabs wide
        frontStats.CoveredPixels.Should().BeGreaterThan(0);
        sideStats.CoveredPixels.Should().BeGreaterThan(0);
        sideStats.CoveredPixels.Should().BeLessThan((int)(frontStats.CoveredPixels * 0.8d));
    }

    [Fact]
    public void RenderFrame_mirrors_the_two_opposite_directions_of_a_symmetric_model()
    {
        //Arrange
        ModelRenderOptions options = FlatOptions() with { Directions = 8, FrameSize = new Size(48, 48) };
        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.SymmetricWedge();
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap front = renderer.RenderFrame(model, null, 0, fit);
        using SKBitmap back = renderer.RenderFrame(model, null, 4, fit);

        //Assert - the model is mirror-symmetric front to back and the camera's right vector flips,
        //  so the two silhouettes are horizontal mirrors of each other
        TestGltfModels.MirroredCoverageAgreement(front, back).Should().BeGreaterThan(0.97d);
    }

    [Fact]
    public void ComputeFit_shares_one_scale_with_every_frame_of_every_clip()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel model = TestGltfModels.Quad();
        GameModelAnimationClip clip = TestGltfModels.GrowingClip(model, frameCount: 4, maximumScale: 2f);

        //Act
        ModelCameraFit restOnly = renderer.ComputeFit(model, null);
        ModelCameraFit withClip = renderer.ComputeFit(model, [clip]);

        //Assert - the widest pose of the clip is twice the rest pose, so the shared scale halves
        withClip.Scale.Should().BeApproximately(restOnly.Scale * 0.5f, 0.02f);
    }

    [Fact]
    public void RenderFrame_keeps_every_frame_of_a_growing_clip_inside_the_cell()
    {
        //Arrange
        ModelRenderOptions options = FlatOptions() with
        {
            FrameSize = new Size(64, 64),
            FitPadding = 0.06f,
        };

        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.Quad();
        GameModelAnimationClip clip = TestGltfModels.GrowingClip(model, frameCount: 4, maximumScale: 2f);
        ModelCameraFit fit = renderer.ComputeFit(model, [clip]);

        //Act
        using SKBitmap firstFrame = renderer.RenderFrame(model, clip.Frames[0], 0, fit);
        using SKBitmap lastFrame = renderer.RenderFrame(model, clip.Frames[^1], 0, fit);
        RenderedFrameStats first = TestGltfModels.Measure(firstFrame);
        RenderedFrameStats last = TestGltfModels.Measure(lastFrame);

        //Assert - the last pose is twice the first and still does not touch the cell edges
        first.DrawnWidth.Should().BeGreaterThan(0);
        last.DrawnWidth.Should().BeInRange((int)(first.DrawnWidth * 1.8d), (int)(first.DrawnWidth * 2.2d));
        last.MinX.Should().BeGreaterThan(0);
        last.MaxX.Should().BeLessThan(63);
        last.MinY.Should().BeGreaterThan(0);
        last.MaxY.Should().BeLessThan(63);
    }

    [Fact]
    public void RenderFrame_discards_a_masked_sample_below_the_cutoff_and_keeps_one_above_it()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel below = TestGltfModels.Quad(
            new Vector4(1f, 1f, 1f, 0.2f), GameModelAlphaMode.Mask, alphaCutoff: 0.5f);
        GameModel above = TestGltfModels.Quad(
            new Vector4(1f, 1f, 1f, 0.2f), GameModelAlphaMode.Mask, alphaCutoff: 0.1f);

        //Act
        using SKBitmap discarded = renderer.RenderFrame(below, null, 0, renderer.ComputeFit(below, null));
        using SKBitmap kept = renderer.RenderFrame(above, null, 0, renderer.ComputeFit(above, null));

        //Assert - a masked sample is either fully there or not there at all
        TestGltfModels.Measure(discarded).CoveredPixels.Should().Be(0);
        TestGltfModels.Measure(kept).Coverage.Should().BeGreaterThan(0.4d);
        kept.GetPixel(kept.Width / 2, kept.Height / 2).Alpha.Should().Be(255);
    }

    [Fact]
    public void RenderFrame_blends_a_translucent_surface_over_what_is_behind_it()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel model = TestGltfModels.OverlappingQuads(
            farColor: new Vector4(1f, 0f, 0f, 1f), nearColor: new Vector4(0f, 0f, 1f, 0.5f));

        GameModel blended = WithNearMaterialBlended(model);
        ModelCameraFit fit = renderer.ComputeFit(blended, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(blended, null, 0, fit);
        SKColor centre = frame.GetPixel(frame.Width / 2, frame.Height / 2);

        //Assert - half the blue surface and half the red one behind it
        centre.Red.Should().BeInRange(80, 180);
        centre.Blue.Should().BeInRange(80, 180);
        centre.Alpha.Should().Be(255);
    }

    [Fact]
    public void RenderFrame_wraps_texture_coordinates_outside_the_unit_square()
    {
        //Arrange - a two-by-two texture whose top-left texel is red and bottom-right texel is white
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel wrapsForward = TexturedQuad(1.25f, 1.25f);
        GameModel wrapsBackward = TexturedQuad(-0.25f, -0.25f);

        //Act
        using SKBitmap forward =
            renderer.RenderFrame(wrapsForward, null, 0, renderer.ComputeFit(wrapsForward, null));
        using SKBitmap backward =
            renderer.RenderFrame(wrapsBackward, null, 0, renderer.ComputeFit(wrapsBackward, null));

        RenderedFrameStats forwardStats = TestGltfModels.Measure(forward);
        RenderedFrameStats backwardStats = TestGltfModels.Measure(backward);

        //Assert - 1.25 wraps to 0.25, which is the red texel; -0.25 wraps to 0.75, the white one
        forwardStats.AverageRed.Should().BeGreaterThan(200);
        forwardStats.AverageGreen.Should().BeLessThan(20);
        backwardStats.AverageRed.Should().BeGreaterThan(200);
        backwardStats.AverageGreen.Should().BeGreaterThan(200);
        backwardStats.AverageBlue.Should().BeGreaterThan(200);
    }

    [Fact]
    public void RenderFrame_antialiases_the_silhouette_when_supersampling()
    {
        //Arrange - a raised camera, so the silhouette has sloping edges rather than the axis-aligned
        //  ones a box seen level-on presents
        ModelRenderOptions options = FlatOptions() with
        {
            Supersample = 4,
            Directions = 8,
            PitchDegrees = 30f,
            FitPadding = 0.06f,
            FrameSize = new Size(48, 48),
        };

        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.SymmetricWedge();
        ModelCameraFit fit = renderer.ComputeFit(model, null);

        //Act
        using SKBitmap frame = renderer.RenderFrame(model, null, 1, fit);
        int partiallyCovered = 0;

        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                byte alpha = frame.GetPixel(x, y).Alpha;
                if (alpha is > 16 and < 240) { partiallyCovered++; }
            }
        }

        //Assert
        renderer.Supersample.Should().Be(4);
        partiallyCovered.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderFrame_also_fits_the_model_under_a_perspective_camera()
    {
        //Arrange
        ModelRenderOptions options = FlatOptions() with
        {
            Projection = ModelProjection.Perspective,
            FieldOfViewDegrees = 35f,
            FrameSize = new Size(64, 64),
        };

        ModelSpriteRenderer renderer = new(options);
        GameModel model = TestGltfModels.SymmetricWedge();

        //Act
        ModelCameraFit fit = renderer.ComputeFit(model, null);
        using SKBitmap frame = renderer.RenderFrame(model, null, 0, fit);
        RenderedFrameStats stats = TestGltfModels.Measure(frame);

        //Assert
        fit.IsPerspective.Should().BeTrue();
        fit.Distance.Should().BeGreaterThan(fit.Radius);
        stats.CoveredPixels.Should().BeGreaterThan(0);
        stats.MinX.Should().BeGreaterThanOrEqualTo(0);
        stats.MaxX.Should().BeLessThan(64);
        stats.MaxY.Should().BeLessThan(64);
    }

    [Fact]
    public void DirectionYawDegrees_spreads_the_rows_over_a_full_turn()
    {
        //Arrange
        ModelRenderOptions options = new() { Directions = 8, StartYawDegrees = 45f };
        ModelSpriteRenderer renderer = new(options);

        //Act, Assert
        renderer.DirectionYawDegrees(0).Should().BeApproximately(45f, 0.001f);
        renderer.DirectionYawDegrees(2).Should().BeApproximately(135f, 0.001f);
        renderer.DirectionYawDegrees(7).Should().BeApproximately(360f, 0.001f);
    }

    [Fact]
    public void Constructor_rejects_a_frame_size_that_is_not_at_least_one_pixel()
    {
        //Arrange
        ModelRenderOptions options = new() { FrameSize = new Size(0, 32) };

        //Act
        Action act = () => _ = new ModelSpriteRenderer(options);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*FrameSize*");
    }

    [Fact]
    public void Constructor_rejects_a_direction_count_below_one()
    {
        //Arrange
        ModelRenderOptions options = new() { Directions = 0 };

        //Act
        Action act = () => _ = new ModelSpriteRenderer(options);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Directions*");
    }

    [Fact]
    public void Constructor_clamps_a_supersampling_factor_into_the_supported_range()
    {
        //Arrange, Act
        ModelSpriteRenderer none = new(new ModelRenderOptions { Supersample = 0 });
        ModelSpriteRenderer excessive = new(new ModelRenderOptions { Supersample = 64 });

        //Assert
        none.Supersample.Should().Be(1);
        excessive.Supersample.Should().Be(ModelSpriteRenderer.MaxSupersample);
    }

    [Fact]
    public void RenderFrame_produces_an_empty_cell_for_a_model_with_no_geometry()
    {
        //Arrange
        GameModel empty = new()
        {
            Meshes = [],
            Materials = [],
            BoundsMin = Vector3.Zero,
            BoundsMax = Vector3.Zero,
        };

        ModelSpriteRenderer renderer = new(FlatOptions());

        //Act
        ModelCameraFit fit = renderer.ComputeFit(empty, null);
        using SKBitmap frame = renderer.RenderFrame(empty, null, 0, fit);

        //Assert
        TestGltfModels.Measure(frame).CoveredPixels.Should().Be(0);
    }

    [Fact]
    public void RenderFrame_rejects_a_frame_whose_mesh_count_does_not_match_the_model()
    {
        //Arrange
        ModelSpriteRenderer renderer = new(FlatOptions());
        GameModel model = TestGltfModels.Quad();
        ModelCameraFit fit = renderer.ComputeFit(model, null);
        GameModelAnimationFrame mismatched = new() { Meshes = [] };

        //Act
        Action act = () => renderer.RenderFrame(model, mismatched, 0, fit).Dispose();

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    //A head-on, unlit-from-behind, unsupersampled camera, which keeps the geometry assertions crisp
    private static ModelRenderOptions FlatOptions() => new()
    {
        FrameSize = new Size(48, 48),
        Directions = 1,
        PitchDegrees = 0f,
        Supersample = 1,
        FitPadding = 0f,
        AmbientLight = 0f,
        LightDirection = FrontLight,
    };

    private static GameModel DoubleSided(GameModel model) => new()
    {
        Name = model.Name,
        Meshes = model.Meshes,
        Materials =
        [
            new GameModelMaterial
            {
                Name = model.Materials[0].Name,
                BaseColorFactor = model.Materials[0].BaseColorFactor,
                AlphaMode = model.Materials[0].AlphaMode,
                DoubleSided = true,
            },
        ],
        BoundsMin = model.BoundsMin,
        BoundsMax = model.BoundsMax,
        Pivot = model.Pivot,
    };

    private static GameModel WithNearMaterialBlended(GameModel model) => new()
    {
        Name = model.Name,
        Meshes = model.Meshes,
        Materials =
        [
            model.Materials[0],
            new GameModelMaterial
            {
                Name = model.Materials[1].Name,
                BaseColorFactor = model.Materials[1].BaseColorFactor,
                AlphaMode = GameModelAlphaMode.Blend,
            },
        ],
        BoundsMin = model.BoundsMin,
        BoundsMax = model.BoundsMax,
        Pivot = model.Pivot,
    };

    private static GameModel TexturedQuad(float u, float v)
    {
        GameModel quad = TestGltfModels.Quad(Vector4.One);
        float[] texCoords = [u, v, u, v, u, v, u, v];

        //Two by two: red, green on the top row; blue, white on the bottom
        byte[] texture =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 255, 255,
        ];

        return new GameModel
        {
            Name = "textured",
            Meshes =
            [
                new GameModelMesh
                {
                    Positions = quad.Meshes[0].Positions,
                    Normals = quad.Meshes[0].Normals,
                    TexCoords = texCoords,
                    Indices = quad.Meshes[0].Indices,
                    MaterialIndex = 0,
                },
            ],
            Materials =
            [
                new GameModelMaterial
                {
                    Name = "atlas",
                    BaseColorFactor = Vector4.One,
                    BaseColorTextureRgba = texture,
                    BaseColorTextureWidth = 2,
                    BaseColorTextureHeight = 2,
                },
            ],
            BoundsMin = quad.BoundsMin,
            BoundsMax = quad.BoundsMax,
            Pivot = quad.Pivot,
        };
    }
}
