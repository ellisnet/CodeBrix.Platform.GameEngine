using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Models;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the grid a model sprite sheet is laid out on, which is the output layout contract of
/// <see cref="ModelRenderOptions"/>: one region per animation plus the rest pose, columns of frames
/// by rows of directions, and the size guard that refuses a sheet no graphics device could hold.
/// </summary>
public class ModelSheetLayoutTests
{
    [Fact]
    public void Create_gives_the_rest_pose_one_column_and_a_row_per_direction()
    {
        //Arrange
        ModelRenderOptions options = new() { FrameSize = new Size(32, 32), Directions = 4 };

        //Act
        ModelSheetLayout layout = ModelSheetLayout.Create(options, []);

        //Assert
        layout.Regions.Count.Should().Be(1);
        layout.Regions[0].Name.Should().Be(ModelRenderOptions.RestPoseRegionName);
        layout.Regions[0].Columns.Should().Be(1);
        layout.Regions[0].Rows.Should().Be(4);
        layout.Regions[0].Clip.Should().BeNull();
        layout.Regions[0].Area.Should().Be(new Rectangle(0, 0, 32, 128));
        layout.Width.Should().Be(32);
        layout.Height.Should().Be(128);
    }

    [Fact]
    public void Create_gives_each_animation_a_column_per_frame_and_stacks_the_regions()
    {
        //Arrange
        ModelRenderOptions options = new() { FrameSize = new Size(16, 16), Directions = 2 };
        GameModelAnimationClip walk = Clip("walk", 5);
        GameModelAnimationClip idle = Clip("idle", 3);

        //Act
        ModelSheetLayout layout = ModelSheetLayout.Create(options, [walk, idle]);

        //Assert - rest first, then the animations in the order they were asked for
        layout.Regions.Select(region => region.Name)
            .Should().BeEquivalentTo([ModelRenderOptions.RestPoseRegionName, "walk", "idle"]);
        layout.Regions[1].Columns.Should().Be(5);
        layout.Regions[1].Area.Should().Be(new Rectangle(0, 32, 80, 32));
        layout.Regions[2].Columns.Should().Be(3);
        layout.Regions[2].Area.Should().Be(new Rectangle(0, 64, 48, 32));
        layout.Width.Should().Be(80);
        layout.Height.Should().Be(96);
    }

    [Fact]
    public void Create_leaves_the_rest_pose_out_when_it_was_not_asked_for()
    {
        //Arrange
        ModelRenderOptions options = new()
        {
            FrameSize = new Size(16, 16),
            Directions = 2,
            IncludeRestPose = false,
        };

        //Act
        ModelSheetLayout layout = ModelSheetLayout.Create(options, [Clip("walk", 4)]);

        //Assert
        layout.Regions.Count.Should().Be(1);
        layout.Regions[0].Name.Should().Be("walk");
        layout.Height.Should().Be(32);
    }

    [Fact]
    public void Create_refuses_a_sheet_that_would_exceed_the_dimension_limit()
    {
        //Arrange - eight directions of 1024 pixel cells already fill the limit vertically
        ModelRenderOptions options = new() { FrameSize = new Size(1024, 1024), Directions = 8 };

        //Act
        Action act = () => ModelSheetLayout.Create(options, [Clip("walk", 4)]);

        //Assert - and the message says what to reduce
        ArgumentException failure = act.Should().Throw<ArgumentException>().Which;
        failure.Message.Should().Contain(ModelSheetLayout.MaxDimension.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        failure.Message.Should().Contain("RegisterAs");
        failure.Message.Should().Contain("AnimationFramesPerSecond");
        failure.Message.Should().Contain("FrameSize");
    }

    [Fact]
    public void Create_refuses_two_regions_with_the_same_name()
    {
        //Arrange - an animation actually named after the rest-pose region
        ModelRenderOptions options = new() { FrameSize = new Size(8, 8), Directions = 1 };

        //Act
        Action act = () =>
            ModelSheetLayout.Create(options, [Clip(ModelRenderOptions.RestPoseRegionName, 2)]);

        //Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{ModelRenderOptions.RestPoseRegionName}*");
    }

    [Fact]
    public void Create_refuses_a_sheet_with_nothing_on_it()
    {
        //Arrange
        ModelRenderOptions options = new() { IncludeRestPose = false };

        //Act
        Action act = () => ModelSheetLayout.Create(options, []);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*nothing to render*");
    }

    [Fact]
    public void Create_refuses_a_frame_size_below_one_pixel()
    {
        //Arrange
        ModelRenderOptions options = new() { FrameSize = new Size(32, 0) };

        //Act
        Action act = () => ModelSheetLayout.Create(options, []);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*FrameSize*");
    }

    [Fact]
    public void Create_refuses_a_direction_count_below_one()
    {
        //Arrange
        ModelRenderOptions options = new() { Directions = 0 };

        //Act
        Action act = () => ModelSheetLayout.Create(options, []);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Directions*");
    }

    private static GameModelAnimationClip Clip(string name, int frameCount)
    {
        List<GameModelAnimationFrame> frames = new(frameCount);

        for (int i = 0; i < frameCount; i++)
        {
            frames.Add(new GameModelAnimationFrame
            {
                Meshes = [new GameModelFrameMesh { Positions = new float[3], Normals = new float[3] }],
            });
        }

        return new GameModelAnimationClip
        {
            Name = name,
            Duration = frameCount / 12f,
            FrameRate = 12,
            Frames = frames,
        };
    }
}
