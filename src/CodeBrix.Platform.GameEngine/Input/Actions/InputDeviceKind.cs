namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// The kind of device that produced the player's most recent input (<see cref="InputActionMap.LastDevice"/>),
/// so on-screen prompts can show keyboard keys or gamepad buttons.
/// </summary>
public enum InputDeviceKind
{
    /// <summary>The keyboard (and the mouse).</summary>
    KeyboardMouse = 0,

    /// <summary>A gamepad.</summary>
    Gamepad = 1,
}
