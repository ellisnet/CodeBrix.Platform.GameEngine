using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CodeBrix.Platform.GameEngine.Input.Actions;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="InputActionMap"/>: actions over keys, gamepad buttons, D-pad and stick directions; press and
/// release edges latched between steps; hold-to-repeat; the stick's hysteresis and settle window; held-at-start
/// suppression; last device; every connected gamepad aggregated; swappable profiles; key claims; engine attach.
/// Devices are fakes of <see cref="IKeyboardAdapter"/> and <see cref="IGamepadAdapter"/>.
/// </summary>
public class InputActionMapTests
{
    private const double Step = 1.0 / 60;
    private const double Delay = 0.35;
    private const double Interval = 0.1;

    private const int KeyEnter = 0x0D;
    private const int KeyShift = 0x10;
    private const int KeyEscape = 0x1B;
    private const int KeySpace = 0x20;
    private const int KeyLeft = 0x25;
    private const int KeyUp = 0x26;
    private const int KeyRight = 0x27;
    private const int KeyDown = 0x28;
    private const int KeyA = 0x41;
    private const int KeyD = 0x44;
    private const int KeyLeftShift = 0xA0;

    private readonly FakeKeyboard _keyboard = new();
    private readonly List<FakePad> _pads = new();
    private bool _padListThrows;

    private static InputBindingProfile ShooterProfile() => new InputBindingProfile("Classic")
        .Bind("MoveLeft", InputBinding.Key(KeyLeft), InputBinding.Key(KeyA), InputBinding.DPad(StickDirection.Left))
        .Bind("MoveRight", InputBinding.Key(KeyRight), InputBinding.Key(KeyD), InputBinding.DPad(StickDirection.Right))
        .Bind("Fire", InputBinding.Key(KeySpace, "Space"), InputBinding.GamepadButton("A"))
        .Bind("Bomb", InputBinding.Key(KeyLeftShift), InputBinding.Key(KeyShift), InputBinding.GamepadButton("B"))
        .Bind("MenuUp", InputBinding.Key(KeyUp, "Up"), InputBinding.DPad(StickDirection.Up), InputBinding.StickPush(GamepadStick.Left, StickDirection.Up))
        .Bind("MenuDown", InputBinding.Key(KeyDown), InputBinding.DPad(StickDirection.Down), InputBinding.StickPush(GamepadStick.Left, StickDirection.Down))
        .Bind("MenuLeft", InputBinding.Key(KeyLeft), InputBinding.DPad(StickDirection.Left), InputBinding.StickPush(GamepadStick.Left, StickDirection.Left))
        .Bind("MenuRight", InputBinding.Key(KeyRight), InputBinding.DPad(StickDirection.Right), InputBinding.StickPush(GamepadStick.Left, StickDirection.Right))
        .Bind("Confirm", InputBinding.Key(KeyEnter, "Enter"), InputBinding.GamepadButton("A"))
        .Bind("Back", InputBinding.Key(KeyEscape), InputBinding.GamepadButton("B"))
        .Bind("Pause", InputBinding.Key(KeyEscape), InputBinding.GamepadButton("Start"));

    private InputActionMap Create(InputBindingProfile? profile = null)
    {
        var map = new InputActionMap(profile ?? ShooterProfile(), () => _keyboard, () =>
        {
            if (_padListThrows)
                throw new InvalidOperationException("Collection was modified.");
            return _pads;
        });
        map.SetRepeat("MenuUp", new InputRepeat(Delay, Interval));
        map.SetRepeat("MenuDown", new InputRepeat(Delay, Interval));
        map.SetRepeat("MenuLeft", new InputRepeat(Delay, Interval));
        map.SetRepeat("MenuRight", new InputRepeat(Delay, Interval));
        return map;
    }

    private InputActionMap Started(InputBindingProfile? profile = null)
    {
        var map = Create(profile);
        map.Update(0);
        return map;
    }

