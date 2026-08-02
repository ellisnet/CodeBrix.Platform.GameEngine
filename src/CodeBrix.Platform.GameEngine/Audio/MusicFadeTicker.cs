using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// One fade in flight: a value moving from <see cref="From"/> to <see cref="To"/> over
/// <see cref="DurationSeconds"/>, applied every tick.
/// </summary>
internal sealed class MusicFade
{
    internal float From;
    internal float To;
    internal double DurationSeconds;
    internal double ElapsedSeconds;
    internal Action<float> Apply = null!;
    internal Action? OnComplete;
    internal bool Cancelled;

    /// <summary>The value this fade should be applying at its current elapsed time.</summary>
    internal float CurrentValue
    {
        get
        {
            if (DurationSeconds <= 0)
            {
                return To;
            }

            var t = ElapsedSeconds / DurationSeconds;
            return t >= 1.0 ? To : (float)(From + ((To - From) * t));
        }
    }

    internal bool IsComplete => Cancelled || ElapsedSeconds >= DurationSeconds;
}

/// <summary>
/// The clock every music fade, crossfade, duck and stem transition runs on: one background thread
/// that ticks the active fades and applies their values.
/// </summary>
/// <remarks>
/// <para>
/// WHY A THREAD RATHER THAN THE ENGINE'S TIMERS: fades have to work in BOTH hosting modes, and in
/// Mode B the engine cycle never runs, so <see cref="Timers.Timer"/> is not available. A dedicated
/// thread is the only clock that behaves identically either way.
/// </para>
/// <para>
/// It costs nothing when idle: with no fade in flight the thread blocks on a wait handle rather than
/// spinning, and it is not started at all until the first fade is queued. The tick loop itself
/// allocates nothing — allocation happens when a fade STARTS, which is a scene change, not a frame.
/// </para>
/// <para>
/// It FREEZES with the global engine pause (<see cref="AudioPauseRegistry"/> drives that), so a
/// two-second crossfade spanning a ten-minute pause is still a two-second crossfade. Elapsed time is
/// accumulated from a stopwatch delta per tick rather than from wall-clock arithmetic, so a frozen
/// interval simply never accumulates.
/// </para>
/// <para>
/// It writes float volumes and nothing else: it never touches game state, the scene graph or UI.
/// </para>
/// </remarks>
internal sealed class MusicFadeTicker : IDisposable
{
    private const int TickMilliseconds = 20;

    private readonly object _gate = new();
    private readonly List<MusicFade> _fades = new();
    private readonly AutoResetEvent _wake = new(false);

    private Thread? _thread;
    private volatile bool _frozen;
    private volatile bool _stopped;

    /// <summary>The number of fades currently in flight. For diagnostics and tests.</summary>
    internal int ActiveFadeCount
    {
        get { lock (_gate) { return _fades.Count; } }
    }

    /// <summary>Whether the ticker is frozen by the global engine pause.</summary>
    internal bool IsFrozen => _frozen;

    /// <summary>
    /// Suppresses the background thread so <see cref="Tick"/> is the only thing that advances a
    /// fade. For tests: a real ticking thread makes fade assertions a race, and sleeping long enough
    /// to be sure would make the suite slow and still flaky.
    /// </summary>
    internal bool ManualTickingForTests { get; set; }

    /// <summary>
    /// Queues a fade, starting the ticker thread if this is the first one. A zero or negative
    /// duration applies the destination value immediately and completes without a tick.
    /// </summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The destination value.</param>
    /// <param name="duration">How long the fade takes. Zero or less applies <paramref name="to"/> at once.</param>
    /// <param name="apply">Applies an intermediate value. Called on the ticker thread.</param>
    /// <param name="onComplete">Runs once the fade reaches its destination. Not called if cancelled.</param>
    /// <returns>The queued fade, for <see cref="Cancel"/>.</returns>
    internal MusicFade Add(float from, float to, TimeSpan duration, Action<float> apply, Action? onComplete = null)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var fade = new MusicFade
        {
            From = from,
            To = to,
            DurationSeconds = duration.TotalSeconds,
            Apply = apply,
            OnComplete = onComplete,
        };

        if (fade.DurationSeconds <= 0)
        {
            // No clock needed: an instant "fade" is just the destination value.
            apply(to);
            onComplete?.Invoke();
            return fade;
        }

