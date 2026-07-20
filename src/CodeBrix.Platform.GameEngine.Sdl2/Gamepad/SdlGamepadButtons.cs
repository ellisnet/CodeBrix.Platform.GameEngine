using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

/// <summary>
/// The button names reported by <see cref="SdlGamepadAdapter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine's gamepad seam identifies buttons by string, so these constants exist to keep games
/// from hard-coding string literals that a typo would silently break: a misspelled button name
/// registered with the event poller simply never fires.
/// </para>
/// <para>
/// The names follow the SDL2 standard controller layout, which is Xbox-style. On a controller with
/// different face-button labels the physical positions still map through SDL2's controller
/// database, so <see cref="A"/> is always the bottom face button regardless of what is printed on it.
/// </para>
/// </remarks>
public static class SdlGamepadButtons
{
    /// <summary>The bottom face button. Labelled A on an Xbox controller.</summary>
    public const string A = "A";

    /// <summary>The right face button. Labelled B on an Xbox controller.</summary>
    public const string B = "B";

    /// <summary>The left face button. Labelled X on an Xbox controller.</summary>
    public const string X = "X";

    /// <summary>The top face button. Labelled Y on an Xbox controller.</summary>
    public const string Y = "Y";

    /// <summary>The Back / View / Select button.</summary>
    public const string Back = "Back";

    /// <summary>The Guide button - the illuminated logo button in the centre.</summary>
    public const string Guide = "Guide";

    /// <summary>The Start / Menu button.</summary>
    public const string Start = "Start";

    /// <summary>The left thumbstick pressed inward.</summary>
    public const string LeftStick = "LeftStick";

    /// <summary>The right thumbstick pressed inward.</summary>
    public const string RightStick = "RightStick";

    /// <summary>The left shoulder bumper.</summary>
    public const string LeftShoulder = "LeftShoulder";

    /// <summary>The right shoulder bumper.</summary>
    public const string RightShoulder = "RightShoulder";

    /// <summary>Directional pad up.</summary>
    public const string DPadUp = "DPadUp";

    /// <summary>Directional pad down.</summary>
    public const string DPadDown = "DPadDown";

    /// <summary>Directional pad left.</summary>
    public const string DPadLeft = "DPadLeft";

    /// <summary>Directional pad right.</summary>
    public const string DPadRight = "DPadRight";

    /// <summary>
    /// Gets every button name this package can report, in SDL2 button order.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        A, B, X, Y,
        Back, Guide, Start,
        LeftStick, RightStick,
        LeftShoulder, RightShoulder,
        DPadUp, DPadDown, DPadLeft, DPadRight,
    ];
}