    private FakePad Pad()
    {
        var pad = new FakePad($"pad{_pads.Count}");
        _pads.Add(pad);
        return pad;
    }

    /// <summary>Keeps the current device state for a while, one step at a time; counts the steps in which the action triggered.</summary>
    private static int Hold(InputActionMap map, string action, double seconds)
    {
        var count = 0;
        var steps = (int)Math.Round(seconds / Step);
        for (var i = 0; i < steps; i++)
        {
            map.Update(Step);
            if (map.IsTriggered(action))
                count++;
        }

        return count;
    }

    [Fact]
    public void an_unknown_action_reads_as_idle()
    {
        //Arrange
        var map = Started();

        //Act
        _keyboard.Press(KeySpace);
        map.Update(Step);

        //Assert
        map.IsHeld("NoSuchAction").Should().BeFalse();
        map.WasPressed("NoSuchAction").Should().BeFalse();
        map.IsTriggered("NoSuchAction").Should().BeFalse();
    }

    [Fact]
    public void nothing_is_held_before_the_first_update() => Create().IsHeld("Fire").Should().BeFalse();

    [Fact]
    public void a_key_press_is_held_pressed_once_and_released_once()
    {
        //Arrange
        var map = Started();

        //Act
        _keyboard.Press(KeySpace);
        map.Update(Step);
        var first = (map.IsHeld("Fire"), map.WasPressed("Fire"), map.WasReleased("Fire"));
        map.Update(Step);
        var second = (map.IsHeld("Fire"), map.WasPressed("Fire"), map.WasReleased("Fire"));
        _keyboard.Release(KeySpace);
        map.Update(Step);
        var third = (map.IsHeld("Fire"), map.WasPressed("Fire"), map.WasReleased("Fire"));

        //Assert
        first.Should().Be((true, true, false));
        second.Should().Be((true, false, false));
        third.Should().Be((false, false, true));
    }

    [Fact]
    public void a_key_tap_between_two_steps_is_latched_for_the_next_step_only()
    {
        //Arrange
        var map = Started();

        //Act - the engine polls twice inside one step: down, then up again
        _keyboard.Press(KeyEnter);
        map.Poll(0.002);
        _keyboard.Release(KeyEnter);
        map.Poll(0.002);
        map.Update(Step);
        var step = (map.IsHeld("Confirm"), map.WasPressed("Confirm"), map.WasReleased("Confirm"));
        map.Update(Step);
        var next = (map.IsHeld("Confirm"), map.WasPressed("Confirm"));

        //Assert
        step.Should().Be((true, true, true));
        next.Should().Be((false, false));
    }

    [Fact]
    public void a_gamepad_button_tap_between_two_steps_is_latched()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        pad.Press("A");
        map.Poll(0.008);
        pad.Release("A");
        map.Poll(0.008);
        map.Update(Step);

