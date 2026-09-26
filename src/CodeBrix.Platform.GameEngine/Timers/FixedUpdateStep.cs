namespace CodeBrix.Platform.GameEngine.Timers; //CodeBrix (not from Gondwana)

/// <summary>
/// Describes one step of the engine's fixed-step update hook, as passed to
/// <see cref="Engine.FixedUpdate"/> (see <see cref="Configuration.EngineConfiguration.FixedUpdateRate"/>).
/// </summary>
/// <param name="StepNumber">
/// The running number of this step, starting at one for the first step the engine ever raised and
/// never reset (not by a rate change, a pause or a restart), so it can seed deterministic logic.
/// </param>
/// <param name="DeltaSeconds">The fixed duration of the step, in seconds (<c>1 / FixedUpdateRate</c>).</param>
/// <param name="Tick">
/// The simulation tick (<see cref="HighResTimer"/> ticks) the step stands for. Steps within one cycle
/// are one step apart, even though they run back to back.
/// </param>
/// <param name="IndexInCycle">The zero-based position of this step among the steps of the current cycle.</param>
/// <param name="StepsInCycle">
/// The number of steps due in the current cycle. Fewer may run if a handler pauses or stops the engine.
/// </param>
public readonly record struct FixedUpdateStep(
    long StepNumber,
    double DeltaSeconds,
    long Tick,
    int IndexInCycle,
    int StepsInCycle)
{
    /// <summary>Gets a value indicating whether this is the last step due in the current cycle.</summary>
    public bool IsLastInCycle => IndexInCycle == StepsInCycle - 1;
}
