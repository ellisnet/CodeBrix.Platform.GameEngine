using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Sdl2.Tests;

/// <summary>
/// Validates the conversion of raw SDL2 axis readings into engine gamepad values.
/// </summary>
/// <remarks>
/// This is the part of the adapter that real hardware cannot be made to exercise on demand - a
/// stick cannot be asked to report exactly -32768 during a test run - so the conversions are tested
/// here with fabricated raw values instead.
/// </remarks>
public class SdlAxisConversionTests
{
    [Fact]
    public void ToStickState_treats_negative_raw_y_as_up()
    {
        //Arrange
        // SDL2 reports a stick pushed UP as a NEGATIVE vertical value, while the engine's
        // GamepadStickState defines +1 as up.
        const int rawUp = -16384;

        //Act
        var state = SdlAxisConversion.ToStickState(0, rawUp);

        //Assert
        state.Y.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ToStickState_treats_positive_raw_y_as_down()
        => SdlAxisConversion.ToStickState(0, 16384).Y.Should().BeLessThan(0f);

    [Fact]
    public void ToStickState_does_not_overflow_on_minimum_raw_y()
    {
        //Arrange
        // A freshly connected Bluetooth controller was observed reporting exactly this value on
        // every stick axis before its first HID report arrived. Inverting it naively as a signed
        // 16-bit value overflows, so this guards the widening in the conversion.
        const int rawMinimum = -32768;

        //Act
        var state = SdlAxisConversion.ToStickState(rawMinimum, rawMinimum);

        //Assert
        state.X.Should().Be(-1f);
        state.Y.Should().Be(1f);
    }

    [Fact]
    public void ToStickState_maps_maximum_raw_values_to_full_deflection()
    {
        //Act
        var state = SdlAxisConversion.ToStickState(32767, 32767);

        //Assert
        state.X.Should().Be(1f);
        state.Y.Should().Be(-1f);
    }

    [Fact]
    public void ToStickState_maps_centered_stick_to_zero()
    {
        //Act
        var state = SdlAxisConversion.ToStickState(0, 0);

        //Assert
        state.X.Should().Be(0f);
        state.Y.Should().Be(0f);
    }

    [Fact]
    public void ToStickState_leaves_resting_drift_inside_the_default_deadzone()
    {
        //Arrange
        // Real resting values observed from an idle Xbox Wireless Controller. They are not zero,
        // and a game must not read them as intent to move.
        const int restingX = 2909;
        const int restingY = 1325;

        //Act
        var state = SdlAxisConversion.ToStickState(restingX, restingY);

        //Assert
        state.IsEngaged().Should().BeFalse();
        state.Direction().Should().Be(StickDirection.None);
    }

    [Fact]
    public void ToStickState_preserves_the_raw_horizontal_value()
        => SdlAxisConversion.ToStickState(1234, 0).RawX.Should().Be(1234);

    [Fact]
    public void ToStickState_records_the_inverted_raw_vertical_value()
    {
        //Act
        var state = SdlAxisConversion.ToStickState(0, 1234);

        //Assert
        // The raw vertical value is stored already inverted, so that it stays consistent with the
        // normalized Y beside it rather than contradicting it.
        state.RawY.Should().Be(-1234);
    }

    [Fact]
    public void ToTriggerValue_maps_released_trigger_to_zero()
        => SdlAxisConversion.ToTriggerValue(0).Should().Be(0f);

    [Fact]
    public void ToTriggerValue_maps_fully_pressed_trigger_to_one()
        => SdlAxisConversion.ToTriggerValue(32767).Should().Be(1f);

    [Fact]
    public void ToTriggerValue_maps_half_pressed_trigger_to_about_half()
    {
        //Act
        var value = SdlAxisConversion.ToTriggerValue(16384);

        //Assert
        value.Should().BeGreaterThan(0.49f);
        value.Should().BeLessThan(0.51f);
    }

    [Fact]
    public void ToTriggerValue_treats_negative_raw_value_as_released()
    {
        //Arrange
        // SDL2's controller layer normalizes triggers to 0..32767, but the underlying joystick axis
        // is signed and rests at the minimum on some drivers. Were that to reach the conversion
        // unclamped, an untouched trigger would read as fully pressed.
        const short rawResting = -32768;

        //Act
        var value = SdlAxisConversion.ToTriggerValue(rawResting);

        //Assert
        value.Should().Be(0f);
    }
}
