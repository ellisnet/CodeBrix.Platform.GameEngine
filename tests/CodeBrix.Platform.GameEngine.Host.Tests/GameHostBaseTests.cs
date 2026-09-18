using System;
using System.Reflection;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Host.Hosting;
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
}
