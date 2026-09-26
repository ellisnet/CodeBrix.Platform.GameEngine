using System;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for <see cref="KeyboardEventPoller.IsMonitoringKey"/>, the any-thread view of which keys the game
/// has registered - what a host keyboard adapter asks to decide which key events to mark handled. The
/// poller is a process-global singleton, so every test resets it afterwards.
/// </summary>
public class KeyboardEventPollerTests : IDisposable
{
    [Fact]
    public void IsMonitoringKey_is_true_at_once_after_StartMonitoringKey()
    {
        //Arrange
        var poller = CreatePoller();

        //Act
        poller.StartMonitoringKey(0x70); // F1

        //Assert - no poll has run, so the configuration itself is still queued
        poller.IsMonitoringKey(0x70).Should().BeTrue();
        poller.IsMonitoringKey(0x71).Should().BeFalse();
    }

    [Fact]
    public void IsMonitoringKey_covers_every_key_of_StartMonitoringKeys()
    {
        //Arrange
        var poller = CreatePoller();

        //Act
        poller.StartMonitoringKeys([0x25, 0x26, 0x27, 0x28]); // the arrow keys

        //Assert
        poller.IsMonitoringKey(0x25).Should().BeTrue();
        poller.IsMonitoringKey(0x28).Should().BeTrue();
        poller.IsMonitoringKey(0x20).Should().BeFalse();
    }

    [Fact]
    public void IsMonitoringKey_covers_the_whole_8_bit_space_after_StartMonitoringAllKeys()
    {
        //Arrange
        var poller = CreatePoller();

        //Act
        poller.StartMonitoringAllKeys();

        //Assert
        poller.IsMonitoringKey(1).Should().BeTrue();
        poller.IsMonitoringKey(0x09).Should().BeTrue(); // Tab
        poller.IsMonitoringKey(255).Should().BeTrue();
        poller.IsMonitoringKey(256).Should().BeFalse();
    }

    [Fact]
    public void IsMonitoringKey_is_false_at_once_after_StopMonitoringKey()
    {
        //Arrange
        var poller = CreatePoller();
        poller.StartMonitoringKey(0x70);
        poller.StartMonitoringKey(0x71);

        //Act
        poller.StopMonitoringKey(0x70);

        //Assert
        poller.IsMonitoringKey(0x70).Should().BeFalse();
        poller.IsMonitoringKey(0x71).Should().BeTrue();
    }

    [Fact]
    public void IsMonitoringKey_is_false_for_every_key_after_StopMonitoringAllKeys()
    {
        //Arrange
        var poller = CreatePoller();
        poller.StartMonitoringAllKeys();

        //Act
        poller.StopMonitoringAllKeys();

        //Assert
        poller.IsMonitoringKey(0x70).Should().BeFalse();
        poller.IsMonitoringKey(0x09).Should().BeFalse();
    }

    public void Dispose()
    {
        KeyboardEventPoller.ResetForTests();
        GC.SuppressFinalize(this);
    }

    private static KeyboardEventPoller CreatePoller()
    {
        KeyboardEventPoller.Initialize(new FakeKeyboardAdapter());
        return KeyboardEventPoller.Instance!;
    }

    private sealed class FakeKeyboardAdapter : IKeyboardAdapter
    {
        public bool IsDown(int keyCode) => false;

        public KeyboardModifierState CurrentKeyboardModifiers => KeyboardModifierState.None;
    }
}
