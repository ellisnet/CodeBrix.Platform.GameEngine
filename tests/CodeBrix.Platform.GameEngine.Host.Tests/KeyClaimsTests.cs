using CodeBrix.Platform.GameEngine.Host.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// The rule <see cref="CodeBrixKeyboardAdapter"/> uses to decide which key events to mark handled: a key the
/// game uses (claimed, or monitored on the poller the adapter feeds) stays with the game; every other key
/// passes through to the application. The adapter itself needs a live UI element, so its wiring
/// (KeyDown/KeyUp marked handled) is verified on X11.
/// </summary>
public class KeyClaimsTests
{
    private const int F1 = 0x70;
    private const int Space = 0x20;

    [Fact]
    public void IsUsed_is_true_for_a_claimed_key_and_false_for_others()
    {
        //Arrange
        var claims = new KeyClaims();
        claims.Claim(Space);

        //Act
        var spaceUsed = claims.IsUsed(Space, new FakeKeyboardAdapter(), poller: null);
        var f1Used = claims.IsUsed(F1, new FakeKeyboardAdapter(), poller: null);

        //Assert
        spaceUsed.Should().BeTrue();
        f1Used.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_covers_every_key_claimed_in_one_call()
    {
        //Arrange
        var claims = new KeyClaims();

        //Act
        claims.Claim([0x25, 0x26, 0x27, 0x28]);

        //Assert
        claims.IsClaimed(0x25).Should().BeTrue();
        claims.IsClaimed(0x28).Should().BeTrue();
        claims.IsClaimed(Space).Should().BeFalse();
    }

    [Fact]
    public void IsUsed_is_false_after_the_claim_is_withdrawn()
    {
        //Arrange
        var claims = new KeyClaims();
        claims.Claim(Space);
        claims.Claim(F1);

        //Act
        claims.Unclaim(Space);

        //Assert
        claims.IsUsed(Space, new FakeKeyboardAdapter(), poller: null).Should().BeFalse();
        claims.IsUsed(F1, new FakeKeyboardAdapter(), poller: null).Should().BeTrue();
    }

    [Fact]
    public void IsUsed_is_false_for_every_key_after_UnclaimAll()
    {
        //Arrange
        var claims = new KeyClaims();
        claims.Claim([Space, F1]);

        //Act
        claims.UnclaimAll();

        //Assert
        claims.IsClaimed(Space).Should().BeFalse();
        claims.IsClaimed(F1).Should().BeFalse();
    }

    [Fact]
    public void IsUsed_is_true_for_a_key_monitored_on_the_poller_the_adapter_feeds()
    {
        //Arrange
        var adapter = new FakeKeyboardAdapter();
        KeyboardEventPoller.Initialize(adapter);
        var poller = KeyboardEventPoller.Instance!;
        poller.StartMonitoringKey(F1);
        var claims = new KeyClaims();

        //Act
        var f1Used = claims.IsUsed(F1, adapter, poller);
        var spaceUsed = claims.IsUsed(Space, adapter, poller);

        //Assert
        f1Used.Should().BeTrue();
        spaceUsed.Should().BeFalse();
    }

    [Fact]
    public void IsUsed_ignores_a_poller_fed_by_another_adapter()
    {
        //Arrange
        KeyboardEventPoller.Initialize(new FakeKeyboardAdapter());
        var poller = KeyboardEventPoller.Instance!;
        poller.StartMonitoringAllKeys();
        var claims = new KeyClaims();

        //Act
        var used = claims.IsUsed(F1, new FakeKeyboardAdapter(), poller);

        //Assert
        used.Should().BeFalse();
    }

    [Fact]
    public void the_keyboard_adapter_takes_claims_through_the_core_claim_interface() =>
        typeof(IKeyClaimingAdapter).IsAssignableFrom(typeof(CodeBrixKeyboardAdapter)).Should().BeTrue();

    private sealed class FakeKeyboardAdapter : IKeyboardAdapter
    {
        public bool IsDown(int keyCode) => false;

        public KeyboardModifierState CurrentKeyboardModifiers => KeyboardModifierState.None;
    }
}
