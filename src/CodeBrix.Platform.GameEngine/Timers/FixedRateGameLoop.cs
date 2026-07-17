using System;
using System.Diagnostics;
using System.Threading;

namespace CodeBrix.Platform.GameEngine.Timers;

/// <summary>
/// Hosts a fixed-rate callback on a dedicated background thread — the pacing utility for
/// software-rendered (framebuffer-style) games that own their game loop and tick at a fixed
/// rate (e.g. 35 or 70 Hz).
/// </summary>
/// <remarks>
/// <para>
/// Pacing uses a non-drifting fixed timestep: each tic's target time advances the previous
/// target by exactly one period (never by "now"), so scheduling lag does not accumulate over
/// time. When the loop falls behind, tics run back to back to catch up, but never more than
/// <see cref="MaxCatchUpTics"/> periods' worth — a longer stall re-baselines the schedule and
/// counts the skipped tics in <see cref="DroppedTics"/>, so a suspend/resume or debugger break
/// cannot produce an unbounded burst.
/// </para>
/// <para>
/// Waiting is a sleep+yield hybrid, not a busy loop: the thread sleeps until close to the
/// target time and only yields/spins for the final stretch, so an idle loop does not consume
/// a full core.
/// </para>
/// <para>
/// The callback runs on the loop's dedicated thread. An exception thrown by the callback
/// stops the loop, is stored in <see cref="LastException"/>, and is reported through
/// <see cref="UnhandledException"/>.
/// </para>
/// </remarks>
public sealed class FixedRateGameLoop : IDisposable
{
    // Sleep only until this close to the target, then yield-spin the rest — OS sleep
    // granularity makes closer sleeps overshoot.
    private const double SleepUntilMillisecondsBeforeTarget = 2.0;

    private readonly Action _tic;
    private readonly long _periodTicks;
    private readonly ManualResetEventSlim _resumeSignal = new(true);
    private readonly object _stateGate = new();

    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile bool _isPaused;
    private bool _isDisposed;

    private long _ticCount;
    private long _droppedTics;
    private double _actualTicsPerSecond;
    private long _rateWindowStartTimestamp;
    private long _rateWindowStartTicCount;

    /// <summary>
    /// Creates a loop that calls <paramref name="onTic"/> at a fixed rate of
    /// <paramref name="ticsPerSecond"/> once <see cref="Start"/> is called.
    /// </summary>
    /// <param name="ticsPerSecond">The tic rate in Hz (1 to 1000); any integer rate works (35, 60, 70, ...).</param>
    /// <param name="onTic">The callback to run each tic, on the loop's dedicated thread.</param>
    public FixedRateGameLoop(int ticsPerSecond, Action onTic)
    {
        if (ticsPerSecond < 1 || ticsPerSecond > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(ticsPerSecond), ticsPerSecond, "The tic rate must be between 1 and 1000 Hz.");
        }

