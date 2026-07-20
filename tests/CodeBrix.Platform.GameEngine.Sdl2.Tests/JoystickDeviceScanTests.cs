using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Sdl2.Tests;

/// <summary>
/// Validates the parsing of /proc/bus/input/devices used to decide whether a joystick is attached
/// but unreadable.
/// </summary>
/// <remarks>
/// This backs the permission hint offered when SDL2 is working but sees no controllers. Getting it
/// wrong is worse than saying nothing: an earlier version asked only whether ANY /dev/input/event*
/// node was readable, which on an ordinary desktop is false whenever no controller is attached -
/// input nodes are mode 660 owned by group 'input', and a login session gets access to individual
/// devices through a per-device ACL rather than through group membership. That made the hint claim
/// a permissions fault in exactly the ordinary case, sending the user after a problem they did not
/// have. The rule now is narrower: only a device the kernel reports as a JOYSTICK, which cannot be
/// opened, justifies the advice.
/// </remarks>
public class JoystickDeviceScanTests
{
    // A real /proc/bus/input/devices excerpt: a laptop keyboard, a touchpad, and an Xbox
    // controller. Only the controller carries a "js" handler.
    private const string RealisticProcContents = """
        I: Bus=0011 Vendor=0001 Product=0001 Version=ab41
        N: Name="AT Translated Set 2 keyboard"
        P: Phys=isa0060/serio0/input0
        S: Sysfs=/devices/platform/i8042/serio0/input/input3
        U: Uniq=
        H: Handlers=sysrq kbd event3 leds
        B: PROP=0

        I: Bus=0018 Vendor=04f3 Product=0033 Version=0100
        N: Name="Elan Touchpad"
        P: Phys=i2c-ELAN0000:00
        S: Sysfs=/devices/pci0000:00/input/input8
        U: Uniq=
        H: Handlers=mouse0 event8
        B: PROP=5

        I: Bus=0005 Vendor=045e Product=0b20 Version=0517
        N: Name="Xbox Wireless Controller"
        P: Phys=98:59:7a:52:85:8b
        S: Sysfs=/devices/virtual/misc/uhid/0005:045E:0B20.0005/input/input27
        U: Uniq=ec:83:50:a6:d4:90
        H: Handlers=kbd event19 js0
        B: PROP=0
        """;

    [Fact]
    public void Finds_the_event_node_of_a_joystick()
        => SdlGamepadManager.ParseJoystickEventNodes(RealisticProcContents)
            .Should().BeEquivalentTo(["event19"]);

    [Fact]
    public void Ignores_devices_without_a_joystick_handler()
    {
        //Act
        var nodes = SdlGamepadManager.ParseJoystickEventNodes(RealisticProcContents);

        //Assert
        // The keyboard (event3) and touchpad (event8) must not be mistaken for controllers, or the
        // permission hint would key off entirely unrelated hardware.
        nodes.Should().NotContain("event3");
        nodes.Should().NotContain("event8");
    }

    [Fact]
    public void Returns_nothing_when_no_joystick_is_attached()
    {
        //Arrange
        const string noJoystick = """
            I: Bus=0011 Vendor=0001 Product=0001 Version=ab41
            N: Name="AT Translated Set 2 keyboard"
            H: Handlers=sysrq kbd event3 leds
            B: PROP=0
            """;

        //Act & Assert
        // This is the ordinary "controller is switched off" case, and it must NOT be reported as a
        // permissions problem.
        SdlGamepadManager.ParseJoystickEventNodes(noJoystick).Should().BeEmpty();
    }

    [Fact]
    public void Finds_every_joystick_when_several_are_attached()
    {
        //Arrange
        const string twoPads = """
            H: Handlers=kbd event19 js0
            B: PROP=0

            H: Handlers=event22 js1
            B: PROP=0
            """;

        //Act & Assert
        SdlGamepadManager.ParseJoystickEventNodes(twoPads)
            .Should().BeEquivalentTo(["event19", "event22"]);
    }

    [Fact]
    public void Does_not_treat_a_handler_merely_starting_with_js_as_a_joystick()
    {
        //Arrange
        // A joystick handler is "js" followed by a digit. Matching a bare "js" prefix would let an
        // unrelated handler name masquerade as a controller.
        const string notAJoystick = "H: Handlers=jsomething event5\n";

        //Act & Assert
        SdlGamepadManager.ParseJoystickEventNodes(notAJoystick).Should().BeEmpty();
    }

    [Fact]
    public void Ignores_a_joystick_block_that_names_no_event_node()
    {
        //Arrange
        // The legacy js interface without an evdev node: there is nothing for SDL2 to read, so
        // there is no readability conclusion to draw.
        const string joydevOnly = "H: Handlers=js0\n";

        //Act & Assert
        SdlGamepadManager.ParseJoystickEventNodes(joydevOnly).Should().BeEmpty();
    }

    [Fact]
    public void Handles_empty_input()
        => SdlGamepadManager.ParseJoystickEventNodes(string.Empty).Should().BeEmpty();

    [Fact]
    public void Handles_carriage_returns()
        => SdlGamepadManager.ParseJoystickEventNodes("H: Handlers=kbd event19 js0\r\nB: PROP=0\r\n")
            .Should().BeEquivalentTo(["event19"]);
}
