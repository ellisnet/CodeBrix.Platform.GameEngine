using System;

namespace CodeBrix.Platform.GameEngine.Timers; //was previously: Gondwana.Timers;

/// <summary>
/// Converts irregular externally driven ticks into bounded batches of fixed simulation steps.
/// </summary>
internal sealed class FixedStepAccumulator
{
    private long _lastDriverTick;
    private long _accumulatedTicks;

    /// <summary>Gets the tick represented by the most recently scheduled simulation step.</summary>
    internal long SimulationTick { get; private set; }

    /// <summary>Resets both the driver and simulation clocks to <paramref name="tick"/>.</summary>
    /// <param name="tick">The tick both clocks restart from.</param>
    internal void Reset(long tick)
    {
        _lastDriverTick = tick;
        _accumulatedTicks = 0;
        SimulationTick = tick;
    }

    /// <summary>
    /// Moves the driver clock past a globally paused interval so the first tick after
    /// <see cref="Engine.Resume"/> does not see the whole pause as elapsed driver time. The
    /// simulation tick itself does not move: simulation time is frozen while the engine is paused.
    /// </summary>
    /// <param name="pausedTicks">The duration of the pause, in ticks.</param>
    internal void ShiftForResume(long pausedTicks)
    {
        if (pausedTicks > 0)
            _lastDriverTick += pausedTicks;
    }

    /// <summary>
    /// Accumulates driver time and returns the fixed steps due for the next presentation opportunity.
    /// Excess backlog is discarded after <paramref name="maxSteps"/> steps to prevent a runaway
    /// catch-up sequence after throttling or a suspended host.
    /// </summary>
    /// <param name="driverTick">The current tick of the external driver.</param>
    /// <param name="updatesPerSecond">The fixed simulation rate, in hertz.</param>
    /// <param name="maxSteps">The maximum number of steps this batch may contain.</param>
    /// <returns>The batch of fixed simulation steps that are due.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="updatesPerSecond"/> or <paramref name="maxSteps"/> is less than one.
    /// </exception>
    internal FixedStepBatch Advance(long driverTick, int updatesPerSecond, int maxSteps)
    {
        if (updatesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(updatesPerSecond));
        if (maxSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSteps));

        long stepTicks = Math.Max(1, HighResTimer.TicksPerSecond / updatesPerSecond);
        long elapsedTicks = driverTick > _lastDriverTick
            ? driverTick - _lastDriverTick
            : 0;

        if (driverTick > _lastDriverTick)
            _lastDriverTick = driverTick;

        long maxAccumulatedTicks = stepTicks > long.MaxValue / maxSteps
            ? long.MaxValue
            : stepTicks * maxSteps;

        _accumulatedTicks = elapsedTicks >= maxAccumulatedTicks - _accumulatedTicks
            ? maxAccumulatedTicks
            : _accumulatedTicks + elapsedTicks;

        int stepCount = (int)Math.Min(maxSteps, _accumulatedTicks / stepTicks);
        _accumulatedTicks -= stepCount * stepTicks;

        long firstStepTick = SimulationTick + stepTicks;
        SimulationTick += stepCount * stepTicks;

        return new FixedStepBatch(firstStepTick, stepTicks, stepCount);
    }
}

/// <summary>Describes one bounded batch of fixed simulation steps.</summary>
/// <param name="FirstStepTick">The absolute simulation tick of the first step in the batch.</param>
/// <param name="StepTicks">The duration of one fixed step, in ticks.</param>
/// <param name="StepCount">The number of steps in the batch.</param>
internal readonly record struct FixedStepBatch(long FirstStepTick, long StepTicks, int StepCount)
{
    /// <summary>Gets the absolute simulation tick for the zero-based step index.</summary>
    /// <param name="index">The zero-based index of the step within the batch.</param>
    /// <returns>The absolute simulation tick of that step.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside the batch.
    /// </exception>
    internal long GetStepTick(int index)
    {
        if ((uint)index >= (uint)StepCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        return FirstStepTick + (index * StepTicks);
    }
}