        _tic = onTic ?? throw new ArgumentNullException(nameof(onTic));
        TargetTicsPerSecond = ticsPerSecond;
        _periodTicks = Stopwatch.Frequency / ticsPerSecond;
    }

    /// <summary>The fixed tic rate this loop targets, in Hz.</summary>
    public int TargetTicsPerSecond { get; }

    /// <summary>
    /// The maximum number of catch-up tics that may run back to back when the loop falls
    /// behind. Falling further behind than this re-baselines the schedule and counts the
    /// skipped tics in <see cref="DroppedTics"/>. Default is 5.
    /// </summary>
    public int MaxCatchUpTics { get; set; } = 5;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/> (also while paused).</summary>
    public bool IsRunning => _thread is not null;

    /// <summary>True while the loop is paused via <see cref="Pause"/>.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Total tics run since <see cref="Start"/>.</summary>
    public long TicCount => Interlocked.Read(ref _ticCount);

    /// <summary>Total scheduled tics skipped by the bounded catch-up policy.</summary>
    public long DroppedTics => Interlocked.Read(ref _droppedTics);

    /// <summary>
    /// The measured tic rate over the most recent one-second window, for drift/health
    /// monitoring. Zero until the first window completes after <see cref="Start"/>.
    /// </summary>
    public double ActualTicsPerSecond => Volatile.Read(ref _actualTicsPerSecond);

    /// <summary>The callback exception that stopped the loop, if any.</summary>
    public Exception? LastException { get; private set; }

    /// <summary>
    /// Raised on the loop thread when the tic callback throws; the loop stops afterwards.
    /// </summary>
    public event Action<Exception>? UnhandledException;

    /// <summary>Starts the loop on a new dedicated background thread.</summary>
    public void Start()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_thread is not null)
            {
                throw new InvalidOperationException("The loop is already running; call Stop() before starting it again.");
            }

            _stopRequested = false;
            _isPaused = false;
            _resumeSignal.Set();
            Interlocked.Exchange(ref _ticCount, 0);
            Interlocked.Exchange(ref _droppedTics, 0);
            Volatile.Write(ref _actualTicsPerSecond, 0);
            LastException = null;

            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = $"{nameof(FixedRateGameLoop)} {TargetTicsPerSecond} Hz",
            };
            _thread.Start();
        }
    }

    /// <summary>
    /// Stops the loop and waits for the loop thread to finish the tic in progress. Safe to
    /// call when not running.
    /// </summary>
    public void Stop()
    {
        Thread? thread;
        lock (_stateGate)
        {
            thread = _thread;
            _thread = null;
            _stopRequested = true;
            _resumeSignal.Set(); // release a paused loop so it can observe the stop
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join();
        }
    }

    /// <summary>Pauses the loop after the tic in progress; no tics run until <see cref="Resume"/>.</summary>
    public void Pause()
    {
        _isPaused = true;
        _resumeSignal.Reset();
    }

    /// <summary>
    /// Resumes a paused loop. The schedule is re-baselined, so the pause produces no
    /// catch-up burst.
    /// </summary>
    public void Resume()
    {
        _isPaused = false;
        _resumeSignal.Set();
    }

    /// <summary>Stops the loop and releases its resources.</summary>
    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
        }

        Stop();
        _resumeSignal.Dispose();
    }

    private void RunLoop()
    {
        var nextTarget = Stopwatch.GetTimestamp() + _periodTicks;
        _rateWindowStartTimestamp = Stopwatch.GetTimestamp();
        _rateWindowStartTicCount = 0;

        while (!_stopRequested)
        {
            if (_isPaused)
            {
                _resumeSignal.Wait();
                // Re-baseline so the paused time does not turn into a catch-up burst.
                nextTarget = Stopwatch.GetTimestamp() + _periodTicks;
                _rateWindowStartTimestamp = Stopwatch.GetTimestamp();
                _rateWindowStartTicCount = Interlocked.Read(ref _ticCount);
                continue;
            }

            WaitUntil(nextTarget);
            if (_stopRequested)
            {
                break;
            }

            try
            {
                _tic();
            }
            catch (Exception ex)
            {
                LastException = ex;
                _stopRequested = true;
                lock (_stateGate)
                {
                    _thread = null;
                }
                UnhandledException?.Invoke(ex);
                return;
            }

            var ticCount = Interlocked.Increment(ref _ticCount);
            nextTarget += _periodTicks;

            // Bounded catch-up: if we are further behind than MaxCatchUpTics periods,
            // skip the unrunnable tics and re-baseline instead of bursting.
            var now = Stopwatch.GetTimestamp();
            if (now > nextTarget)
            {
                var periodsBehind = (now - nextTarget) / _periodTicks;
                if (periodsBehind >= MaxCatchUpTics)
                {
                    Interlocked.Add(ref _droppedTics, periodsBehind);
                    nextTarget = now;
                }
            }

            // Publish the measured rate once per one-second window.
            var windowElapsed = now - _rateWindowStartTimestamp;
            if (windowElapsed >= Stopwatch.Frequency)
            {
                var windowTics = ticCount - _rateWindowStartTicCount;
                Volatile.Write(ref _actualTicsPerSecond, windowTics * (double)Stopwatch.Frequency / windowElapsed);
                _rateWindowStartTimestamp = now;
                _rateWindowStartTicCount = ticCount;
            }
        }
    }

    private void WaitUntil(long targetTimestamp)
    {
        while (!_stopRequested)
        {
            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return;
            }

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > SleepUntilMillisecondsBeforeTarget)
            {
                Thread.Sleep((int)(remainingMilliseconds - SleepUntilMillisecondsBeforeTarget));
            }
            else
            {
                Thread.Yield();
            }
        }
    }
}
