using System;

namespace KenneyAssetsDemo.Game;

/// <summary>
/// Writes the demo's start-up report to the console, one line per loading step, every line carrying the
/// same fixed prefix.
/// </summary>
/// <remarks>
/// The report goes to the console rather than to the engine logger because it is the demo's own answer
/// to "what did the asset provider actually give me?", and because a fixed prefix makes the run
/// checkable without a screenshot: every line starts with <c>[KenneyAssetsDemo]</c>, a problem the demo
/// worked around says <c>WARNING</c>, a problem it could not work around says <c>FAILED</c>, and the
/// last line of a good run is <c>READY</c>.
/// </remarks>
internal static class DemoLog
{
    /// <summary>The prefix every line of the report carries.</summary>
    internal const string Prefix = "[KenneyAssetsDemo]";

    /// <summary>Writes one line of the report.</summary>
    /// <param name="message">The line, without the prefix.</param>
    internal static void Write(string message) => Console.WriteLine($"{Prefix} {message}");

    /// <summary>Writes the line that says the demo is loaded and playable. Always the last line.</summary>
    internal static void Ready() => Write("READY");

    /// <summary>
    /// Runs a loading step the demo can do without, and reports a failure instead of letting it stop the
    /// demo.
    /// </summary>
    /// <param name="step">What the step is, for the report.</param>
    /// <param name="action">The step.</param>
    /// <returns>
    /// <see langword="true"/> when the step succeeded; <see langword="false"/> when it failed and was
    /// reported.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
    internal static bool TryStep(string step, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();

            return true;
        }
        catch (Exception failure)
        {
            //Missing sound, a font that will not read, an icon that will not rasterize: the demo is
            //  still worth looking at without them, so say what was lost and carry on.
            Write($"WARNING: {step}: {failure.GetType().Name}: {failure.Message}");

            return false;
        }
    }

    /// <summary>
    /// Runs a loading step the demo cannot do without, reports a failure and lets it through.
    /// </summary>
    /// <param name="step">What the step is, for the report.</param>
    /// <param name="action">The step.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
    internal static void Step(string step, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception failure)
        {
            //No packs, no map or no character means there is no demo, so the report says so in the
            //  same shape as everything else and then the exception goes where it was going.
            Write($"FAILED: {step}: {failure.GetType().Name}: {failure.Message}");

            throw;
        }
    }
}
