using System.Collections;
using System.Linq;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Host.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Host.Input.Mouse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SilverAssertions;
using Xunit;
using WuxPointerUpdateKind = Windows.UI.Input.PointerUpdateKind;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// The keyboard adapter's refocus-on-press must run on every press over the surface, including one the
/// mouse adapter has already marked handled. Whether focus actually lands needs a live head (a detached
/// element cannot take focus), so this checks that the refocus handler is subscribed for handled
/// events too, and that the press is still raised through it without error.
/// </summary>
[Collection(HeadlessPlatform.CollectionName)]
public class CodeBrixKeyboardAdapterTests
{
    private readonly Grid _parent;
    private readonly Canvas _surface;

    public CodeBrixKeyboardAdapterTests()
    {
        HeadlessPlatform.EnsureInitialized();

        _parent = new Grid();
        _surface = new Canvas();
        _parent.Children.Add(_surface);
    }

    [Fact]
    public void The_refocus_on_press_is_subscribed_for_handled_presses_too()
    {
        //Arrange - the mouse adapter first, so it marks the press handled before the keyboard adapter's turn.
        using var mouse = new CodeBrixMouseAdapter(_surface);
        using var keyboard = new CodeBrixKeyboardAdapter(_surface);

        //Act
        var subscriptions = PressedHandlerSubscriptions(_surface);

        //Assert
        subscriptions.Should().NotBeEmpty();
        subscriptions.Should().OnlyContain(handledEventsToo => handledEventsToo);
    }

    [Fact]
    public void A_press_the_mouse_adapter_kept_still_reaches_the_keyboard_adapter_without_error()
    {
        //Arrange
        using var mouse = new CodeBrixMouseAdapter(_surface);
        using var keyboard = new CodeBrixKeyboardAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _surface.IsTabStop.Should().BeTrue();
    }

    // The HandledEventsToo flag of every PointerPressed subscription on the element, read from the
    // framework's handler store (internal, hence the reflection).
    private static bool[] PressedHandlerSubscriptions(UIElement element)
    {
        var store = (IDictionary)typeof(UIElement)
            .GetField("_eventHandlerStore", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(element)!;
        var handlers = (IEnumerable)store[UIElement.PointerPressedEvent]!;

        return handlers.Cast<object>()
            .Select(info => (bool)info.GetType()
                .GetProperty("HandledEventsToo", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(info)!)
            .ToArray();
    }
}
