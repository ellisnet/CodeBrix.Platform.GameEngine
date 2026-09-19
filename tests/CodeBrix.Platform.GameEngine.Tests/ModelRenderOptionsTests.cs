using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the options a provider pre-renders a model into sprite frames with: the defaults a
/// caller inherits by passing nothing, the rest-pose region name that is part of the sheet layout
/// contract, and the record semantics callers rely on.
/// </summary>
public class ModelRenderOptionsTests
{
    [Fact]
    public void Defaults_produce_an_eight_direction_sheet_of_the_rest_pose()
    {
        //Arrange & Act
        var options = new ModelRenderOptions();

        //Assert
        options.FrameSize.Should().Be(new Size(128, 128));
        options.Directions.Should().Be(8);
        options.StartYawDegrees.Should().Be(0.0f);
        options.PitchDegrees.Should().Be(30.0f);
        options.Projection.Should().Be(ModelProjection.Orthographic);
        options.FieldOfViewDegrees.Should().Be(35.0f);
        options.AnimationNames.Should().BeNull();
        options.IncludeRestPose.Should().BeTrue();
        options.AnimationFramesPerSecond.Should().Be(12);
        options.Supersample.Should().Be(2);
        options.AmbientLight.Should().BeApproximately(0.45f, 0.0001f);
        options.FitPadding.Should().BeApproximately(0.06f, 0.0001f);
    }

    [Fact]
    public void The_default_light_comes_normalized_from_above_in_front_and_to_the_left()
    {
        //Arrange & Act
        var light = new ModelRenderOptions().LightDirection;

        //Assert
        light.Length().Should().BeApproximately(1.0f, 0.0001f);
        light.X.Should().BeLessThan(0.0f);
        light.Y.Should().BeGreaterThan(0.0f);
        light.Z.Should().BeGreaterThan(0.0f);
    }

    [Fact]
    public void RestPoseRegionName_is_the_name_of_the_rest_pose_region()
    {
        //Arrange & Act & Assert
        ModelRenderOptions.RestPoseRegionName.Should().Be("rest");
    }

    [Fact]
    public void An_altered_copy_keeps_the_untouched_members()
    {
        //Arrange
        var options = new ModelRenderOptions { Directions = 16, AnimationNames = ["walk"] };

        //Act
        var altered = options with { Supersample = 1 };

        //Assert
        altered.Directions.Should().Be(16);
        altered.AnimationNames.Should().ContainSingle();
        altered.Supersample.Should().Be(1);
        options.Supersample.Should().Be(2);
    }

    [Fact]
    public void Two_options_with_the_same_values_are_equal()
    {
        //Arrange & Act
        var first = new ModelRenderOptions { Directions = 4, PitchDegrees = 45.0f };
        var second = new ModelRenderOptions { Directions = 4, PitchDegrees = 45.0f };

        //Assert
        first.Should().Be(second);
    }
}
