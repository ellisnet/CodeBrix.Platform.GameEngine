using System;
using CodeBrix.Platform.GameEngine.Input.Actions;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>Tests for <see cref="InputBinding"/>: the factories, equality and log names.</summary>
public class InputBindingTests
{
    [Fact]
    public void equality_ignores_the_display_name() => InputBinding.Key(13, "Enter").Should().Be(InputBinding.Key(13));

    [Fact]
    public void different_inputs_are_not_equal()
    {
        //Arrange
        var up = InputBinding.StickPush(GamepadStick.Left, StickDirection.Up);

        //Act
        var others = new[] { InputBinding.StickPush(GamepadStick.Right, StickDirection.Up), InputBinding.StickPush(GamepadStick.Left, StickDirection.Down), InputBinding.DPad(StickDirection.Up) };

        //Assert
        foreach (var other in others)
            (up != other).Should().BeTrue();
    }

    [Theory]
    [InlineData(StickDirection.Up, "DPadUp")]
    [InlineData(StickDirection.Down, "DPadDown")]
    [InlineData(StickDirection.Left, "DPadLeft")]
    [InlineData(StickDirection.Right, "DPadRight")]
    public void DPad_is_the_matching_gamepad_button(StickDirection direction, string button)
    {
        //Arrange + Act
        var binding = InputBinding.DPad(direction);

        //Assert
        binding.Kind.Should().Be(InputBindingKind.GamepadButton);
        binding.Button.Should().Be(button);
        binding.Device.Should().Be(InputDeviceKind.Gamepad);
    }

    [Theory]
    [InlineData(StickDirection.None)]
    [InlineData(StickDirection.Up | StickDirection.Left)]
    public void a_stick_or_dpad_binding_needs_exactly_one_direction(StickDirection direction)
    {
        //Arrange + Act
        var stick = () => InputBinding.StickPush(GamepadStick.Left, direction);
        var dpad = () => InputBinding.DPad(direction);

        //Assert
        stick.Should().Throw<ArgumentOutOfRangeException>();
        dpad.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_empty_button_name_is_rejected() => ((Func<InputBinding>)(() => InputBinding.GamepadButton(""))).Should().Throw<ArgumentException>();

    [Fact]
    public void ToString_names_the_input_for_logs()
    {
        //Arrange + Act
        var names = new[]
        {
            InputBinding.Key(13, "Enter").ToString(),
            InputBinding.Key(32).ToString(),
            InputBinding.GamepadButton("A").ToString(),
            InputBinding.StickPush(GamepadStick.Right, StickDirection.Down).ToString(),
        };

        //Assert
        names.Should().Equal("key Enter", "key 32", "button A", "stick Right Down");
        InputBinding.Key(32).Device.Should().Be(InputDeviceKind.KeyboardMouse);
    }
}
