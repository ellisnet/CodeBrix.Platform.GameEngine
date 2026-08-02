using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// The gain law a fade or crossfade follows.
/// </summary>
public enum MusicFadeCurve
{
    /// <summary>
    /// Constant power: the two sides of a crossfade sum to a steady loudness throughout. The
    /// default, and the right choice for a crossfade between two unrelated pieces of music.
    /// </summary>
    EqualPower = 0,

    /// <summary>
    /// Straight-line gain. Correct for a single fade in or out, but a crossfade using it DIPS
    /// audibly in the middle — at the halfway point both sides are at 0.5, which is about 6 dB down
    /// rather than the 3 dB that sounds level. Choose it for two copies of the SAME material
    /// (a stem swap, a loop splice), where the signals are correlated and linear is what stays flat.
    /// </summary>
    Linear = 1,
}

/// <summary>Evaluates <see cref="MusicFadeCurve"/> gains.</summary>
internal static class MusicFadeCurves
{
    /// <summary>The gain a curve gives at normalized progress <paramref name="t"/>.</summary>
    /// <param name="curve">The curve to evaluate.</param>
    /// <param name="t">Progress from 0.0 (silent) to 1.0 (full).</param>
    /// <returns>The gain, 0.0 to 1.0.</returns>
    internal static float GainAt(MusicFadeCurve curve, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return curve == MusicFadeCurve.Linear
            ? t
            : MathF.Sin(t * MathF.PI * 0.5f);
    }
}
