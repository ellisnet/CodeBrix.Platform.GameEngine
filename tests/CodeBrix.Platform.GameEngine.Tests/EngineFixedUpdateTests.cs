using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the engine's fixed-step update hook (<see cref="Engine.FixedUpdate"/>,
/// <see cref="Engine.AfterFixedUpdates"/>, <see cref="Configuration.EngineConfiguration.FixedUpdateRate"/>):
/// off by default, the configured rate in both loop modes, the per-cycle cap, frozen across the global
/// pause, and a handler's pause ending the cycle's remaining steps.
/// </summary>
public class EngineFixedUpdateTests
{
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("The expected condition was not reached in time.");
            Thread.Sleep(5);
        }
    }

    private static void ResetEngine()
    {
        if (Engine.Instance.IsPaused)
            Engine.Instance.Resume();
        if (Engine.Instance.IsRunning)
            Engine.Instance.StopAndWait();

        Engine.Instance.Configuration.FixedUpdateRate = 0;
        Engine.Instance.Configuration.MaxFixedUpdateSteps = 5;
    }

    [Fact]
    public void the_hook_is_off_by_default_and_raises_nothing()
    {
        //Arrange
        long steps = 0;
        long cycles = 0;
        Action<FixedUpdateStep> onStep = _ => Interlocked.Increment(ref steps);
        Action onCycle = () => Interlocked.Increment(ref cycles);
        Engine.Instance.FixedUpdate += onStep;
        Engine.Instance.AfterBackgroundTasksExecute += onCycle;

        try
        {
            //Act
            Engine.Instance.Start(new SynchronizationContext());
            WaitUntil(() => Interlocked.Read(ref cycles) > 1000);
            Thread.Sleep(100);

            //Assert
            Engine.Instance.Configuration.FixedUpdateRate.Should().Be(0);
            Interlocked.Read(ref steps).Should().Be(0);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            Engine.Instance.AfterBackgroundTasksExecute -= onCycle;
            ResetEngine();
        }
    }

    [Fact]
    public void the_engine_loop_raises_numbered_fixed_steps_at_the_configured_rate()
    {
        //Arrange
        var steps = new ConcurrentQueue<FixedUpdateStep>();
        var batches = new ConcurrentQueue<int>();
        Action<FixedUpdateStep> onStep = steps.Enqueue;
        Action<int> onBatch = batches.Enqueue;
        Engine.Instance.FixedUpdate += onStep;
        Engine.Instance.AfterFixedUpdates += onBatch;

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            Engine.Instance.Configuration.FixedUpdateRate = 100;

            //Act
            WaitUntil(() => steps.Count >= 30);
            var stopwatch = Stopwatch.StartNew();
            int atStart = steps.Count;
            Thread.Sleep(500);
            int inHalfSecond = steps.Count - atStart;
            stopwatch.Stop();

            //Assert - about 100 steps a second, each 10 ms long, numbered without gaps
            double expected = stopwatch.Elapsed.TotalSeconds * 100;
            inHalfSecond.Should().BeInRange((int)(expected * 0.6), (int)(expected * 1.4) + 5);

            var snapshot = steps.ToArray();
            snapshot.Should().OnlyContain(step => Math.Abs(step.DeltaSeconds - 0.01) < 0.0005);
            for (int i = 1; i < snapshot.Length; i++)
                (snapshot[i].StepNumber - snapshot[i - 1].StepNumber).Should().Be(1);
            snapshot.Should().OnlyContain(step => step.IndexInCycle < step.StepsInCycle);

            batches.ToArray().Should().OnlyContain(count => count >= 1 && count <= 5);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            Engine.Instance.AfterFixedUpdates -= onBatch;
            ResetEngine();
        }
    }

    [Fact]
    public void a_stalled_cycle_runs_no_more_than_MaxFixedUpdateSteps()
    {
        //Arrange
        var batches = new ConcurrentQueue<int>();
        int stalls = 0;
        long steps = 0;
        Action<FixedUpdateStep> onStep = _ => Interlocked.Increment(ref steps);
        Action<int> onBatch = batches.Enqueue;
        Action stallOnce = () =>
        {
            if (Interlocked.Read(ref steps) > 5 && Interlocked.CompareExchange(ref stalls, 1, 0) == 0)
                Thread.Sleep(300);
        };
        Engine.Instance.FixedUpdate += onStep;
        Engine.Instance.AfterFixedUpdates += onBatch;
        Engine.Instance.AfterBackgroundTasksExecute += stallOnce;

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            Engine.Instance.Configuration.MaxFixedUpdateSteps = 2;
            Engine.Instance.Configuration.FixedUpdateRate = 100;

            //Act - one 300 ms stall owes 30 steps (the flag is set as the stall begins)
            WaitUntil(() => Volatile.Read(ref stalls) == 1);
            Thread.Sleep(500);

            //Assert - the owed steps are capped, never replayed
            var snapshot = batches.ToArray();
            snapshot.Should().OnlyContain(count => count >= 1 && count <= 2);
            snapshot.Should().Contain(2);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            Engine.Instance.AfterFixedUpdates -= onBatch;
            Engine.Instance.AfterBackgroundTasksExecute -= stallOnce;
            ResetEngine();
        }
    }

    [Fact]
    public void steps_are_frozen_across_the_engine_pause_and_not_replayed_after_it()
    {
        //Arrange
        long steps = 0;
        var batchesAfterResume = new ConcurrentQueue<int>();
        int resumed = 0;
        Action<FixedUpdateStep> onStep = _ => Interlocked.Increment(ref steps);
        Action<int> onBatch = count =>
        {
            if (Volatile.Read(ref resumed) == 1)
                batchesAfterResume.Enqueue(count);
        };
        Engine.Instance.FixedUpdate += onStep;
        Engine.Instance.AfterFixedUpdates += onBatch;

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            Engine.Instance.Configuration.MaxFixedUpdateSteps = 50;
            Engine.Instance.Configuration.FixedUpdateRate = 100;
            WaitUntil(() => Interlocked.Read(ref steps) > 10);

            //Act
            Engine.Instance.Pause();
            long atPause = Interlocked.Read(ref steps);
            Thread.Sleep(400);
            long whilePaused = Interlocked.Read(ref steps);
            Volatile.Write(ref resumed, 1);
            Engine.Instance.Resume();
            WaitUntil(() => !batchesAfterResume.IsEmpty);

            //Assert - nothing ran while paused, and the 40 steps the pause spanned were not replayed
            whilePaused.Should().Be(atPause);
            batchesAfterResume.TryPeek(out int first).Should().BeTrue();
            first.Should().BeLessThanOrEqualTo(3);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            Engine.Instance.AfterFixedUpdates -= onBatch;
            ResetEngine();
        }
    }

    [Fact]
    public void a_handler_that_pauses_the_engine_ends_the_cycles_remaining_steps()
    {
        //Arrange
        int paused = 0;
        int batchWhenPaused = -1;
        int stepsDueWhenPaused = -1;
        int stalls = 0;
        long steps = 0;
        Action stallOnce = () =>
        {
            if (Interlocked.Read(ref steps) > 5 && Interlocked.CompareExchange(ref stalls, 1, 0) == 0)
                Thread.Sleep(100);
        };
        Action<FixedUpdateStep> onStep = step =>
        {
            Interlocked.Increment(ref steps);
            if (step.StepsInCycle > 1 && Interlocked.CompareExchange(ref paused, 1, 0) == 0)
            {
                stepsDueWhenPaused = step.StepsInCycle;
                Engine.Instance.Pause();
            }
        };
        Action<int> onBatch = count =>
        {
            if (Volatile.Read(ref paused) == 1 && batchWhenPaused < 0)
                batchWhenPaused = count;
        };
        Engine.Instance.FixedUpdate += onStep;
        Engine.Instance.AfterFixedUpdates += onBatch;
        Engine.Instance.AfterBackgroundTasksExecute += stallOnce;

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            Engine.Instance.Configuration.MaxFixedUpdateSteps = 5;
            Engine.Instance.Configuration.FixedUpdateRate = 100;

            //Act
            WaitUntil(() => Engine.Instance.IsPaused && Volatile.Read(ref batchWhenPaused) >= 0);

            //Assert - several steps were due, but only the one that paused ran
            stepsDueWhenPaused.Should().BeGreaterThan(1);
            batchWhenPaused.Should().Be(1);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            Engine.Instance.AfterFixedUpdates -= onBatch;
            Engine.Instance.AfterBackgroundTasksExecute -= stallOnce;
            ResetEngine();
        }
    }

    [Fact]
    public void timer_driven_mode_raises_fixed_steps_on_the_simulation_clock()
    {
        //Arrange
        long steps = 0;
        Action<FixedUpdateStep> onStep = _ => Interlocked.Increment(ref steps);
        Engine.Instance.FixedUpdate += onStep;

        Engine.Instance.StartTimerDriven(new SynchronizationContext());
        var configuration = Engine.Instance.Configuration;
        double originalSampling = configuration.SamplingTimeForCPS;
        configuration.SamplingTimeForCPS = 0;
        configuration.FixedUpdateRate = 60;

        try
        {
            //Act
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(20);
                Engine.Instance.Tick();
            }
            long beforePause = Interlocked.Read(ref steps);

            Engine.Instance.Pause();
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(20);
                Engine.Instance.Tick();
            }
            long whilePaused = Interlocked.Read(ref steps);
            Engine.Instance.Resume();
            Engine.Instance.Tick();

            //Assert - about 60 a second of simulated time, none while paused, no burst on resume
            beforePause.Should().BeGreaterThan(5);
            whilePaused.Should().Be(beforePause);
            (Interlocked.Read(ref steps) - whilePaused).Should().BeLessThanOrEqualTo(2);
        }
        finally
        {
            Engine.Instance.FixedUpdate -= onStep;
            configuration.SamplingTimeForCPS = originalSampling;
            ResetEngine();
        }
    }

    [Fact]
    public void IsLastInCycle_marks_the_last_step_due() =>
        new FixedUpdateStep(7, 0.01, 0, 2, 3).IsLastInCycle.Should().BeTrue();
}
