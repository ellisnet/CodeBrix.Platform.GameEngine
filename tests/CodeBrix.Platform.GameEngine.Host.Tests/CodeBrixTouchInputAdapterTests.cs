using CodeBrix.Platform.GameEngine.Host.Input.Mouse;
using CodeBrix.Platform.GameEngine.Host.Input.Touch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SilverAssertions;
using Windows.Devices.Input;
using Xunit;
using WuxPointerUpdateKind = Windows.UI.Input.PointerUpdateKind;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// The touch adapter keeps the contacts it tracks (marked handled, so a parent of the surface does not
/// act on them as well) and leaves alone what it ignores - mouse input unless mouse emulation is on.
/// Several adapters on one surface each still see every event, whichever of them marked it handled.
/// </summary>
[Collection(HeadlessPlatform.CollectionName)]
public class CodeBrixTouchInputAdapterTests
{
    private readonly Grid _parent;
    private readonly Canvas _surface;
    private int _parentPressedCount;
    private int _parentReleasedCount;

    public CodeBrixTouchInputAdapterTests()
    {
        HeadlessPlatform.EnsureInitialized();

        _parent = new Grid();
        _surface = new Canvas();
        _parent.Children.Add(_surface);

        _parent.PointerPressed += (_, _) => _parentPressedCount++;
        _parent.PointerReleased += (_, _) => _parentReleasedCount++;
    }

    [Fact]
    public void A_touch_contact_is_tracked_and_its_press_and_release_are_marked_handled()
    {
        //Arrange
        using var adapter = new CodeBrixTouchInputAdapter(_surface);
        var press = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed,
            leftPressed: true, deviceType: PointerDeviceType.Touch, pointerId: 7);
        var release = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonReleased,
            deviceType: PointerDeviceType.Touch, pointerId: 7);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, press);
        var activeAfterPress = adapter.ActiveTouches.Count;
        TestPointerEvents.Raise(_surface, UIElement.PointerReleasedEvent, release);

        //Assert
        activeAfterPress.Should().Be(1);
        press.Handled.Should().BeTrue();
        release.Handled.Should().BeTrue();
        _parentPressedCount.Should().Be(0);
        _parentReleasedCount.Should().Be(0);
        adapter.ActiveTouches.Should().BeEmpty();
    }

    [Fact]
    public void A_mouse_press_is_left_unhandled_when_mouse_emulation_is_off()
    {
        //Arrange
        using var adapter = new CodeBrixTouchInputAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeFalse();
        _parentPressedCount.Should().Be(1);
        adapter.ActiveTouches.Should().BeEmpty();
    }

    [Fact]
    public void An_emulating_touch_adapter_attached_after_the_mouse_adapter_still_sees_the_press()
    {
        //Arrange - the mouse adapter marks the press handled before the touch adapter's turn.
        using var mouse = new CodeBrixMouseAdapter(_surface);
        using var touch = new CodeBrixTouchInputAdapter(_surface, emulateMouse: true);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        touch.ActiveTouches.Should().ContainSingle().Which.Id.Should().Be(0);
        _parentPressedCount.Should().Be(0);
    }
}
