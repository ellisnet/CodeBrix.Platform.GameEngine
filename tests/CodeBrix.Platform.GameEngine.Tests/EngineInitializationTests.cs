using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the <see cref="Engine"/> initialization, start and shutdown lifecycle. Every test
/// builds its own private <see cref="Engine"/> through <see cref="Activator"/> so the global
/// <see cref="Engine.Instance"/> singleton is never started, paused or disposed by this class.
/// Disposal still reaches process-wide subsystems (input pollers, timers, the shared audio
/// output), which is why this assembly runs serially.
/// </summary>
public class EngineInitializationTests
{
    [Fact]
    public void Initialize_resets_initialization_state_and_signals_completion_when_it_throws()
    {
        //Arrange
        var engine = CreateEngineInstance();
        var invalidConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(invalidConfigPath, "{");

        try
        {
            //Act
            Action initialize = () => engine.Initialize(configFileName: invalidConfigPath);

            //Assert
            initialize.Should().Throw<Exception>();
            engine.IsInitializing.Should().BeFalse();
            engine.IsInitialized.Should().BeFalse();
            GetInitDoneEvent(engine).IsSet.Should().BeTrue();
        }
        finally
        {
            GC.SuppressFinalize(engine);
            File.Delete(invalidConfigPath);
        }
    }

    [Fact]
    public void Initialize_can_retry_successfully_after_a_failed_attempt()
    {
        //Arrange
        var engine = CreateEngineInstance();
        var invalidConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(invalidConfigPath, "{");

        try
        {
            Action initialize = () => engine.Initialize(configFileName: invalidConfigPath);
            initialize.Should().Throw<Exception>();

            //Act (a missing file just yields defaults, so this attempt succeeds)
            var missingConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            engine.Initialize(configFileName: missingConfigPath);

            //Assert
            engine.IsInitializing.Should().BeFalse();
            engine.IsInitialized.Should().BeTrue();
        }
        finally
        {
            GC.SuppressFinalize(engine);
            File.Delete(invalidConfigPath);
        }
    }

    [Fact]
    public void Start_uses_the_configured_wait_timeout_when_initialization_is_in_progress()
    {
        //Arrange
        var engine = CreateEngineInstance();

        try
        {
            engine.Configuration.StartInitializationWaitTimeout = 0.001f;
            SetInitializationState(engine, isInitializing: true, isInitialized: false);
            GetInitDoneEvent(engine).Reset();

            //Act
            Action start = () => engine.Start(new SynchronizationContext());

            //Assert
            var expectedSeconds = engine.Configuration.StartInitializationWaitTimeout
                .ToString("0.###", CultureInfo.CurrentCulture);
            start.Should().Throw<InvalidOperationException>()
                .WithMessage($"*did not complete within {expectedSeconds} seconds*");
        }
        finally
        {
            GC.SuppressFinalize(engine);
        }
    }

    [Fact]
    public void Start_reports_an_initialization_that_failed_on_another_thread()
    {
        //Arrange
        var engine = CreateEngineInstance();

        try
        {
            SetInitializationState(engine, isInitializing: true, isInitialized: false);
            GetInitDoneEvent(engine).Set(); // the other thread finished — unsuccessfully

            //Act
            Action start = () => engine.Start(new SynchronizationContext());

            //Assert
            start.Should().Throw<InvalidOperationException>()
                .WithMessage("*failed on another thread*");
        }
        finally
        {
            GC.SuppressFinalize(engine);
        }
    }

