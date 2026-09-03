using System;
using System.Numerics;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Physics.Movement;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="MovementController"/> teardown behavior: stopping all movement also clears follow
/// state, and disposal detaches the scripted-completion callbacks as well as the two public events.
/// </summary>
public class MovementControllerTests
{
    [Fact]
    public void StopAllMovement_clears_follow_state()
    {
        //Arrange
        var mover = new TestMover();
        var controller = new MovementController(mover, MovementState.ForPixel());
        controller.FollowPixelHard(() => new Vector2(100f, 100f));
        controller.IsFollowing.Should().BeTrue();

        //Act
        controller.StopAllMovement();

        //Assert
        controller.IsFollowing.Should().BeFalse();
        controller.IsScripted.Should().BeFalse();
    }

    [Fact]
    public void Dispose_clears_the_scripted_completion_callbacks()
    {
        //Arrange
        var mover = new TestMover();
        var controller = new MovementController(mover, MovementState.ForPixel());
        controller.MoveTo(new Vector2(50f, 50f), durationSec: 1f)
                  .OnComplete(() => { });
        GetScriptCompleted(controller).Should().NotBeNull();

        //Act
        controller.Dispose();

        //Assert
        GetScriptCompleted(controller).Should().BeNull();
    }

    private static Delegate? GetScriptCompleted(MovementController controller)
    {
        var field = typeof(MovementController).GetField("_scriptCompleted",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException(
                        "Could not find MovementController._scriptCompleted via reflection.");
        return (Delegate?)field.GetValue(controller);
    }

    private sealed class TestMover : IMovable
    {
        private Vector2 _position;

        public MovementSpace PositionSpace => MovementSpace.Pixel;

        public Vector2 GetPosition() => _position;

        public void SetPosition(Vector2 pos) => _position = pos;
    }
}
