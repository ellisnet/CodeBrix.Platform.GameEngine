using CodeBrix.Platform.GameEngine.Input.Gamepad;
using System;

namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

/// <summary>
/// Converts raw SDL2 axis readings into the engine's gamepad value types.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="SdlGamepadAdapter"/> so that it can be exercised directly with
/// fabricated raw values. The adapter itself can only be driven by real hardware, but this is
/// where the conversions that are actually easy to get wrong live - axis inversion and the
/// asymmetric range of a signed 16-bit value - so it is worth being able to test them in isolation.
/// </remarks>
internal static class SdlAxisConversion
{
    private const float AxisRange = 32767f;

    /// <summary>
    /// Converts a raw SDL2 stick reading into a <see cref="GamepadStickState"/>.
    /// </summary>
    /// <param name="rawX">The raw horizontal axis value, from -32768 to 32767.</param>
    /// <param name="rawY">The raw vertical axis value, from -32768 to 32767. Negative is up.</param>
    /// <returns>The converted stick state, with the vertical axis inverted.</returns>
    /// <remarks>
    /// SDL2 reports a stick pushed UP as NEGATIVE, whereas <see cref="GamepadStickState"/> defines
    /// +1 as up, so the vertical axis is inverted here.
    /// <para>
    /// Both parameters are <see cref="int"/> rather than <see cref="short"/> on purpose. Negating a
    /// raw value of -32768 overflows a signed 16-bit type, and -32768 is a value a real stick does
    /// report; widening first makes the inversion safe, and the clamping inside
    /// <see cref="GamepadStickState.FromRaw16"/> brings the result back into range.
    /// </para>
    /// </remarks>
    internal static GamepadStickState ToStickState(int rawX, int rawY)
        => GamepadStickState.FromRaw16(rawX, -rawY);

    /// <summary>
    /// Converts a raw SDL2 trigger reading into a 0.0 to 1.0 pressure value.
    /// </summary>
    /// <param name="raw">The raw trigger value. SDL2 normalizes triggers to 0 through 32767.</param>
    /// <returns>The trigger pressure, from 0.0 (released) to 1.0 (fully pressed).</returns>
    /// <remarks>
    /// Negative readings are treated as zero. SDL2's game controller layer normalizes triggers to a
    /// non-negative range, but the underlying joystick axis is signed and rests at -32768 on some
    /// drivers, so clamping the low end costs nothing and guards against a trigger that would
    /// otherwise read as fully pressed while untouched.
    /// </remarks>
    internal static float ToTriggerValue(short raw)
        => raw <= 0 ? 0f : Math.Clamp(raw / AxisRange, 0f, 1f);
}
