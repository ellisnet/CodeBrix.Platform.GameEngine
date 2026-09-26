using System;
using CodeBrix.Platform.GameEngine.Input.Actions;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>Tests for <see cref="InputBindingProfile"/>: binding, rebinding, copying and the key list a map claims.</summary>
public class InputBindingProfileTests
{
    [Fact]
    public void Bind_adds_bindings_once_and_keeps_the_action_order()
    {
        //Arrange
        var profile = new InputBindingProfile("Keys");

        //Act
        profile.Bind("Fire", InputBinding.Key(0x20)).Bind("Jump", InputBinding.Key(0x26)).Bind("Fire", InputBinding.Key(0x20, "Space"), InputBinding.GamepadButton("A"));

        //Assert
        profile.Actions.Should().Equal("Fire", "Jump");
        profile.GetBindings("Fire").Should().Equal(InputBinding.Key(0x20), InputBinding.GamepadButton("A"));
    }

    [Fact]
    public void Rebind_replaces_and_Unbind_removes()
    {
        //Arrange
        var profile = new InputBindingProfile("Keys").Bind("Fire", InputBinding.Key(0x20)).Bind("Jump", InputBinding.Key(0x26));

        //Act
        profile.Rebind("Fire", InputBinding.GamepadButton("RightShoulder"));
        profile.Unbind("Jump");

        //Assert
        profile.GetBindings("Fire").Should().Equal(InputBinding.GamepadButton("RightShoulder"));
        profile.GetBindings("Jump").Should().BeEmpty();
        profile.Actions.Should().Equal("Fire");
    }

    [Fact]
    public void Copy_is_independent_of_the_original()
    {
        //Arrange
        var classic = new InputBindingProfile("Classic").Bind("Fire", InputBinding.GamepadButton("A"));

        //Act
        var shoulder = classic.Copy("Shoulder").Rebind("Fire", InputBinding.GamepadButton("RightShoulder"));

        //Assert
        shoulder.Name.Should().Be("Shoulder");
        classic.GetBindings("Fire").Should().Equal(InputBinding.GamepadButton("A"));
        shoulder.GetBindings("Fire").Should().Equal(InputBinding.GamepadButton("RightShoulder"));
    }

    [Fact]
    public void KeyCodes_lists_each_bound_key_once()
    {
        //Arrange
        var profile = new InputBindingProfile("Keys")
            .Bind("Left", InputBinding.Key(0x25), InputBinding.StickPush(GamepadStick.Left, StickDirection.Left))
            .Bind("MenuLeft", InputBinding.Key(0x25), InputBinding.DPad(StickDirection.Left))
            .Bind("Fire", InputBinding.Key(0x20));

        //Act
        var keys = profile.KeyCodes;

        //Assert
        keys.Should().BeEquivalentTo(new[] { 0x25, 0x20 });
    }

    [Fact]
    public void an_empty_name_or_action_is_rejected()
    {
        //Arrange
        var profile = new InputBindingProfile("Keys");

        //Act
        var noName = () => new InputBindingProfile("");
        var noAction = () => profile.Bind("", InputBinding.Key(1));

        //Assert
        noName.Should().Throw<ArgumentException>();
        noAction.Should().Throw<ArgumentException>();
    }
}
