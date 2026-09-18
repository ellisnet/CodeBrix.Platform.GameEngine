using System;
using System.Reflection;
using System.Threading;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;
using EngineTimer = CodeBrix.Platform.GameEngine.Timers.Timer;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the engine <see cref="EngineTimer"/> registry: registration, removal, disposal, and the
/// tick-raising loop (including the length validation and the single-shot catch-up guard).
/// </summary>
public class TimerTests : IDisposable
{
    /// <summary>
    /// Puts the process-global timer registry into a known state before each test.
    /// </summary>
    public TimerTests()
    {
        EngineTimer.ClearAll();
        EngineTimer.PausedAll = false;
    }

    /// <summary>
    /// Restores the process-global timer registry after each test.
    /// </summary>
    public void Dispose()
    {
        EngineTimer.ClearAll();
        EngineTimer.PausedAll = false;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Add_with_explicit_id_registers_the_timer_for_lookup()
    {
        //Arrange + Act
        var timer = EngineTimer.Add("explicit", TimerType.PreCycle, TimerCycles.Repeating, 0.01);

        //Assert
        timer.TimerID.Should().Be("explicit");
        EngineTimer.Get("explicit").Should().BeSameAs(timer);
        timer.Length.Should().BeGreaterThan(0);
        EngineTimer.Count.Should().Be(1);
    }

    [Fact]
    public void Add_without_an_id_generates_and_registers_one()
    {
        //Arrange + Act
        var timer = EngineTimer.Add(TimerType.PostCycle, TimerCycles.Once, 0.01);

        //Assert
        string.IsNullOrWhiteSpace(timer.TimerID).Should().BeFalse();
        EngineTimer.TimerIDs.Should().Contain(timer.TimerID);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void Add_with_an_invalid_length_throws(double length)
    {
        //Arrange
        Action act = () => EngineTimer.Add("invalid", TimerType.PreCycle, TimerCycles.Repeating, length);

        //Act + Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        EngineTimer.Count.Should().Be(0);
    }

    [Fact]
    public void Add_with_a_sub_tick_length_throws()
    {
        //Arrange
        double length = 0.5 / HighResTimer.TicksPerSecond;
        Action act = () => EngineTimer.Add("sub-tick", TimerType.PreCycle, TimerCycles.Repeating, length);

        //Act + Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        EngineTimer.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_is_safe_for_existing_and_missing_ids()
    {
        //Arrange
        EngineTimer.Add("to-remove", TimerType.PreCycle, TimerCycles.Repeating, 0.01);

        //Act
        EngineTimer.Remove("to-remove");
        EngineTimer.Remove("missing");

        //Assert
        EngineTimer.Count.Should().Be(0);
    }

    [Fact]
    public void ClearAll_removes_every_registered_timer()
    {
        //Arrange
        EngineTimer.Add("a", TimerType.PreCycle, TimerCycles.Repeating, 0.01);
        EngineTimer.Add("b", TimerType.PostCycle, TimerCycles.Once, 0.01);

        //Act
        EngineTimer.ClearAll();

        //Assert
        EngineTimer.Count.Should().Be(0);
        EngineTimer.TimerIDs.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_removes_the_timer_and_is_idempotent()
    {
        //Arrange
        var timer = EngineTimer.Add("disposable", TimerType.PreCycle, TimerCycles.Repeating, 0.01);

        //Act
        timer.Dispose();
        timer.Dispose();

        //Assert
        EngineTimer.Count.Should().Be(0);
        EngineTimer.TimerIDs.Should().NotContain("disposable");
    }

    [Fact]
    public void RaiseTimerEvents_invokes_the_tick_handler_for_a_matching_type()
    {
        //Arrange
        var timer = EngineTimer.Add("pre", TimerType.PreCycle, TimerCycles.Repeating, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, HighResTimer.GetCurrentTick() + timer.Length);

        //Assert
        ticks.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void RaiseTimerEvents_does_not_invoke_a_timer_of_a_different_type()
    {
        //Arrange
        var timer = EngineTimer.Add("post", TimerType.PostCycle, TimerCycles.Repeating, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, HighResTimer.GetCurrentTick() + timer.Length);

        //Assert
        ticks.Should().Be(0);
    }

    [Fact]
    public void RaiseTimerEvents_removes_a_once_timer_after_its_tick()
    {
        //Arrange
        var timer = EngineTimer.Add("once", TimerType.PreCycle, TimerCycles.Once, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, GetLastEventTick(timer) + timer.Length);

        //Assert
        ticks.Should().Be(1);
        EngineTimer.TimerIDs.Should().NotContain("once");
    }

    [Fact]
    public void RaiseTimerEvents_raises_an_overdue_once_timer_exactly_one_time()
    {
        //Arrange
        var timer = EngineTimer.Add("overdue-once", TimerType.PreCycle, TimerCycles.Once, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, GetLastEventTick(timer) + (timer.Length * 3));

        //Assert
        ticks.Should().Be(1);
        EngineTimer.TimerIDs.Should().NotContain("overdue-once");
    }

    [Fact]
    public void RaiseTimerEvents_does_not_tick_while_PausedAll_is_set()
    {
        //Arrange
        var timer = EngineTimer.Add("paused", TimerType.PreCycle, TimerCycles.Repeating, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;
        EngineTimer.PausedAll = true;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, HighResTimer.GetCurrentTick() + (timer.Length * 3));

        //Assert
        ticks.Should().Be(0);
    }

    [Fact]
    public void RaiseTimerEvents_lets_a_repeating_timer_catch_up_on_missed_intervals()
    {
        //Arrange
        var timer = EngineTimer.Add("repeat", TimerType.PreCycle, TimerCycles.Repeating, 0.01);
        int ticks = 0;
        timer.Tick += () => ticks++;

        //Act
        EngineTimer.RaiseTimerEvents(TimerType.PreCycle, HighResTimer.GetCurrentTick() + (timer.Length * 3));

        //Assert
        ticks.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Add_starts_a_PreCycle_timer_on_simulation_time_in_timer_driven_mode()
    {
        //Arrange - one 1/120 s simulation step per Tick(), so the simulation clock falls far
        //behind wall time when a tick is late. The configuration object is read AFTER the start
        //call, because the first start initializes the engine and replaces it.
        int preCycleTicks = 0;
        int postCycleTicks = 0;
        Engine.Instance.StartTimerDriven(new SynchronizationContext());

        var configuration = Engine.Instance.Configuration;
        int originalRate = configuration.TimerDrivenSimulationRate;
        int originalMaxSteps = configuration.MaxTimerDrivenSimulationSteps;
        double originalSampling = configuration.SamplingTimeForCPS;
        configuration.TimerDrivenSimulationRate = 120;
        configuration.MaxTimerDrivenSimulationSteps = 1;
        configuration.SamplingTimeForCPS = 0;

        try
        {
            var preCycle = EngineTimer.Add("sim-pre", TimerType.PreCycle, TimerCycles.Repeating, 0.05);
            preCycle.Tick += () => preCycleTicks++;

            var postCycle = EngineTimer.Add("sim-post", TimerType.PostCycle, TimerCycles.Repeating, 0.05);
            postCycle.Tick += () => postCycleTicks++;

            //Act - 200 ms of wall time, but the step cap advances the simulation by ~8 ms
            Thread.Sleep(200);
            Engine.Instance.Tick();

            //Assert - the pre-cycle timer is measured against simulation time and has not elapsed,
            //while the post-cycle timer runs on the presentation (wall) clock and has
            preCycleTicks.Should().Be(0);
            postCycleTicks.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Engine.Instance.IsRunning)
                Engine.Instance.Stop();

            configuration.TimerDrivenSimulationRate = originalRate;
            configuration.MaxTimerDrivenSimulationSteps = originalMaxSteps;
            configuration.SamplingTimeForCPS = originalSampling;
        }
    }

    private static long GetLastEventTick(EngineTimer timer)
    {
        var property = typeof(EngineTimer).GetProperty("_lastEventTick",
                           BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? throw new InvalidOperationException("Could not find Timer._lastEventTick via reflection.");
        return (long)(property.GetValue(timer) ?? throw new InvalidOperationException("Timer._lastEventTick is null."));
    }
}
