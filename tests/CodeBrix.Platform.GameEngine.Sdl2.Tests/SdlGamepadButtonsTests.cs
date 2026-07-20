using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
using CodeBrix.Platform.GameEngine.Sdl2.Native;
using SilverAssertions;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Sdl2.Tests;

/// <summary>
/// Validates the button name table used to report pressed buttons.
/// </summary>
/// <remarks>
/// The adapter reads buttons by looping over SDL2's button indices and using each index to look up
/// a name in <see cref="SdlGamepadButtons.All"/>. That makes the ORDER of that list load-bearing:
/// were an entry inserted or moved, every button would silently report under a neighbour's name
/// rather than fail outright. These tests pin the ordering to the SDL2 enum.
/// </remarks>
public class SdlGamepadButtonsTests
{
    [Fact]
    public void All_is_ordered_to_match_the_sdl_button_enum()
    {
        //Arrange
        var expected = new[]
        {
            (SDL_GameControllerButton.A, SdlGamepadButtons.A),
            (SDL_GameControllerButton.B, SdlGamepadButtons.B),
            (SDL_GameControllerButton.X, SdlGamepadButtons.X),
            (SDL_GameControllerButton.Y, SdlGamepadButtons.Y),
            (SDL_GameControllerButton.Back, SdlGamepadButtons.Back),
            (SDL_GameControllerButton.Guide, SdlGamepadButtons.Guide),
            (SDL_GameControllerButton.Start, SdlGamepadButtons.Start),
            (SDL_GameControllerButton.LeftStick, SdlGamepadButtons.LeftStick),
            (SDL_GameControllerButton.RightStick, SdlGamepadButtons.RightStick),
            (SDL_GameControllerButton.LeftShoulder, SdlGamepadButtons.LeftShoulder),
            (SDL_GameControllerButton.RightShoulder, SdlGamepadButtons.RightShoulder),
            (SDL_GameControllerButton.DPadUp, SdlGamepadButtons.DPadUp),
            (SDL_GameControllerButton.DPadDown, SdlGamepadButtons.DPadDown),
            (SDL_GameControllerButton.DPadLeft, SdlGamepadButtons.DPadLeft),
            (SDL_GameControllerButton.DPadRight, SdlGamepadButtons.DPadRight),
        };

        //Act & Assert
        foreach ((SDL_GameControllerButton button, string name) in expected)
        {
            SdlGamepadButtons.All[(int)button].Should().Be(name);
        }
    }

    [Fact]
    public void All_covers_every_button_the_sdl_enum_defines()
        => SdlGamepadButtons.All.Count.Should().Be((int)SDL_GameControllerButton.Max);

    [Fact]
    public void All_contains_no_duplicate_names()
        => SdlGamepadButtons.All.Distinct(StringComparer.Ordinal).Count()
            .Should().Be(SdlGamepadButtons.All.Count);

    [Fact]
    public void All_contains_no_blank_names()
        => SdlGamepadButtons.All.Any(string.IsNullOrWhiteSpace).Should().BeFalse();
}
