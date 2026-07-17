using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Timers; //was previously: Gondwana.Timers;
/// <summary>
/// Provides utility methods for working with the system's high-resolution timer.
/// </summary>
/// <remarks>The <see cref="HighResTimer"/> class offers methods to retrieve high-resolution tick counts and
/// calculate elapsed time with precision. It relies on the <see cref="System.Diagnostics.Stopwatch"/> class to access
/// the system's high-resolution performance counter, if available.</remarks>
public static class HighResTimer
{
    /// <summary>
    /// The number of ticks per second for the system's high-resolution timer.
    /// </summary>
    public static long TicksPerSecond => Stopwatch.Frequency;

    /// <summary>
    /// Indicates whether a high-resolution performance counter is available.
    /// </summary>
    public static bool HighPerfSupported => Stopwatch.IsHighResolution;

    /// <summary>
    /// Gets the current tick count using the high-resolution timer.
    /// </summary>
    public static long GetCurrentTick() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Returns the elapsed time in seconds between two tick counts.
    /// </summary>
    public static float GetDuration(long start, long stop) => (float)(stop - start) / TicksPerSecond;

    /// <summary>
    /// Returns the elapsed time in seconds since the given start tick.
    /// </summary>
    public static float GetElapsedSince(long start) => GetDuration(start, GetCurrentTick());

    /// <summary>
    /// Shifts a tick baseline forward on resume from a global engine pause: by the paused
    /// duration, but never past <paramref name="resumeTick"/> — so a baseline captured DURING
    /// the pause (e.g. by a pause-overlay drawing created in a Paused handler) is never pushed
    /// into the future.
    /// </summary>
    /// <param name="baseline">The tick baseline to shift.</param>
    /// <param name="pausedTicks">The duration of the pause, in ticks.</param>
    /// <param name="resumeTick">The current tick at the moment of resume.</param>
    /// <returns>The shifted baseline.</returns>
    internal static long ShiftBaselineForResume(long baseline, long pausedTicks, long resumeTick)
        => baseline + Math.Min(pausedTicks, Math.Max(0L, resumeTick - baseline));
}