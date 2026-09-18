using System;
using CodeBrix.Platform.GameEngine.Configuration;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests timer-driven fixed-step scheduling independently of the engine singleton.
/// </summary>
public class FixedStepAccumulatorTests
{
    private const int UpdateRate = 120;

    [Fact]
    public void Advance_retains_the_fractional_remainder_across_frames()
    {
        //Arrange
        long stepTicks = GetStepTicks();
        const long startTick = 1_000;
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(startTick);

        //Act
        var early = accumulator.Advance(startTick + stepTicks - 1, UpdateRate, maxSteps: 8);
        var first = accumulator.Advance(startTick + stepTicks + 1, UpdateRate, maxSteps: 8);
        var second = accumulator.Advance(startTick + (2 * stepTicks), UpdateRate, maxSteps: 8);

        //Assert
        early.StepCount.Should().Be(0);
        first.StepCount.Should().Be(1);
        first.GetStepTick(0).Should().Be(startTick + stepTicks);
        second.StepCount.Should().Be(1);
        second.GetStepTick(0).Should().Be(startTick + (2 * stepTicks));
    }

    [Fact]
    public void Advance_caps_catch_up_and_discards_excess_backlog()
    {
        //Arrange
        long stepTicks = GetStepTicks();
        const long startTick = 5_000;
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(startTick);

        //Act
        var delayed = accumulator.Advance(startTick + (20 * stepTicks), UpdateRate, maxSteps: 8);
        var next = accumulator.Advance(startTick + (21 * stepTicks), UpdateRate, maxSteps: 8);

        //Assert
        delayed.StepCount.Should().Be(8);
        delayed.GetStepTick(0).Should().Be(startTick + stepTicks);
        delayed.GetStepTick(7).Should().Be(startTick + (8 * stepTicks));
        next.StepCount.Should().Be(1);
        next.GetStepTick(0).Should().Be(startTick + (9 * stepTicks));
    }

    [Fact]
    public void Advance_does_not_rewind_on_a_non_monotonic_driver_tick()
    {
        //Arrange
        const long startTick = 10_000;
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(startTick);

        //Act
        var batch = accumulator.Advance(startTick - 1, UpdateRate, maxSteps: 8);

        //Assert
        batch.StepCount.Should().Be(0);
        accumulator.SimulationTick.Should().Be(startTick);
    }

    [Fact]
    public void Advance_rejects_a_rate_or_step_cap_below_one()
    {
        //Arrange
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(0);

        //Act
        Action zeroRate = () => accumulator.Advance(1_000, updatesPerSecond: 0, maxSteps: 8);
        Action zeroSteps = () => accumulator.Advance(1_000, UpdateRate, maxSteps: 0);

        //Assert
        zeroRate.Should().Throw<ArgumentOutOfRangeException>();
        zeroSteps.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ShiftForResume_keeps_the_pause_out_of_the_next_batch()
    {
        //Arrange
        long stepTicks = GetStepTicks();
        const long startTick = 20_000;
        long pausedTicks = 40 * stepTicks;
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(startTick);

        //Act - the driver clock ran on through a long pause; the simulation clock did not
        accumulator.ShiftForResume(pausedTicks);
        var resumed = accumulator.Advance(startTick + pausedTicks + stepTicks, UpdateRate, maxSteps: 8);

        //Assert
        resumed.StepCount.Should().Be(1);
        resumed.GetStepTick(0).Should().Be(startTick + stepTicks);
        accumulator.SimulationTick.Should().Be(startTick + stepTicks);
    }

    [Fact]
    public void GetStepTick_rejects_an_index_outside_the_batch()
    {
        //Arrange
        long stepTicks = GetStepTicks();
        var accumulator = new FixedStepAccumulator();
        accumulator.Reset(0);
        var batch = accumulator.Advance(stepTicks, UpdateRate, maxSteps: 8);

        //Act
        Action pastTheEnd = () => batch.GetStepTick(batch.StepCount);

        //Assert
        pastTheEnd.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EngineConfiguration_clamps_timer_driven_scheduling_values()
    {
        //Act
        var configuration = new EngineConfiguration
        {
            TimerDrivenSimulationRate = 0,
            MaxTimerDrivenSimulationSteps = -1,
            StartInitializationWaitTimeout = 0,
        };

        //Assert
        configuration.TimerDrivenSimulationRate.Should().Be(1);
        configuration.MaxTimerDrivenSimulationSteps.Should().Be(1);
        configuration.StartInitializationWaitTimeout.Should().Be(0.001f);
    }

    [Fact]
    public void EngineConfiguration_defaults_timer_driven_scheduling_values()
    {
        //Act
        var configuration = new EngineConfiguration();

        //Assert
        configuration.TimerDrivenSimulationRate.Should().Be(120);
        configuration.MaxTimerDrivenSimulationSteps.Should().Be(8);
    }

    private static long GetStepTicks() =>
        Math.Max(1, HighResTimer.TicksPerSecond / UpdateRate);
}
