using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Headless tests for the <see cref="GameHostBase"/> shutdown sequence. The engine's background
/// cycle is simulated with a task the test completes on demand, so the ordering guarantee
/// (stop and join before any cleanup hook releases native drawing resources) is observable
/// without a live platform head.
/// </summary>
public class GameHostBaseTests
{
    [Fact]
    public async Task Dispose_waits_for_rendering_before_calling_resource_cleanup_hooks()
    {
        //Arrange
        var cycleField = typeof(Engine).GetField("_cycleTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalCycle = cycleField.GetValue(Engine.Instance);
        var cycle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ShutdownHost();

        typeof(GameHostBase).GetField("_engineStarted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, true);
        typeof(GameHostBase).GetField("_engineInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, true);
        cycleField.SetValue(Engine.Instance, cycle.Task);

        //Act
        var disposing = Task.Run(
            () =>
            {
                Action dispose = () => host.Dispose();
                dispose.Should().Throw<CleanupReachedException>();
            },
            TestContext.Current.CancellationToken);

        try
        {
            //Assert
            await host.SchedulingStopped.Task.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Func<Task> disposeBeforeTheCycleEnds =
                () => disposing.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            await disposeBeforeTheCycleEnds.Should().ThrowAsync<TimeoutException>();
            host.CleanupReached.Should().BeFalse();

            cycle.SetResult();

            await disposing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            host.CleanupReached.Should().BeTrue();
        }
        finally
        {
            cycle.TrySetResult();
            await disposing;
            cycleField.SetValue(Engine.Instance, originalCycle);
        }
    }

    [Fact]
    public void the_fixed_update_hooks_run_once_the_game_sets_a_rate_and_stop_after_dispose()
    {
        //Arrange - a timer-driven engine, so the test drives every cycle itself
        var host = new FixedStepHost();
        host.Initialize();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            //Act
            while ((host.Steps < 5 || host.Batches == 0) && stopwatch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(10);
                Engine.Instance.Tick();
            }

            //Assert
            host.Steps.Should().BeGreaterThanOrEqualTo(5);
            host.Batches.Should().BeGreaterThan(0);
            host.LastStep.DeltaSeconds.Should().BeApproximately(0.01, 0.0005);
        }
        finally
        {
            Action dispose = () => host.Dispose();
            dispose.Should().Throw<CleanupReachedException>();
            Engine.Instance.Configuration.FixedUpdateRate = 0;
        }

        //Assert - the hooks were unsubscribed: a restarted engine with the rate back on no longer reaches them
        int stepsAtDispose = host.Steps;
        Engine.Instance.StartTimerDriven(new SynchronizationContext());
        try
        {
            Engine.Instance.Configuration.FixedUpdateRate = 100;
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(10);
                Engine.Instance.Tick();
            }
            host.Steps.Should().Be(stepsAtDispose);
        }
        finally
        {
            Engine.Instance.Configuration.FixedUpdateRate = 0;
            Engine.Instance.StopAndWait();
        }
    }

    private sealed class CleanupReachedException : Exception
    {
    }

    private sealed class ShutdownHost : GameHostBase
    {
        public TaskCompletionSource SchedulingStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CleanupReached { get; private set; }

        protected override void ConfigurePlatform()
        {
        }

        protected override void StopEngineCore() => SchedulingStopped.SetResult();

        protected override void OnDisposing()
        {
            CleanupReached = true;

            // End the probe before disposing the process-wide singleton used by other tests.
            throw new CleanupReachedException();
        }
    }

    private sealed class FixedStepHost : GameHostBase
    {
        public int Steps;
        public int Batches;
        public FixedUpdateStep LastStep;

        protected override void ConfigurePlatform()
        {
        }

        protected override SynchronizationContext? GetSynchronizationContext() => new();

        protected override void StartEngineCore(SynchronizationContext syncContext) =>
            Engine.StartTimerDriven(syncContext);

        protected override void OnEngineInitialized() => Engine.Configuration.FixedUpdateRate = 100;

        protected override void OnFixedUpdate(FixedUpdateStep step)
        {
            Steps++;
            LastStep = step;
        }

        protected override void OnAfterFixedUpdates(int stepCount) => Batches++;

        // End the probe before disposing the process-wide singleton used by other tests.
        protected override void OnDisposing() => throw new CleanupReachedException();
    }
}