        //Assert
        map.WasPressed("Fire").Should().BeTrue();
        map.IsHeld("Fire").Should().BeTrue();
        map.WasPressed("Confirm").Should().BeTrue();
    }

    [Fact]
    public void keyboard_and_gamepad_work_at_the_same_time()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        _keyboard.Press(KeyRight);
        pad.Press("A");
        map.Update(Step);

        //Assert
        map.IsHeld("MoveRight").Should().BeTrue();
        map.IsHeld("Fire").Should().BeTrue();
    }

    [Fact]
    public void an_input_held_when_the_map_starts_does_not_count_until_released()
    {
        //Arrange
        _keyboard.Press(KeyEnter);
        var map = Started();

        //Act
        map.Update(Step);
        var whileHeld = (map.IsHeld("Confirm"), map.WasPressed("Confirm"));
        _keyboard.Release(KeyEnter);
        map.Update(Step);
        var released = map.WasReleased("Confirm");
        _keyboard.Press(KeyEnter);
        map.Update(Step);

        //Assert
        whileHeld.Should().Be((false, false));
        released.Should().BeFalse();
        map.WasPressed("Confirm").Should().BeTrue();
    }

    [Fact]
    public void SuppressHeld_makes_a_held_action_wait_for_its_release()
    {
        //Arrange
        var map = Started();
        _keyboard.Press(KeySpace);
        map.Update(Step);

        //Act - a new screen starts
        map.SuppressHeld();
        map.Update(Step);
        var suppressed = map.IsHeld("Fire");
        _keyboard.Release(KeySpace);
        map.Update(Step);
        _keyboard.Press(KeySpace);
        map.Update(Step);

        //Assert
        suppressed.Should().BeFalse();
        map.WasPressed("Fire").Should().BeTrue();
    }

    [Fact]
    public void SuppressHeld_drops_a_press_still_latched()
    {
        //Arrange
        var map = Started();
        _keyboard.Press(KeyEnter);
        map.Poll(0.001);

        //Act
        map.SuppressHeld();
        map.Update(Step);

        //Assert
        map.WasPressed("Confirm").Should().BeFalse();
        map.IsHeld("Confirm").Should().BeFalse();
    }

    [Fact]
    public void holding_one_action_on_two_devices_acts_once_but_the_last_device_follows_the_second()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        _keyboard.Press(KeyLeft);
        map.Update(Step);

        //Act
        pad.Press("DPadLeft");
        map.Update(Step);

        //Assert
        map.IsTriggered("MenuLeft").Should().BeFalse();
        map.WasPressed("MoveLeft").Should().BeFalse();
        map.LastDevice.Should().Be(InputDeviceKind.Gamepad);
    }

    [Fact]
    public void LastDevice_follows_the_device_that_pressed_last()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        pad.Press("A");
        map.Update(Step);
        var afterPad = map.LastDevice;
        _keyboard.Press(KeySpace);
        map.Update(Step);
        var afterKey = map.LastDevice;
        pad.LeftStick = new GamepadStickState(0.9f, 0f);
        map.Update(Step);
        var afterStick = map.LastDevice;
        map.NoteDeviceUsed(InputDeviceKind.KeyboardMouse);

        //Assert
        afterPad.Should().Be(InputDeviceKind.Gamepad);
        afterKey.Should().Be(InputDeviceKind.KeyboardMouse);
        afterStick.Should().Be(InputDeviceKind.Gamepad);
        map.LastDevice.Should().Be(InputDeviceKind.KeyboardMouse);
    }

    [Fact]
    public void a_hold_shorter_than_the_repeat_delay_acts_once_on_every_device()
    {
        //Arrange
        var pad = Pad();
        var keyMap = Started();

        //Act
        _keyboard.Press(KeyUp);
        var byKey = Hold(keyMap, "MenuUp", Delay - (2 * Step));
        _keyboard.Release(KeyUp);
        var dpadMap = Started();
        pad.Press("DPadUp");
        var byDPad = Hold(dpadMap, "MenuUp", Delay - (2 * Step));
        pad.Release("DPadUp");
        var stickMap = Started();
        pad.LeftStick = new GamepadStickState(0f, 1f);
        var byStick = Hold(stickMap, "MenuUp", Delay - (2 * Step));

        //Assert
        byKey.Should().Be(1);
        byDPad.Should().Be(1);
        byStick.Should().Be(1);
    }

    [Fact]
    public void a_hold_past_the_delay_repeats_at_the_interval()
    {
        //Arrange
        var map = Started();

        //Act - the press, then 1 s past the delay at 0.1 s = 10 repeats
        _keyboard.Press(KeyDown);
        var count = Hold(map, "MenuDown", Delay + 1.0 - (Step / 2));

        //Assert
        count.Should().BeInRange(10, 12);
    }

    [Fact]
    public void a_stick_hold_past_the_delay_repeats_at_the_interval()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        pad.LeftStick = new GamepadStickState(0f, 1f);
        var count = Hold(map, "MenuUp", Delay + 1.0 - (Step / 2));

        //Assert
        count.Should().BeInRange(10, 12);
    }

    [Fact]
    public void SetRepeat_changes_the_interval_of_a_running_hold()
    {
        //Arrange
        var menus = Started();
        var slower = Started();
        slower.SetRepeat("MenuDown", new InputRepeat(Delay, 0.12));

        //Act
        _keyboard.Press(KeyDown);
        var menuCount = Hold(menus, "MenuDown", Delay + 1.2);
        var slowerCount = Hold(slower, "MenuDown", Delay + 1.2);

        //Assert
        menuCount.Should().BeInRange(12, 14);
        slowerCount.Should().BeInRange(10, 12);
        slowerCount.Should().BeLessThan(menuCount);
    }

    [Fact]
    public void an_action_without_repeat_timing_triggers_only_on_the_press()
    {
        //Arrange
        var map = Started();

        //Act
        _keyboard.Press(KeyEnter);
        var count = Hold(map, "Confirm", 2.0);

        //Assert
        count.Should().Be(1);
    }

    [Fact]
    public void releasing_resets_the_repeat_clock()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.Press("DPadRight");
        Hold(map, "MenuRight", Delay + 0.5);

        //Act
        pad.Release("DPadRight");
        var released = Hold(map, "MenuRight", 0.2);
        pad.Press("DPadRight");
        var again = Hold(map, "MenuRight", Delay - (2 * Step));

        //Assert
        released.Should().Be(0);
        again.Should().Be(1);
    }

    [Fact]
    public void a_long_stall_gives_one_repeat_not_a_burst()
    {
        //Arrange
        var map = Started();
        _keyboard.Press(KeyDown);
        map.Update(Step);

        //Act
        map.Update(2.0);
        var afterStall = map.IsTriggered("MenuDown");
        map.Update(Interval / 2);
        var halfInterval = map.IsTriggered("MenuDown");
        map.Update((Interval / 2) + 0.001);

        //Assert
        afterStall.Should().BeTrue();
        halfInterval.Should().BeFalse();
        map.IsTriggered("MenuDown").Should().BeTrue();
    }

    [Fact]
    public void the_stick_acts_once_per_push_and_rearms_when_released()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        map.ClearRepeat("MenuUp");

        //Act
        pad.LeftStick = new GamepadStickState(0f, 0.9f);
        map.Update(Step);
        var pushed = map.IsTriggered("MenuUp");
        pad.LeftStick = new GamepadStickState(0f, 0.95f);
        map.Update(Step);
        var held = map.IsTriggered("MenuUp");
        pad.LeftStick = new GamepadStickState(0f, 0.1f);
        map.Update(Step);
        map.Update(0.2);
        pad.LeftStick = new GamepadStickState(0f, 0.8f);
        map.Update(Step);

        //Assert
        pushed.Should().BeTrue();
        held.Should().BeFalse();
        map.IsTriggered("MenuUp").Should().BeTrue();
    }

    [Theory]
    [InlineData(0f, 1f, "MenuUp")]
    [InlineData(0f, -0.55f, "MenuDown")]
    [InlineData(-1f, 0f, "MenuLeft")]
    [InlineData(0.55f, 0f, "MenuRight")]
    public void the_stick_directions_are_up_positive_and_right_positive(float x, float y, string expected)
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        pad.LeftStick = new GamepadStickState(x, y);
        map.Update(Step);

        //Assert
        foreach (var action in new[] { "MenuUp", "MenuDown", "MenuLeft", "MenuRight" })
            map.WasPressed(action).Should().Be(action == expected);
        map.PressedBindings.Select(binding => binding.ToString()).Should().Contain($"stick Left {expected[4..]}");
    }

    [Fact]
    public void the_stick_turns_off_only_inside_the_release_threshold()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.LeftStick = new GamepadStickState(0f, 0.9f);
        map.Update(Step);

        //Act
        pad.LeftStick = new GamepadStickState(0f, 0.4f);
        map.Update(Step);
        var between = map.IsHeld("MenuUp");
        pad.LeftStick = new GamepadStickState(0f, 0.25f);
        map.Update(Step);

        //Assert
        between.Should().BeTrue();
        map.IsHeld("MenuUp").Should().BeFalse();
        map.WasReleased("MenuUp").Should().BeTrue();
    }

    [Theory]
    [InlineData(0.9f, -0.7f)]
    [InlineData(-0.9f, 0.7f)]
    public void a_released_stick_springing_back_past_centre_is_not_a_push_the_other_way(float pushed, float overshoot)
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.LeftStick = new GamepadStickState(0f, pushed);
        map.Update(Step);

        //Act - released: the stick snaps back through centre and overshoots for a few milliseconds
        pad.LeftStick = new GamepadStickState(0f, 0f);
        map.Poll(0.004);
        pad.LeftStick = new GamepadStickState(0f, overshoot);
        map.Poll(0.004);
        pad.LeftStick = new GamepadStickState(0f, overshoot / 3);
        map.Poll(0.004);
        pad.LeftStick = new GamepadStickState(0f, -0.03f);
        map.Update(0.004);

        //Assert
        map.WasPressed("MenuUp").Should().BeFalse();
        map.WasPressed("MenuDown").Should().BeFalse();
    }

    [Fact]
    public void a_deliberate_push_the_other_way_acts_once_the_stick_has_settled()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.LeftStick = new GamepadStickState(0f, 0.9f);
        map.Update(Step);
        pad.LeftStick = new GamepadStickState(0f, 0f);
        map.Update(Step);

        //Act
        pad.LeftStick = new GamepadStickState(0f, -0.9f);
        map.Update(0.01);
        var early = map.WasPressed("MenuDown");
        map.Update(map.StickSettleSeconds);

        //Assert
        early.Should().BeFalse();
        map.WasPressed("MenuDown").Should().BeTrue();
    }

    [Fact]
    public void a_stick_already_pushed_when_the_map_starts_neither_presses_nor_repeats()
    {
        //Arrange
        var pad = Pad();
        pad.LeftStick = new GamepadStickState(0f, 1f);
        var map = Started();

        //Act
        var held = Hold(map, "MenuUp", 1.0);
        pad.LeftStick = new GamepadStickState(0f, 0f);
        map.Update(Step);
        map.Update(0.2);
        pad.LeftStick = new GamepadStickState(0f, 1f);
        var afterRelease = Hold(map, "MenuUp", Step);

        //Assert
        held.Should().Be(0);
        afterRelease.Should().Be(1);
    }

    [Fact]
    public void a_button_on_any_connected_gamepad_counts()
    {
        //Arrange
        Pad();
        var second = Pad();
        var map = Started();

        //Act
        second.Press("B");
        map.Update(Step);

        //Assert
        map.WasPressed("Bomb").Should().BeTrue();
    }

    [Fact]
    public void the_stick_pushed_furthest_over_all_gamepads_wins()
    {
        //Arrange
        var resting = Pad();
        var pushed = Pad();
        var map = Started();

        //Act
        resting.LeftStick = new GamepadStickState(0.05f, 0f);
        pushed.LeftStick = new GamepadStickState(-0.6f, 0f);
        map.Update(Step);

        //Assert
        map.GetStick(GamepadStick.Left).X.Should().BeApproximately(-0.6f, 1e-6f);
        map.WasPressed("MenuLeft").Should().BeTrue();
    }

    [Fact]
    public void a_gamepad_unplugged_while_a_button_is_held_releases_it()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.Press("A");
        map.Update(Step);

        //Act
        _pads.Clear();
        map.Update(Step);

        //Assert
        map.WasReleased("Fire").Should().BeTrue();
        map.IsHeld("Fire").Should().BeFalse();
    }

    [Fact]
    public void a_gamepad_list_that_changes_while_read_keeps_the_last_state()
    {
        //Arrange
        var pad = Pad();
        var map = Started();
        pad.Press("A");
        map.Update(Step);

        //Act
        _padListThrows = true;
        map.Update(Step);
        var duringFailure = map.IsHeld("Fire");
        _padListThrows = false;
        map.Update(Step);

        //Assert
        duringFailure.Should().BeTrue();
        map.IsHeld("Fire").Should().BeTrue();
        map.WasPressed("Fire").Should().BeFalse();
    }

    [Fact]
    public void no_gamepads_and_no_keyboard_read_as_idle()
    {
        //Arrange
        var map = new InputActionMap(ShooterProfile(), () => null, () => null);

        //Act
        map.Update(Step);
        map.Update(Step);

        //Assert
        map.IsHeld("Fire").Should().BeFalse();
        map.GetStick(GamepadStick.Right).Magnitude.Should().Be(0f);
    }

    [Fact]
    public void swapping_the_profile_swaps_the_controls()
    {
        //Arrange
        var pad = Pad();
        var classic = ShooterProfile();
        var shoulder = classic.Copy("Shoulder")
            .Rebind("Fire", InputBinding.Key(KeySpace), InputBinding.GamepadButton("RightShoulder"))
            .Rebind("Bomb", InputBinding.Key(KeyLeftShift), InputBinding.GamepadButton("LeftShoulder"));
        var map = Started(classic);

        //Act
        map.Profile = shoulder;
        map.Update(Step);
        pad.Press("A");
        map.Update(Step);
        var faceButton = map.IsHeld("Fire");
        pad.Press("RightShoulder");
        map.Update(Step);

        //Assert
        faceButton.Should().BeFalse();
        map.WasPressed("Fire").Should().BeTrue();
        map.WasPressed("Confirm").Should().BeFalse();
        classic.GetBindings("Fire").Should().Contain(InputBinding.GamepadButton("A"));
    }

    [Fact]
    public void an_input_the_new_profile_newly_binds_that_is_held_at_the_swap_is_not_a_press()
    {
        //Arrange
        var pad = Pad();
        var classic = ShooterProfile();
        var shoulder = classic.Copy("Shoulder").Rebind("Fire", InputBinding.GamepadButton("RightShoulder"));
        var map = Started(classic);
        pad.Press("RightShoulder");
        map.Update(Step);

        //Act
        map.Profile = shoulder;
        map.Update(Step);
        var atSwap = (map.IsHeld("Fire"), map.WasPressed("Fire"));
        pad.Release("RightShoulder");
        map.Update(Step);
        pad.Press("RightShoulder");
        map.Update(Step);

        //Assert
        atSwap.Should().Be((false, false));
        map.WasPressed("Fire").Should().BeTrue();
    }

    [Fact]
    public void an_edit_to_the_active_profile_takes_effect_at_the_next_poll()
    {
        //Arrange
        var profile = ShooterProfile();
        var map = Started(profile);

        //Act
        profile.Bind("Fire", InputBinding.Key(KeyEnter));
        map.Update(Step);
        _keyboard.Press(KeyEnter);
        map.Update(Step);

        //Assert
        map.WasPressed("Fire").Should().BeTrue();
    }

    [Fact]
    public void GetAxis_adds_the_actions_and_the_dead_zoned_stick_and_clamps()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        pad.LeftStick = new GamepadStickState(0.1f, 0f);
        map.Update(Step);
        var inDeadZone = map.GetAxis("MoveLeft", "MoveRight", GamepadStick.Left);
        pad.LeftStick = new GamepadStickState(0.5f, 0f);
        map.Update(Step);
        var stickOnly = map.GetAxis("MoveLeft", "MoveRight", GamepadStick.Left);
        _keyboard.Press(KeyD);
        map.Update(Step);
        var both = map.GetAxis("MoveLeft", "MoveRight", GamepadStick.Left);
        _keyboard.Press(KeyA);
        map.Update(Step);
        var opposite = map.GetAxis("MoveLeft", "MoveRight");

        //Assert
        inDeadZone.Should().Be(0.0);
        stickOnly.Should().BeApproximately(0.5, 1e-6);
        both.Should().Be(1.0);
        opposite.Should().Be(0.0);
    }

    [Fact]
    public void SimulatePress_latches_one_press_without_changing_the_last_device()
    {
        //Arrange
        var map = Started();
        map.LastDevice = InputDeviceKind.Gamepad;

        //Act
        map.SimulatePress("Pause");
        map.Update(Step);
        var first = (map.WasPressed("Pause"), map.IsHeld("Pause"), map.IsTriggered("Pause"));
        map.Update(Step);

        //Assert
        first.Should().Be((true, true, true));
        map.WasPressed("Pause").Should().BeFalse();
        map.LastDevice.Should().Be(InputDeviceKind.Gamepad);
    }

    [Fact]
    public void ClearLatched_drops_pending_presses_and_names()
    {
        //Arrange
        var map = Started();
        _keyboard.Press(KeyEnter);
        map.Poll(0.001);
        _keyboard.Release(KeyEnter);
        map.Poll(0.001);

        //Act
        map.ClearLatched();
        map.Update(Step);

        //Assert
        map.WasPressed("Confirm").Should().BeFalse();
        map.AnyPressed.Should().BeFalse();
        map.PressedBindings.Should().BeEmpty();
    }

    [Fact]
    public void PressedBindings_lists_every_press_once_and_no_repeats()
    {
        //Arrange
        var pad = Pad();
        var map = Started();

        //Act
        _keyboard.Press(KeyEnter);
        pad.Press("Start");
        map.Update(Step);
        var names = map.PressedBindings.Select(binding => binding.ToString()).ToArray();
        var any = map.AnyPressed;
        _keyboard.Press(KeyUp);
        Hold(map, "MenuUp", Delay + 0.5);
        var afterHold = map.PressedBindings;

        //Assert
        names.Should().Contain("key Enter");
        names.Should().Contain("button Start");
        any.Should().BeTrue();
        afterHold.Should().BeEmpty();
    }

    [Fact]
    public void the_active_profile_keys_are_claimed_once_and_keys_already_used_are_left_alone()
    {
        //Arrange
        _keyboard.Claimed.Add(KeyEscape);
        var map = Create();

        //Act
        map.Update(Step);
        map.Update(Step);

        //Assert
        _keyboard.ClaimCalls.Should().Be(1);
        _keyboard.Claimed.Should().Contain(new[] { KeyEnter, KeySpace, KeyUp, KeyLeftShift, KeyShift, KeyA, KeyD });
        map.Detach();
        _keyboard.Claimed.Should().BeEquivalentTo(new[] { KeyEscape });
    }

    [Fact]
    public void a_profile_swap_withdraws_the_claims_on_keys_it_drops()
    {
        //Arrange
        var classic = ShooterProfile();
        var small = new InputBindingProfile("Small").Bind("Fire", InputBinding.Key(KeySpace));
        var map = Started(classic);

        //Act
        map.Profile = small;
        map.Update(Step);

        //Assert
        _keyboard.Claimed.Should().BeEquivalentTo(new[] { KeySpace });
    }

    [Fact]
    public void ClaimKeys_false_withdraws_and_stops_claiming()
    {
        //Arrange
        var map = Started();

        //Act
        map.ClaimKeys = false;
        map.Update(Step);

        //Assert
        _keyboard.Claimed.Should().BeEmpty();
    }

    [Fact]
    public void a_keyboard_without_claims_is_simply_read()
    {
        //Arrange
        var plain = new PlainKeyboard();
        var map = new InputActionMap(ShooterProfile(), () => plain, () => null);
        map.Update(Step);

        //Act
        plain.Down = KeySpace;
        map.Update(Step);

        //Assert
        map.WasPressed("Fire").Should().BeTrue();
    }

    [Fact]
    public void Attach_twice_throws_and_Detach_is_safe_when_not_attached()
    {
        //Arrange
        var map = Create();
        map.Detach();

        //Act
        map.Attach(Engine.Instance);
        var second = () => map.Attach(Engine.Instance);

        //Assert
        second.Should().Throw<InvalidOperationException>();
        map.IsAttached.Should().BeTrue();
        map.Detach();
        map.IsAttached.Should().BeFalse();
    }

    [Fact]
    public void an_attached_map_latches_a_key_tap_shorter_than_a_fixed_step()
    {
        //Arrange
        var map = Create();
        var pressedSteps = 0;
        var steps = 0;
        Action<FixedUpdateStep> onStep = step =>
        {
            map.Update(step.DeltaSeconds);
            if (map.WasPressed("Confirm"))
                Interlocked.Increment(ref pressedSteps);
            Interlocked.Increment(ref steps);
        };
        Engine.Instance.FixedUpdate += onStep;
        map.Attach(Engine.Instance);

        try
        {
            Engine.Instance.Start(new SynchronizationContext());
            Engine.Instance.Configuration.FixedUpdateRate = 20;
            WaitUntil(() => Volatile.Read(ref steps) >= 2);

            //Act - a 5 ms tap inside a 50 ms step
            _keyboard.Press(KeyEnter);
            Thread.Sleep(5);
            _keyboard.Release(KeyEnter);
            var seenAt = Volatile.Read(ref steps);
            WaitUntil(() => Volatile.Read(ref steps) >= seenAt + 3);

            //Assert
            Volatile.Read(ref pressedSteps).Should().Be(1);
        }
        finally
        {
            map.Detach();
            Engine.Instance.FixedUpdate -= onStep;
            if (Engine.Instance.IsRunning)
                Engine.Instance.StopAndWait();
            Engine.Instance.Configuration.FixedUpdateRate = 0;
        }
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("The expected condition was not reached in time.");
            Thread.Sleep(2);
        }
    }

    /// <summary>A keyboard whose keys the test holds, and which records key claims as the Host adapter would.</summary>
    private sealed class FakeKeyboard : IKeyboardAdapter, IKeyClaimingAdapter
    {
        private readonly HashSet<int> _down = new();

        public HashSet<int> Claimed { get; } = new();

        public int ClaimCalls { get; private set; }

        public KeyboardModifierState CurrentKeyboardModifiers => KeyboardModifierState.None;

        public void Press(int keyCode)
        {
            lock (_down)
                _down.Add(keyCode);
        }

        public void Release(int keyCode)
        {
            lock (_down)
                _down.Remove(keyCode);
        }

        public bool IsDown(int keyCode)
        {
            lock (_down)
                return _down.Contains(keyCode);
        }

        public void ClaimKeys(IEnumerable<int> keyCodes)
        {
            ClaimCalls++;
            Claimed.UnionWith(keyCodes);
        }

        public void UnclaimKey(int keyCode) => Claimed.Remove(keyCode);

        public bool IsKeyUsed(int keyCode) => Claimed.Contains(keyCode);
    }

    /// <summary>A keyboard with no key claims.</summary>
    private sealed class PlainKeyboard : IKeyboardAdapter
    {
        public int Down { get; set; }

        public KeyboardModifierState CurrentKeyboardModifiers => KeyboardModifierState.None;

        public bool IsDown(int keyCode) => keyCode == Down;
    }

    /// <summary>A gamepad whose buttons and sticks the test sets.</summary>
    private sealed class FakePad : IGamepadAdapter
    {
        private readonly HashSet<string> _pressed = new(StringComparer.Ordinal);

        public FakePad(string gamepadId) => GamepadId = gamepadId;

        public string GamepadId { get; }

        public IReadOnlyCollection<string> PressedButtons => _pressed;

        public GamepadStickState? LeftStick { get; set; } = new GamepadStickState(0f, 0f);

        public GamepadStickState? RightStick { get; set; }

        public float LeftTrigger => 0f;

        public float RightTrigger => 0f;

        public void Press(string button) => _pressed.Add(button);

        public void Release(string button) => _pressed.Remove(button);
    }
}
