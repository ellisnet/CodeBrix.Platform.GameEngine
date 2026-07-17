using System;
using System.Threading;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class FixedRateGameLoopTests
{
    [Fact]
    public void Constructor_rejects_out_of_range_rates()
    {
        //Arrange
        Action zero = () => _ = new FixedRateGameLoop(0, () => { });
        Action tooFast = () => _ = new FixedRateGameLoop(1001, () => { });

        //Act + Assert
        zero.Should().Throw<ArgumentOutOfRangeException>();
        tooFast.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_rejects_null_callback()
    {
        //Arrange
        Action act = () => _ = new FixedRateGameLoop(35, null!);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Start_paces_tics_at_the_target_rate()
    {
        //Arrange
        var ticCount = 0;
        using var loop = new FixedRateGameLoop(100, () => Interlocked.Increment(ref ticCount));

        //Act
        loop.Start();
        Thread.Sleep(500);
        loop.Stop();

        //Assert - generous tolerance for busy CI machines: the point is "roughly 50",
        // not "25 (half rate)" or "unbounded".
        var observed = Volatile.Read(ref ticCount);
        (observed >= 30).Should().BeTrue($"expected at least 30 tics in 500 ms at 100 Hz but observed {observed}");
        (observed <= 70).Should().BeTrue($"expected at most 70 tics in 500 ms at 100 Hz but observed {observed}");
    }

    [Fact]
    public void Stop_ends_the_loop_and_start_can_run_it_again()
    {
        //Arrange
        var ticCount = 0;
        using var loop = new FixedRateGameLoop(200, () => Interlocked.Increment(ref ticCount));

        //Act
        loop.Start();
        Thread.Sleep(100);
        loop.Stop();
        var afterStop = Volatile.Read(ref ticCount);
        Thread.Sleep(100);
        var afterWait = Volatile.Read(ref ticCount);
        loop.Start();
        Thread.Sleep(100);
        loop.Stop();
        var afterRestart = Volatile.Read(ref ticCount);

        //Assert
        afterWait.Should().Be(afterStop);
        (afterRestart > afterWait).Should().BeTrue("the loop should tick again after a restart");
    }

    [Fact]
    public void Pause_suspends_tics_and_resume_continues_without_a_burst()
    {
        //Arrange
        var ticCount = 0;
        using var loop = new FixedRateGameLoop(100, () => Interlocked.Increment(ref ticCount));
        loop.Start();
        Thread.Sleep(100);

        //Act
        loop.Pause();
        Thread.Sleep(50); // let the tic in progress finish
        var pausedCount = Volatile.Read(ref ticCount);
        Thread.Sleep(300); // paused time that must NOT be caught up after resume
        var stillPausedCount = Volatile.Read(ref ticCount);
        loop.Resume();
        Thread.Sleep(100);
        loop.Stop();
        var resumedCount = Volatile.Read(ref ticCount);

        //Assert
        stillPausedCount.Should().Be(pausedCount);
        (resumedCount > pausedCount).Should().BeTrue("the loop should tick again after Resume");
        // 300 ms of pause at 100 Hz would be ~30 burst tics; a re-baselined resume adds
        // only the ~10 tics of the 100 ms run window (plus slack).
        (resumedCount - pausedCount <= 20).Should().BeTrue(
            $"resume must not burst to catch up the paused time (saw {resumedCount - pausedCount} tics after resume)");
    }

    [Fact]
    public void Long_stall_drops_tics_instead_of_bursting()
    {
        //Arrange
        var ticCount = 0;
        var stallOnce = true;
        using var loop = new FixedRateGameLoop(100, () =>
        {
            Interlocked.Increment(ref ticCount);
            if (stallOnce)
            {
                stallOnce = false;
                Thread.Sleep(300); // ~30 periods behind, far past MaxCatchUpTics
            }
        })
        { MaxCatchUpTics = 5 };

        //Act
        loop.Start();
        Thread.Sleep(600);
        loop.Stop();

        //Assert
        (loop.DroppedTics > 0).Should().BeTrue("a stall beyond MaxCatchUpTics periods must drop tics");
        // Total run 600 ms at 100 Hz = 60 scheduled tics; 300 ms stalled. Without the bound
        // the loop would burst back to ~60; with it, the dropped tics keep the count near 30.
        (Volatile.Read(ref ticCount) < 50).Should().BeTrue(
            $"the bounded catch-up should prevent a full burst (observed {Volatile.Read(ref ticCount)} tics)");
    }

    [Fact]
    public void Callback_exception_stops_the_loop_and_reports_it()
    {
        //Arrange
        var failure = new InvalidOperationException("tic failed");
        Exception? reported = null;
        using var loop = new FixedRateGameLoop(100, () => throw failure);
        loop.UnhandledException += ex => reported = ex;

        //Act
        loop.Start();
        Thread.Sleep(150);

        //Assert
        loop.IsRunning.Should().BeFalse();
        loop.LastException.Should().Be(failure);
        reported.Should().Be(failure);
    }

    [Fact]
    public void Start_twice_throws()
    {
        //Arrange
        using var loop = new FixedRateGameLoop(35, () => { });
        loop.Start();

        //Act
        Action act = loop.Start;

        //Assert
        act.Should().Throw<InvalidOperationException>();
        loop.Stop();
    }
}
