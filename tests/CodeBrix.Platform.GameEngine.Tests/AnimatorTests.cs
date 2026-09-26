using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="Animator"/> frame cycling: a cycle whose throttle is not positive stops instead
/// of spinning the engine thread forever, a cycle that ends with no follow-on cycle does not throw,
/// and a frame that carries its own duration holds for that long instead of the cycle's throttle.
/// </summary>
public class AnimatorTests : IDisposable
{
    private const int HangTimeoutMilliseconds = 5000;

    private readonly Scene _scene = new();

    /// <summary>Clears the process-global registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();
        Tile.TilesAnimating.Clear();
        Cycle.ClearAllAnimationCycles();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private Sprite CreateAnimatingSprite(double throttleTime, string cycleKey)
    {
        var layer = _scene.AddLayer(columnCount: 4, rowCount: 4, width: 16, height: 16, zOrder: 0);
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        sprite.Visible = true;
        var sequence = new FrameSequence(new List<Frame> { default, default, default })
        {
            SequenceCycleType = CycleType.Repeating
        };

        sprite.TileAnimator.CurrentCycle = new Cycle(sequence, throttleTime, cycleKey);
        sprite.TileAnimator.StartAnimation();

        return sprite;
    }

    private static bool CompletesInTime(Action action) =>
        Task.Run(action, TestContext.Current.CancellationToken)
            .Wait(HangTimeoutMilliseconds, TestContext.Current.CancellationToken);

    [Fact]
    public void CycleAnimation_stops_a_cycle_whose_throttle_is_negative()
    {
        //Arrange
        var sprite = CreateAnimatingSprite(throttleTime: -0.1, cycleKey: "negative_throttle");
        var animator = sprite.TileAnimator;

        //Act
        var completed = CompletesInTime(() => animator.CycleAnimation(EngineSimulationClock.GetCurrentTick()));

        //Assert
        completed.Should().BeTrue();
        animator.IsCycling.Should().BeFalse();
    }

    [Fact]
    public void CycleAnimation_stops_when_a_Cycled_handler_makes_the_throttle_negative()
    {
        //Arrange - a positive throttle and a tick far enough ahead for several frame steps.
        var sprite = CreateAnimatingSprite(throttleTime: 0.01, cycleKey: "turns_negative");
        var animator = sprite.TileAnimator;
        animator.Cycled += args => animator.CurrentCycle.ThrottleTime = -0.01;
        var farAhead = EngineSimulationClock.GetCurrentTick() + HighResTimer.TicksPerSecond;

        //Act
        var completed = CompletesInTime(() => animator.CycleAnimation(farAhead));

        //Assert
        completed.Should().BeTrue();
        animator.IsCycling.Should().BeFalse();
    }

    [Fact]
    public void CycleAnimation_still_stops_a_cycle_whose_throttle_is_zero()
    {
        //Arrange
        var sprite = CreateAnimatingSprite(throttleTime: 0, cycleKey: "zero_throttle");
        var animator = sprite.TileAnimator;

        //Act
        animator.CycleAnimation(EngineSimulationClock.GetCurrentTick());

        //Assert
        animator.IsCycling.Should().BeFalse();
    }

    [Fact]
    public void StopAnimation_with_no_next_cycle_does_not_throw()
    {
        //Arrange
        var sprite = CreateAnimatingSprite(throttleTime: 0.1, cycleKey: "no_next_cycle");
        var animator = sprite.TileAnimator;
        animator.CurrentCycle.NextCycle = null!;

        //Act
        var act = () => animator.StopAnimation();

        //Assert
        act.Should().NotThrow();
        animator.IsCycling.Should().BeFalse();
        sprite.Visible.Should().BeTrue();
    }

    [Fact]
    public void StopAnimation_with_no_next_cycle_hides_the_tile_when_the_cycle_asks_for_it()
    {
        //Arrange
        var layer = _scene.AddLayer(columnCount: 4, rowCount: 4, width: 16, height: 16, zOrder: 0);
        var sprite = SpriteManager.Instance.CreateSprite(layer, default);
        var sequence = new FrameSequence(new List<Frame> { default, default });
        var cycle = new Cycle(sequence, 0.1, "hide_at_end", hideTileOnCycleEnd: true) { NextCycle = null! };
        sprite.Visible = true;
        sprite.TileAnimator.CurrentCycle = cycle;
        sprite.TileAnimator.StartAnimation();

        //Act
        sprite.TileAnimator.StopAnimation();

        //Assert
        sprite.Visible.Should().BeFalse();
    }

    [Fact]
    public void CycleAnimation_holds_a_frame_for_its_own_duration()
    {
        //Arrange - a fast cycle whose first frame is timed to hold for a long while.
        var sprite = CreateAnimatingSprite(throttleTime: 0.01, cycleKey: "held_first_frame");
        var animator = sprite.TileAnimator;
        animator.CurrentCycle.Sequence.SetDurationSeconds(0, 60);
        var oneSecondAhead = EngineSimulationClock.GetCurrentTick() + HighResTimer.TicksPerSecond;

        //Act
        animator.CycleAnimation(oneSecondAhead);

        //Assert
        animator.CurrentCycle.Sequence.CurrentFrameIdx.Should().Be(0);
        animator.IsCycling.Should().BeTrue();
    }

    [Fact]
    public void CycleAnimation_moves_on_from_a_short_frame_and_holds_on_a_long_one()
    {
        //Arrange - frame 0 on the fast default, frame 1 held for a long while.
        var sprite = CreateAnimatingSprite(throttleTime: 0.01, cycleKey: "held_second_frame");
        var animator = sprite.TileAnimator;
        animator.CurrentCycle.Sequence.SetDurationSeconds(1, 60);
        var oneSecondAhead = EngineSimulationClock.GetCurrentTick() + HighResTimer.TicksPerSecond;

        //Act
        animator.CycleAnimation(oneSecondAhead);

        //Assert
        animator.CurrentCycle.Sequence.CurrentFrameIdx.Should().Be(1);
        animator.IsCycling.Should().BeTrue();
    }

    [Fact]
    public void CycleAnimation_without_durations_still_advances_on_the_throttle()
    {
        //Arrange - 0.3 s throttle, 3 frames, 1 s later: three advances wrap back to frame 0.
        var sprite = CreateAnimatingSprite(throttleTime: 0.3, cycleKey: "plain_throttle");
        var animator = sprite.TileAnimator;
        var cycledCount = 0;
        animator.Cycled += _ => cycledCount++;
        var oneSecondAhead = EngineSimulationClock.GetCurrentTick() + HighResTimer.TicksPerSecond;

        //Act
        animator.CycleAnimation(oneSecondAhead);

        //Assert
        cycledCount.Should().Be(3);
        animator.CurrentCycle.Sequence.CurrentFrameIdx.Should().Be(0);
    }
}
