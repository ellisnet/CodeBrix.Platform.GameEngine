using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers a baked animation clip: the frame count, the time-to-frame mapping that keeps playback
/// timing with the consumer, and the alignment check a clip offers against a model.
/// </summary>
public class GameModelAnimationClipTests
{
    [Fact]
    public void FrameCount_counts_the_baked_frames()
    {
        //Arrange & Act
        var clip = TestGameModels.Clip();

        //Assert
        clip.FrameCount.Should().Be(4);
        clip.Frames.Count.Should().Be(clip.FrameCount);
    }

    [Fact]
    public void GetFrameIndex_maps_a_time_within_the_clip_to_its_frame()
    {
        //Arrange - four frames over one second, so each frame covers a quarter of a second.
        var clip = TestGameModels.Clip(duration: 1.0f, frameRate: 4);

        //Act & Assert
        clip.GetFrameIndex(0.0).Should().Be(0);
        clip.GetFrameIndex(0.2).Should().Be(0);
        clip.GetFrameIndex(0.25).Should().Be(1);
        clip.GetFrameIndex(0.6).Should().Be(2);
        clip.GetFrameIndex(0.99).Should().Be(3);
    }

    [Fact]
    public void GetFrameIndex_wraps_a_looping_clip_so_it_never_repeats_its_end_pose()
    {
        //Arrange
        var clip = TestGameModels.Clip(duration: 1.0f, frameRate: 4);

        //Act & Assert
        clip.GetFrameIndex(1.0).Should().Be(0);
        clip.GetFrameIndex(1.3).Should().Be(1);
        clip.GetFrameIndex(9.75).Should().Be(3);
        clip.GetFrameIndex(-0.25).Should().Be(3);
        clip.GetFrameIndex(-1.0).Should().Be(0);
    }

    [Fact]
    public void GetFrameIndex_clamps_when_the_clip_does_not_loop()
    {
        //Arrange
        var clip = TestGameModels.Clip(duration: 1.0f, frameRate: 4);

        //Act & Assert
        clip.GetFrameIndex(1.0, loop: false).Should().Be(3);
        clip.GetFrameIndex(45.0, loop: false).Should().Be(3);
        clip.GetFrameIndex(-5.0, loop: false).Should().Be(0);
        clip.GetFrameIndex(0.5, loop: false).Should().Be(2);
    }

    [Fact]
    public void GetFrameIndex_answers_zero_for_a_clip_that_cannot_be_played()
    {
        //Arrange
        var empty = TestGameModels.Clip(frames: []);
        var instant = TestGameModels.Clip(duration: 0.0f);

        //Act & Assert
        empty.GetFrameIndex(0.5).Should().Be(0);
        instant.GetFrameIndex(0.5).Should().Be(0);
        instant.GetFrameIndex(0.5, loop: false).Should().Be(0);
    }

    [Fact]
    public void IsCompatibleWith_accepts_a_clip_baked_for_the_model()
    {
        //Arrange
        var model = TestGameModels.Model([TestGameModels.Mesh(6), TestGameModels.Mesh(3)]);
        var clip = TestGameModels.ClipFor(model);

        //Act
        bool compatible = clip.IsCompatibleWith(model);

        //Assert
        compatible.Should().BeTrue();
    }

    [Fact]
    public void IsCompatibleWith_rejects_a_clip_with_the_wrong_mesh_count()
    {
        //Arrange
        var model = TestGameModels.Model([TestGameModels.Mesh(3), TestGameModels.Mesh(3)]);
        var clip = TestGameModels.Clip(frames: [TestGameModels.Frame(3)]);

        //Act
        bool compatible = clip.IsCompatibleWith(model);

        //Assert
        compatible.Should().BeFalse();
    }

    [Fact]
    public void IsCompatibleWith_rejects_a_clip_with_the_wrong_vertex_count()
    {
        //Arrange
        var model = TestGameModels.Model([TestGameModels.Mesh(6)]);
        var clip = TestGameModels.Clip(frames: [TestGameModels.Frame(6), TestGameModels.Frame(3)]);

        //Act
        bool compatible = clip.IsCompatibleWith(model);

        //Assert
        compatible.Should().BeFalse();
    }

    [Fact]
    public void IsCompatibleWith_rejects_a_clip_with_no_frames()
    {
        //Arrange
        var model = TestGameModels.Model();
        var clip = TestGameModels.Clip(frames: []);

        //Act
        bool compatible = clip.IsCompatibleWith(model);

        //Assert
        compatible.Should().BeFalse();
    }

    [Fact]
    public void IsCompatibleWith_rejects_a_null_model()
    {
        //Arrange
        var clip = TestGameModels.Clip();

        //Act
        Action act = () => clip.IsCompatibleWith(null!);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
