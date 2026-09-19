using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the options an <see cref="IModelAssetSource"/> materializes model data with: the
/// defaults, which bake no animation because baking is opt-in, and the record semantics.
/// </summary>
public class ModelMaterializeOptionsTests
{
    [Fact]
    public void Defaults_bake_no_animation()
    {
        //Arrange & Act
        var options = new ModelMaterializeOptions();

        //Assert
        options.AnimationNames.Should().BeNull();
        options.BakeAllAnimations.Should().BeFalse();
        options.AnimationFramesPerSecond.Should().Be(24);
    }

    [Fact]
    public void Named_animations_are_carried_through()
    {
        //Arrange & Act
        var options = new ModelMaterializeOptions
        {
            AnimationNames = ["walk", "idle"],
            AnimationFramesPerSecond = 30
        };

        //Assert
        options.AnimationNames.Should().NotBeNull();
        options.AnimationNames!.Count.Should().Be(2);
        options.AnimationFramesPerSecond.Should().Be(30);
        options.BakeAllAnimations.Should().BeFalse();
    }

    [Fact]
    public void An_altered_copy_keeps_the_untouched_members()
    {
        //Arrange
        var options = new ModelMaterializeOptions { AnimationFramesPerSecond = 12 };

        //Act
        var altered = options with { BakeAllAnimations = true };

        //Assert
        altered.AnimationFramesPerSecond.Should().Be(12);
        altered.BakeAllAnimations.Should().BeTrue();
        options.BakeAllAnimations.Should().BeFalse();
    }

    [Fact]
    public void Two_options_with_the_same_values_are_equal()
    {
        //Arrange & Act
        var first = new ModelMaterializeOptions { BakeAllAnimations = true };
        var second = new ModelMaterializeOptions { BakeAllAnimations = true };

        //Assert
        first.Should().Be(second);
    }
}