        lock (_gate)
        {
            _fades.Add(fade);
            EnsureThreadStarted();
        }

        _wake.Set();
        return fade;
    }

    /// <summary>Cancels a fade, leaving the value wherever it had reached.</summary>
    /// <param name="fade">The fade to cancel. Null and already-finished fades are ignored.</param>
    internal void Cancel(MusicFade? fade)
    {
        if (fade is null)
        {
            return;
        }

        lock (_gate)
        {
            fade.Cancelled = true;
            _fades.Remove(fade);
        }
    }

    /// <summary>Cancels every fade in flight.</summary>
    internal void CancelAll()
    {
        lock (_gate)
        {
            foreach (var fade in _fades)
            {
                fade.Cancelled = true;
            }

            _fades.Clear();
        }
    }

    /// <summary>Freezes fade progress for the global engine pause.</summary>
    internal void Freeze() => _frozen = true;

    /// <summary>Resumes fade progress after the global engine pause.</summary>
    internal void Unfreeze()
    {
        _frozen = false;
        _wake.Set();
    }

    /// <summary>
    /// Advances every fade by <paramref name="deltaSeconds"/> and applies the results. The ticker
    /// thread calls this; tests call it directly to advance fades deterministically instead of
    /// sleeping.
    /// </summary>
    /// <param name="deltaSeconds">The elapsed time to apply.</param>
    internal void Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        List<MusicFade>? completed = null;
        MusicFade[] active;

        lock (_gate)
        {
            if (_fades.Count == 0)
            {
                return;
            }

            active = _fades.ToArray();

            foreach (var fade in active)
            {
                fade.ElapsedSeconds += deltaSeconds;
            }

            for (var i = _fades.Count - 1; i >= 0; i--)
            {
                if (_fades[i].IsComplete)
                {
                    (completed ??= new List<MusicFade>()).Add(_fades[i]);
                    _fades.RemoveAt(i);
                }
            }
        }

        // Apply OUTSIDE the lock: an apply callback reaches into an audio graph, and a completion
        // callback can queue the next fade (a crossfade chains this way), which would re-enter.
        foreach (var fade in active)
        {
            if (fade.Cancelled)
            {
                continue;
            }

            Invoke(() => fade.Apply(fade.CurrentValue), "apply a music fade value");
        }

        if (completed is null)
        {
            return;
        }

        foreach (var fade in completed)
        {
            if (!fade.Cancelled && fade.OnComplete is not null)
            {
                Invoke(fade.OnComplete, "run a music fade completion callback");
            }
        }
    }

    /// <summary>Stops the ticker thread and drops every fade.</summary>
    public void Dispose()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        CancelAll();
        _wake.Set();

        var thread = _thread;
        _thread = null;

        // Bounded: the loop checks _stopped every tick, so this waits at most one tick plus slack.
        thread?.Join(TimeSpan.FromMilliseconds(TickMilliseconds * 10));
        _wake.Dispose();
    }

    // Callers hold _gate.
    private void EnsureThreadStarted()
    {
        if (_thread is not null || _stopped || ManualTickingForTests)
        {
            return;
        }

        _thread = new Thread(RunLoop)
        {
            IsBackground = true,        // never keeps a closing game alive
            Name = "CodeBrix music fades",
        };

        _thread.Start();
    }

    private void RunLoop()
    {
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed;

        while (!_stopped)
        {
            var now = clock.Elapsed;
            var delta = (now - previous).TotalSeconds;
            previous = now;

            if (!_frozen)
            {
                Invoke(() => Tick(delta), "tick the music fades");
            }

            bool idle;
            lock (_gate)
            {
                idle = _fades.Count == 0;
            }

            if (idle)
            {
                // Park rather than spin. The wait resets the clock baseline on wake, so time spent
                // parked (or frozen) never lands on a fade as a single huge delta.
                _wake.WaitOne();
                previous = clock.Elapsed;
            }
            else
            {
                Thread.Sleep(TickMilliseconds);
            }
        }
    }

    // One exception on this thread would otherwise kill every fade in the game for the rest of the
    // process, so nothing here is allowed to escape.
    private static void Invoke(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "Failed to {What}.", what);
        }
    }
}
