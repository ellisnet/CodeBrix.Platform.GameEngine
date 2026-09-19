using System.Numerics;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the engine-native model data type: its computed bounds and counts, the never-null
/// animation collections, and the lookup of a baked clip by name.
/// </summary>
public class GameModelTests
{
    [Fact]
    public void Defaults_leave_the_optional_members_empty()
    {
        //Arrange & Act
        var model = TestGameModels.Model(name: null);

        //Assert
        model.Name.Should().BeNull();
        model.Pivot.Should().BeNull();
        model.Materials.Should().BeEmpty();
        model.AnimationNames.Should().NotBeNull();
        model.AnimationNames.Should().BeEmpty();
        model.Animations.Should().NotBeNull();
        model.Animations.Should().BeEmpty();
    }

    [Fact]
    public void BoundsCenter_and_BoundsRadius_describe_the_bounding_box()
    {
        //Arrange & Act
        var model = TestGameModels.Model(
            boundsMin: new Vector3(-2.0f, 0.0f, -2.0f),
            boundsMax: new Vector3(2.0f, 4.0f, 2.0f));

        //Assert
        model.BoundsCenter.Should().Be(new Vector3(0.0f, 2.0f, 0.0f));
        model.BoundsRadius.Should().BeApproximately(3.4641f, 0.001f);
    }

    [Fact]
    public void TriangleCount_and_VertexCount_sum_every_mesh()
    {
        //Arrange
        var meshes = new[] { TestGameModels.Mesh(6), TestGameModels.Mesh(3) };

        //Act
        var model = TestGameModels.Model(meshes);

        //Assert
        model.VertexCount.Should().Be(9);
        model.TriangleCount.Should().Be(3);
    }

    [Fact]
    public void AnimationNames_and_Animations_are_never_null()
    {
        //Arrange & Act
        var model = TestGameModels.Model(animationNames: null, animations: null);

        //Assert
        model.AnimationNames.Should().BeEmpty();
        model.Animations.Should().BeEmpty();
    }

    [Fact]
    public void AnimationNames_lists_every_clip_the_asset_offers_baked_or_not()
    {
        //Arrange
        var baked = TestGameModels.Clip("walk");

        //Act
        var model = TestGameModels.Model(animationNames: ["walk", "idle"], animations: [baked]);

        //Assert
        model.AnimationNames.Count.Should().Be(2);
        model.Animations.Should().ContainSingle();
        model.Animations[0].Should().BeSameAs(baked);
    }

    [Fact]
    public void TryGetAnimation_finds_a_baked_clip_ignoring_case()
    {
        //Arrange
        var baked = TestGameModels.Clip("Walk");
        var model = TestGameModels.Model(animationNames: ["Walk"], animations: [baked]);

        //Act
        bool found = model.TryGetAnimation("wALK", out var clip);

        //Assert
        found.Should().BeTrue();
        clip.Should().BeSameAs(baked);
    }

    [Fact]
    public void TryGetAnimation_returns_false_for_a_clip_that_was_not_baked()
    {
        //Arrange - 'idle' is offered by the asset but baking was not asked for.
        var model = TestGameModels.Model(
            animationNames: ["walk", "idle"],
            animations: [TestGameModels.Clip("walk")]);

        //Act
        bool foundIdle = model.TryGetAnimation("idle", out var idle);
        bool foundUnknown = model.TryGetAnimation("swim", out var unknown);
        bool foundBlank = model.TryGetAnimation("  ", out var blank);

        //Assert
        foundIdle.Should().BeFalse();
        idle.Should().BeNull();
        foundUnknown.Should().BeFalse();
        unknown.Should().BeNull();
        foundBlank.Should().BeFalse();
        blank.Should().BeNull();
    }

    [Fact]
    public void Pivot_is_carried_through_when_the_provider_supplies_one()
    {
        //Arrange & Act
        var model = TestGameModels.Model(pivot: new Vector3(0.0f, 0.5f, 0.0f));

        //Assert
        model.Pivot.Should().Be(new Vector3(0.0f, 0.5f, 0.0f));
    }
}
