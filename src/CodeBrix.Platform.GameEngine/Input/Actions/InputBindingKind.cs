namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>The kind of physical input an <see cref="InputBinding"/> reads.</summary>
public enum InputBindingKind
{
    /// <summary>A keyboard key, by key code (<see cref="Keyboard.IKeyboardAdapter.IsDown"/>).</summary>
    Key = 0,

    /// <summary>
    /// A gamepad button, by the name the gamepad adapter reports in
    /// <see cref="Gamepad.IGamepadAdapter.PressedButtons"/> (the D-pad directions are buttons too).
    /// </summary>
    GamepadButton = 1,

    /// <summary>An analog stick pushed in one direction, read as a digital (on / off) input.</summary>
    StickDirection = 2,
}
