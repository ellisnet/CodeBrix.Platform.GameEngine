using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;
using Timer = CodeBrix.Platform.GameEngine.Timers.Timer;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the global <see cref="Engine.Pause"/>/<see cref="Engine.Resume"/> feature. All
/// engine-pause tests live in this one class so they serialize against the engine singleton.
/// </summary>
public class EnginePauseTests
{
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("The expected condition was not reached in time.");
            Thread.Sleep(10);
        }
    }

    private static void EnsureResumedAndStopped()
    {
        if (Engine.Instance.IsPaused)
            Engine.Instance.Resume();
        if (Engine.Instance.IsRunning)
            Engine.Instance.Stop();
    }

    [Fact]
    public void Pause_and_Resume_are_idempotent_and_raise_events_once()
    {
        //Arrange
        int pausedCount = 0;
        int resumedCount = 0;
        Action onPaused = () => Interlocked.Increment(ref pausedCount);
        Action onResumed = () => Interlocked.Increment(ref resumedCount);
        Engine.Instance.Paused += onPaused;
        Engine.Instance.Resumed += onResumed;

        try
        {
            //Act + Assert (engine not running: the framebuffer-mode inline transition path)
            Engine.Instance.Pause();
            Engine.Instance.IsPaused.Should().BeTrue();
            pausedCount.Should().Be(1);

            Engine.Instance.Pause(); // second pause is a no-op
            pausedCount.Should().Be(1);

            Engine.Instance.Resume();
            Engine.Instance.IsPaused.Should().BeFalse();
            resumedCount.Should().Be(1);

            Engine.Instance.Resume(); // second resume is a no-op
            resumedCount.Should().Be(1);
        }
        finally
        {
            Engine.Instance.Paused -= onPaused;
            Engine.Instance.Resumed -= onResumed;
            EnsureResumedAndStopped();
        }
    }

    [Fact]
    public void Pause_parks_engine_pausable_loop_and_Resume_wakes_it_without_burst()
    {
        //Arrange
        using var loop = new FixedRateGameLoop(100, () => { }) { PauseWithEngine = true };

        try
        {
            loop.Start();
            WaitUntil(() => loop.TicCount > 5);

            //Act — pause; the call must not return until the loop is parked
            Engine.Instance.Pause();
            long countAtPause = loop.TicCount;

            //Assert — parked: no tics while paused
            Thread.Sleep(250);
            loop.IsPaused.Should().BeTrue();
            loop.TicCount.Should().Be(countAtPause);

            //Act — resume
            Engine.Instance.Resume();
            WaitUntil(() => loop.TicCount > countAtPause);

            //Assert — no catch-up burst: over ~200 ms at 100 Hz, far fewer than the ~25+
            // tics the 250 ms pause would have produced as a burst
            Thread.Sleep(200);
            long ticsAfterResume = loop.TicCount - countAtPause;
            ticsAfterResume.Should().BeLessThan(50);
            loop.DroppedTics.Should().Be(0);
        }
        finally
        {
            EnsureResumedAndStopped();
            loop.Stop();
        }
    }

    [Fact]
    public void Loop_started_while_engine_paused_starts_parked()
    {
        //Arrange
        using var loop = new FixedRateGameLoop(100, () => { }) { PauseWithEngine = true };

        try
        {
            //Act
            Engine.Instance.Pause();
            loop.Start();
            Thread.Sleep(200);

            //Assert
            loop.TicCount.Should().Be(0);
            loop.IsPaused.Should().BeTrue();

            Engine.Instance.Resume();
            WaitUntil(() => loop.TicCount > 0);
        }
        finally
        {
            EnsureResumedAndStopped();
            loop.Stop();
        }
    }

    [Fact]
    public void Engine_cycle_loop_pauses_quiescently_and_resumes()
    {
        //Arrange
        long cycleCount = 0;
        Action onCycle = () => Interlocked.Increment(ref cycleCount);
        int pausedCount = 0;
        Action onPaused = () => Interlocked.Increment(ref pausedCount);
        Engine.Instance.BeforeBackgroundTasksExecute += onCycle;
        Engine.Instance.Paused += onPaused;

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            WaitUntil(() => Interlocked.Read(ref cycleCount) > 10);

            //Act — Pause() must block until the cycle loop is parked
            Engine.Instance.Pause();
            long countAtPause = Interlocked.Read(ref cycleCount);
            pausedCount.Should().Be(1);

            //Assert — parked: no further cycles
            Thread.Sleep(250);
            Interlocked.Read(ref cycleCount).Should().Be(countAtPause);

            //Act + Assert — resume: cycling continues
            Engine.Instance.Resume();
            WaitUntil(() => Interlocked.Read(ref cycleCount) > countAtPause);
        }
        finally
        {
            Engine.Instance.BeforeBackgroundTasksExecute -= onCycle;
            Engine.Instance.Paused -= onPaused;
            EnsureResumedAndStopped();
        }
    }

    [Fact]
    public void Pause_before_engine_start_parks_the_loop_immediately()
    {
        //Arrange
        long cycleCount = 0;
        Action onCycle = () => Interlocked.Increment(ref cycleCount);
        Engine.Instance.BeforeBackgroundTasksExecute += onCycle;

        try
        {
            //Act
            Engine.Instance.Pause();
            Engine.Instance.Start(new SynchronizationContext());
            Thread.Sleep(250);

            //Assert — the loop started parked; no cycles ran
            Interlocked.Read(ref cycleCount).Should().Be(0);

            Engine.Instance.Resume();
            WaitUntil(() => Interlocked.Read(ref cycleCount) > 0);
        }
        finally
        {
            Engine.Instance.BeforeBackgroundTasksExecute -= onCycle;
            EnsureResumedAndStopped();
        }
    }

    [Fact]
    public async Task Stop_and_restart_while_paused_do_not_deadlock()
    {
        //Arrange
        Engine.Instance.Start(new SynchronizationContext());
        Engine.Instance.Pause();

        //Act — stopping a parked engine must complete promptly (WaitAsync throws
        // TimeoutException — failing the test — if the stop deadlocks)
        var stopTask = Task.Run(() => Engine.Instance.Stop(), TestContext.Current.CancellationToken);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        //Assert
        Engine.Instance.IsRunning.Should().BeFalse();

        // Cleanup: leave the singleton unpaused for other tests
        Engine.Instance.Resume();
    }

    [Fact]
    public void TotalSecondsEngineRunning_excludes_paused_time()
    {
        try
        {
            //Arrange
            Engine.Instance.Start(new SynchronizationContext());
            Thread.Sleep(50);

            //Act
            Engine.Instance.Pause();
            double atPause = Engine.Instance.TotalSecondsEngineRunning;
            Thread.Sleep(400);
            double whilePaused = Engine.Instance.TotalSecondsEngineRunning;
            Engine.Instance.Resume();
            double justAfterResume = Engine.Instance.TotalSecondsEngineRunning;

            //Assert — frozen while paused, and the 400 ms pause is not counted
            whilePaused.Should().Be(atPause);
            (justAfterResume - atPause).Should().BeLessThan(0.2);
        }
        finally
        {
            EnsureResumedAndStopped();
        }
    }

    [Fact]
    public void Timer_ShiftAllForResume_prevents_timer_burst()
    {
        //Arrange
        long tps = HighResTimer.TicksPerSecond;
        long t0 = HighResTimer.GetCurrentTick();
        int fireCount = 0;
        var timer = Timer.Add("pause-test-timer", TimerType.PreCycle, TimerCycles.Repeating, 1.0);
        timer.Tick += () => fireCount++;

        try
        {
            //Act — one interval elapses normally
            Timer.RaiseTimerEvents(TimerType.PreCycle, t0 + (long)(1.1 * tps));
            fireCount.Should().Be(1);

            // A 10-second pause happens; on resume every baseline is shifted
            long resumeTick = t0 + (long)(11.2 * tps);
            Timer.ShiftAllForResume(10 * tps, resumeTick);

            //Assert — no burst right after the resume...
            Timer.RaiseTimerEvents(TimerType.PreCycle, t0 + (long)(11.3 * tps));
            fireCount.Should().Be(1);

            // ...and the timer fires again once its interval genuinely elapses
            Timer.RaiseTimerEvents(TimerType.PreCycle, t0 + (long)(12.2 * tps));
            fireCount.Should().Be(2);
        }
        finally
        {
            Timer.Remove("pause-test-timer");
        }
    }

    [Fact]
    public void ShiftBaselineForResume_applies_the_min_rule()
    {
        //Arrange
        long resumeTick = 1_000_000;
        long pausedTicks = 300_000;

        //Act + Assert — a pre-pause baseline shifts by the full paused duration
        HighResTimer.ShiftBaselineForResume(600_000, pausedTicks, resumeTick).Should().Be(900_000);

        // A baseline captured DURING the pause clamps to the resume tick, never the future
        HighResTimer.ShiftBaselineForResume(900_000, pausedTicks, resumeTick).Should().Be(1_000_000);

        // A baseline at the resume tick is unchanged
        HighResTimer.ShiftBaselineForResume(1_000_000, pausedTicks, resumeTick).Should().Be(1_000_000);
    }

    [Fact]
    public void AudioPauseRegistry_suspends_by_the_short_sfx_rule_and_resumes_the_snapshot()
    {
        //Arrange
        var shortSfx = new FakeVoice { IsPlaying = true, Duration = TimeSpan.FromSeconds(0.5) };
        var longClip = new FakeVoice { IsPlaying = true, Duration = TimeSpan.FromSeconds(5) };
        var stream = new FakeVoice { IsPlaying = true, Duration = null };
        var optedOut = new FakeVoice { IsPlaying = true, Duration = TimeSpan.FromSeconds(5), Override = false };
        var forcedShort = new FakeVoice { IsPlaying = true, Duration = TimeSpan.FromSeconds(0.5), Override = true };
        var silent = new FakeVoice { IsPlaying = false, Duration = TimeSpan.FromSeconds(5) };

        AudioPauseRegistry.Register(shortSfx);
        AudioPauseRegistry.Register(longClip);
        AudioPauseRegistry.Register(stream);
        AudioPauseRegistry.Register(optedOut);
        AudioPauseRegistry.Register(forcedShort);
        AudioPauseRegistry.Register(silent);

        //Act
        AudioPauseRegistry.SuspendAll(1.0);

        //Assert — suspend decisions
        shortSfx.PauseCount.Should().Be(0);      // rings out
        longClip.PauseCount.Should().Be(1);
        stream.PauseCount.Should().Be(1);        // endless always suspends
        optedOut.PauseCount.Should().Be(0);      // per-voice opt-out
        forcedShort.PauseCount.Should().Be(1);   // per-voice force
        silent.PauseCount.Should().Be(0);        // not playing

        //Act + Assert — exactly the suspended snapshot resumes
        AudioPauseRegistry.ResumeAll();
        longClip.ResumeCount.Should().Be(1);
        stream.ResumeCount.Should().Be(1);
        forcedShort.ResumeCount.Should().Be(1);
        shortSfx.ResumeCount.Should().Be(0);
        optedOut.ResumeCount.Should().Be(0);
        silent.ResumeCount.Should().Be(0);

        // Leave the fakes silent so later engine pauses in this class ignore them
        shortSfx.IsPlaying = longClip.IsPlaying = stream.IsPlaying = false;
        optedOut.IsPlaying = forcedShort.IsPlaying = false;
    }

    [Fact]
    public void Pause_captures_the_last_presented_frame_from_a_presenter()
    {
        //Arrange
        using var presenter = new TestPresenter();
        presenter.Configure(4, 4, PixelBufferFormat.Rgba8888);
        var frame = new byte[4 * 4 * 4];
        for (int i = 0; i < frame.Length; i += 4)
        {
            frame[i] = 255;      // solid red, opaque
            frame[i + 3] = 255;
        }
        presenter.PresentFrame(frame);

        try
        {
            //Act
            Engine.Instance.Pause();

            //Assert — the presenter captured a stable, correctly sized snapshot, and the
            // engine exposes a capture globally
            presenter.LastFrameBeforePause.Should().NotBeNull();
            presenter.LastFrameBeforePause!.Width.Should().Be(4);
            presenter.LastFrameBeforePause.Height.Should().Be(4);
            Engine.Instance.LastFrameBeforePause.Should().NotBeNull();

            using var pixels = new SKBitmap(presenter.LastFrameBeforePause.Info);
            presenter.LastFrameBeforePause.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0);
            pixels.GetPixel(2, 2).Red.Should().Be((byte)255);

            //Assert — the Skia-free RGBA export matches: solid red, opaque, R,G,B,A order
            var rgba = presenter.LastFrameBeforePauseAsRgba(out int width, out int height);
            width.Should().Be(4);
            height.Should().Be(4);
            rgba.Should().NotBeNull();
            rgba!.Length.Should().Be(4 * 4 * 4);
            int center = (2 * width + 2) * 4;
            rgba[center].Should().Be((byte)255);       // R
            rgba[center + 1].Should().Be((byte)0);     // G
            rgba[center + 2].Should().Be((byte)0);     // B
            rgba[center + 3].Should().Be((byte)255);   // A

            var engineRgba = Engine.Instance.LastFrameBeforePauseAsRgba(out int engineWidth, out int engineHeight);
            engineRgba.Should().NotBeNull();
            (engineWidth * engineHeight * 4).Should().Be(engineRgba!.Length);
        }
        finally
        {
            EnsureResumedAndStopped();
        }
    }

    private sealed class FakeVoice : IEnginePausableAudio
    {
        public bool IsPlaying;
        public TimeSpan? Duration;
        public bool? Override;
        public int PauseCount;
        public int ResumeCount;

        bool IEnginePausableAudio.IsPlayingForEnginePause => IsPlaying;
        TimeSpan? IEnginePausableAudio.KnownDurationForEnginePause => Duration;
        bool? IEnginePausableAudio.SuspendOnEnginePause => Override;
        void IEnginePausableAudio.EnginePause() => PauseCount++;
        void IEnginePausableAudio.EngineResume() => ResumeCount++;
    }

    private sealed class TestPresenter : PixelFramePresenter
    {
        protected override void RequestPaint()
        {
        }
    }
}
