namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>Which analog stick of a gamepad an <see cref="InputBinding"/> or a stick read refers to.</summary>
public enum GamepadStick
{
    /// <summary>The left stick (<see cref="Gamepad.IGamepadAdapter.LeftStick"/>).</summary>
    Left = 0,

    /// <summary>The right stick (<see cref="Gamepad.IGamepadAdapter.RightStick"/>).</summary>
    Right = 1,
}
