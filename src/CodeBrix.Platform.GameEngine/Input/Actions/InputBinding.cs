using System;
using System.Globalization;
using CodeBrix.Platform.GameEngine.Input.Gamepad;

namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// One physical input bound to an action in an <see cref="InputBindingProfile"/>: a keyboard key, a gamepad
/// button (the D-pad directions included), or an analog stick pushed one way and read as a digital input.
/// </summary>
/// <remarks>
/// Two bindings are equal when they read the same input; <see cref="DisplayName"/> does not take part.
/// </remarks>
public readonly struct InputBinding : IEquatable<InputBinding>
{
    private InputBinding(InputBindingKind kind, int keyCode, string? button, GamepadStick stick, StickDirection direction, string? displayName)
    {
        Kind = kind;
        KeyCode = keyCode;
        Button = button;
        Stick = stick;
        Direction = direction;
        DisplayName = displayName;
    }

    /// <summary>The kind of input this binding reads.</summary>
    public InputBindingKind Kind { get; }

    /// <summary>The key code of a <see cref="InputBindingKind.Key"/> binding; 0 otherwise.</summary>
    public int KeyCode { get; }

    /// <summary>The button name of a <see cref="InputBindingKind.GamepadButton"/> binding; <see langword="null"/> otherwise.</summary>
    public string? Button { get; }

    /// <summary>The stick of a <see cref="InputBindingKind.StickDirection"/> binding.</summary>
    public GamepadStick Stick { get; }

    /// <summary>The single direction of a <see cref="InputBindingKind.StickDirection"/> binding; <see cref="StickDirection.None"/> otherwise.</summary>
    public StickDirection Direction { get; }

    /// <summary>An optional name for logs and prompts (for a key, "Enter" rather than its code).</summary>
    public string? DisplayName { get; }

    /// <summary>Binds a keyboard key.</summary>
    /// <param name="keyCode">The key code, as the keyboard adapter's <see cref="Keyboard.IKeyboardAdapter.IsDown"/> takes it
    /// (with the Host package, a <c>VirtualKey</c> value cast to <see cref="int"/>).</param>
    /// <param name="displayName">An optional name for logs and prompts.</param>
    /// <returns>The binding.</returns>
    public static InputBinding Key(int keyCode, string? displayName = null) =>
        new(InputBindingKind.Key, keyCode, null, GamepadStick.Left, StickDirection.None, displayName);

    /// <summary>Binds a gamepad button, held on ANY connected gamepad.</summary>
    /// <param name="button">The button name as the gamepad adapter reports it in
    /// <see cref="IGamepadAdapter.PressedButtons"/> (with the SDL2 add-in, an <c>SdlGamepadButtons</c> constant).</param>
    /// <returns>The binding.</returns>
    /// <exception cref="ArgumentException"><paramref name="button"/> is null or empty.</exception>
    public static InputBinding GamepadButton(string button)
    {
        ArgumentException.ThrowIfNullOrEmpty(button);
        return new InputBinding(InputBindingKind.GamepadButton, 0, button, GamepadStick.Left, StickDirection.None, null);
    }

    /// <summary>
    /// Binds one D-pad direction: a <see cref="GamepadButton"/> binding named "DPadUp", "DPadDown", "DPadLeft" or
    /// "DPadRight" - the names the SDL2 add-in's gamepad adapter reports.
    /// </summary>
    /// <param name="direction">Exactly one of <see cref="StickDirection.Up"/>, <see cref="StickDirection.Down"/>,
    /// <see cref="StickDirection.Left"/> or <see cref="StickDirection.Right"/>.</param>
    /// <returns>The binding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a single direction.</exception>
    public static InputBinding DPad(StickDirection direction) => GamepadButton(direction switch
    {
        StickDirection.Up => "DPadUp",
        StickDirection.Down => "DPadDown",
        StickDirection.Left => "DPadLeft",
        StickDirection.Right => "DPadRight",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Use exactly one of Up, Down, Left or Right."),
    });

    /// <summary>
    /// Binds an analog stick pushed one way, read as a digital input on ANY connected gamepad: on past
    /// <see cref="InputActionMap.StickPressThreshold"/>, off again inside <see cref="InputActionMap.StickReleaseThreshold"/>,
    /// with the settle window <see cref="InputActionMap.StickSettleSeconds"/> after a release.
    /// </summary>
    /// <param name="stick">The stick.</param>
    /// <param name="direction">Exactly one of <see cref="StickDirection.Up"/>, <see cref="StickDirection.Down"/>,
    /// <see cref="StickDirection.Left"/> or <see cref="StickDirection.Right"/>.</param>
    /// <returns>The binding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a single direction.</exception>
    public static InputBinding StickPush(GamepadStick stick, StickDirection direction)
    {
        if (direction is not (StickDirection.Up or StickDirection.Down or StickDirection.Left or StickDirection.Right))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Use exactly one of Up, Down, Left or Right.");
        }

        return new InputBinding(InputBindingKind.StickDirection, 0, null, stick, direction, null);
    }

    /// <summary>The device kind this binding belongs to.</summary>
    public InputDeviceKind Device => Kind == InputBindingKind.Key ? InputDeviceKind.KeyboardMouse : InputDeviceKind.Gamepad;

    /// <summary>A short description for logs: "key Enter", "button A", "stick Left Up".</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Kind switch
    {
        InputBindingKind.Key => $"key {DisplayName ?? KeyCode.ToString(CultureInfo.InvariantCulture)}",
        InputBindingKind.GamepadButton => $"button {DisplayName ?? Button}",
        _ => $"stick {Stick} {Direction}",
    };

    /// <inheritdoc />
    public bool Equals(InputBinding other) =>
        Kind == other.Kind && KeyCode == other.KeyCode && string.Equals(Button, other.Button, StringComparison.Ordinal) &&
        Stick == other.Stick && Direction == other.Direction;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InputBinding other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, KeyCode, Button, Stick, Direction);

    /// <summary>Returns whether two bindings read the same input.</summary>
    /// <param name="left">The first binding.</param>
    /// <param name="right">The second binding.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public static bool operator ==(InputBinding left, InputBinding right) => left.Equals(right);

    /// <summary>Returns whether two bindings read different inputs.</summary>
    /// <param name="left">The first binding.</param>
    /// <param name="right">The second binding.</param>
    /// <returns><see langword="true"/> if they differ.</returns>
    public static bool operator !=(InputBinding left, InputBinding right) => !left.Equals(right);
}