    [Fact]
    public async Task Dispose_does_not_wait_for_its_own_cycle_task_when_called_on_the_engine_thread()
    {
        //Arrange
        var engine = CreateEngineInstance();
        Task? cycleTask = null;
        var disposeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            SetIsRunning(engine, true);

            //Act
            cycleTask = Task.Run(
                () =>
                {
                    SpinWait.SpinUntil(() => cycleTask is not null, TimeSpan.FromSeconds(1)).Should().BeTrue();

                    engine.EngineDispatcher.BindToCurrentThread();
                    SetCycleTask(engine, cycleTask!);

                    engine.Dispose();
                    disposeReturned.SetResult();
                },
                TestContext.Current.CancellationToken);

            //Assert — Dispose returns while its own cycle task is still running...
            await disposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            // ...and the managed cleanup completes once that task does
            await cycleTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            SpinWait.SpinUntil(() => engine.IsDisposed, TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
        finally
        {
            GC.SuppressFinalize(engine);
        }
    }

    [Fact]
    public async Task Dispose_defers_managed_cleanup_when_called_from_inside_a_cycle()
    {
        //Arrange
        var engine = CreateEngineInstance();
        Task? cycleTask = null;
        var disposeObservedInsideCycle =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            SetIsRunning(engine, true);
            engine.Configuration.SamplingTimeForCPS = 0;
            engine.BeforeBackgroundTasksExecute += () =>
            {
                engine.Dispose();
                disposeObservedInsideCycle.SetResult(engine.IsDisposed);
            };

            //Act
            cycleTask = Task.Run(
                () =>
                {
                    SpinWait.SpinUntil(() => cycleTask is not null, TimeSpan.FromSeconds(1)).Should().BeTrue();

                    engine.EngineDispatcher.BindToCurrentThread();
                    SetCycleTask(engine, cycleTask!);
                    InvokeCycle(engine);
                },
                TestContext.Current.CancellationToken);

            var wasDisposedInsideCycle = await disposeObservedInsideCycle.Task
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await cycleTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            //Assert
            wasDisposedInsideCycle.Should().BeFalse();
            SpinWait.SpinUntil(() => engine.IsDisposed, TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
        finally
        {
            GC.SuppressFinalize(engine);
        }
    }

    [Fact]
    public async Task StopAndWait_waits_for_a_pending_cycle_when_already_stopped()
    {
        //Arrange
        var engine = CreateEngineInstance();
        var cycle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetCycleTask(engine, cycle.Task);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        //Act
        var stopping = Task.Run(
            () =>
            {
                entered.SetResult();
                engine.StopAndWait();
            },
            TestContext.Current.CancellationToken);

        try
        {
            //Assert
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Func<Task> stopBeforeTheCycleEnds =
                () => stopping.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            await stopBeforeTheCycleEnds.Should().ThrowAsync<TimeoutException>();

            cycle.SetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            engine.IsDisposed.Should().BeFalse();
        }
        finally
        {
            cycle.TrySetResult();
            await stopping;
            GC.SuppressFinalize(engine);
        }
    }

    [Fact]
    public void StopAndWait_rejects_a_self_wait_from_the_active_engine_thread()
    {
        //Arrange
        var engine = CreateEngineInstance();

        try
        {
            engine.EngineDispatcher.BindToCurrentThread();
            SetCycleTask(engine, new TaskCompletionSource().Task);

            //Act
            Action stopAndWait = () => engine.StopAndWait();

            //Assert
            stopAndWait.Should().Throw<InvalidOperationException>()
                .WithMessage("*outside the background engine thread*");
        }
        finally
        {
            GC.SuppressFinalize(engine);
        }
    }

    private static Engine CreateEngineInstance() =>
        (Engine)Activator.CreateInstance(typeof(Engine), nonPublic: true)!;

    private static ManualResetEventSlim GetInitDoneEvent(Engine engine)
    {
        var field = typeof(Engine).GetField(
            "_initDone",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Engine._initDone via reflection.");

        return (ManualResetEventSlim)(field.GetValue(engine)
            ?? throw new InvalidOperationException("Engine._initDone is null."));
    }

    private static void SetInitializationState(Engine engine, bool isInitializing, bool isInitialized)
    {
        var initializingField = typeof(Engine).GetField(
            "_isInitializing",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Engine._isInitializing via reflection.");

        var initializedField = typeof(Engine).GetField(
            "_isInitialized",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Engine._isInitialized via reflection.");

        initializingField.SetValue(engine, isInitializing);
        initializedField.SetValue(engine, isInitialized);
    }

    private static void SetCycleTask(Engine engine, Task cycleTask)
    {
        var cycleTaskField = typeof(Engine).GetField(
            "_cycleTask",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Engine._cycleTask via reflection.");

        cycleTaskField.SetValue(engine, cycleTask);
    }

    private static void SetIsRunning(Engine engine, bool isRunning)
    {
        var property = typeof(Engine).GetProperty(
            nameof(Engine.IsRunning),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Could not find Engine.IsRunning via reflection.");

        property.SetValue(engine, isRunning);
    }

    private static void InvokeCycle(Engine engine)
    {
        var cycleMethod = typeof(Engine).GetMethod(
            "Cycle",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find Engine.Cycle via reflection.");

        cycleMethod.Invoke(engine, null);
    }
}
