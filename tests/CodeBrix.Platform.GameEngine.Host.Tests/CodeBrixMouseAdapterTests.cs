using CodeBrix.Platform.GameEngine.Host.Input.Mouse;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SilverAssertions;
using Xunit;
using WuxPointerUpdateKind = Windows.UI.Input.PointerUpdateKind;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// The mouse adapter keeps the pointer events it reads: they are marked handled, so a parent of the
/// surface (a ScrollViewer, a page-level handler) does not act on a game click as well, and the
/// framework does not treat the click as one nobody wanted (which drops keyboard focus). Events that
/// are not the surface's own, or that something inside the surface handled first, are left alone.
/// Real elements and real routed events, host-free (see <see cref="HeadlessPlatform"/>).
/// </summary>
[Collection(HeadlessPlatform.CollectionName)]
public class CodeBrixMouseAdapterTests
{
    private readonly Grid _parent;
    private readonly Canvas _surface;
    private int _parentPressedCount;
    private int _parentReleasedCount;
    private int _parentMovedCount;
    private int _parentWheelCount;

    public CodeBrixMouseAdapterTests()
    {
        HeadlessPlatform.EnsureInitialized();

        _parent = new Grid();
        _surface = new Canvas();
        _parent.Children.Add(_surface);

        _parent.PointerPressed += (_, _) => _parentPressedCount++;
        _parent.PointerReleased += (_, _) => _parentReleasedCount++;
        _parent.PointerMoved += (_, _) => _parentMovedCount++;
        _parent.PointerWheelChanged += (_, _) => _parentWheelCount++;
    }

    [Fact]
    public void A_left_press_on_the_surface_is_marked_handled_and_does_not_bubble_to_the_parent()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _parentPressedCount.Should().Be(0);
        adapter.PressedButtons.Should().Contain(MouseButton.Left);
    }

    [Fact]
    public void A_left_release_on_the_surface_is_marked_handled_and_clears_the_button()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonReleased);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerReleasedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _parentReleasedCount.Should().Be(0);
        adapter.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void A_right_press_on_the_surface_is_marked_handled()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.RightButtonPressed, rightPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _parentPressedCount.Should().Be(0);
        adapter.PressedButtons.Should().Contain(MouseButton.Right);
    }

    [Fact]
    public void A_hover_move_over_the_surface_is_read_but_left_unhandled()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.Other);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerMovedEvent, args);

        //Assert
        args.Handled.Should().BeFalse();
        _parentMovedCount.Should().Be(1);
    }

    [Fact]
    public void A_drag_move_over_the_surface_is_marked_handled()
    {
        //Arrange - the left button went down on the surface and is still held.
        using var adapter = new CodeBrixMouseAdapter(_surface);
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.Other, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerMovedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _parentMovedCount.Should().Be(0);
    }

    [Fact]
    public void A_wheel_over_the_surface_is_marked_handled_and_accumulated()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.Other, wheelDelta: 120);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerWheelChangedEvent, args);

        //Assert
        args.Handled.Should().BeTrue();
        _parentWheelCount.Should().Be(0);
        adapter.ScrollDelta.Should().Be(120);
    }

    [Fact]
    public void A_press_something_inside_the_surface_handled_first_is_left_alone()
    {
        //Arrange - a child of the surface that handles its own presses, as a button would.
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var child = new Border();
        _surface.Children.Add(child);
        child.PointerPressed += (_, e) => e.Handled = true;
        var args = TestPointerEvents.Create(child, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(child, UIElement.PointerPressedEvent, args);

        //Assert
        adapter.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void A_press_on_an_element_outside_the_surface_is_not_touched()
    {
        //Arrange - a sibling of the surface, such as a link or a button beside the game.
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var sibling = new Border();
        _parent.Children.Add(sibling);
        var args = TestPointerEvents.Create(sibling, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(sibling, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeFalse();
        _parentPressedCount.Should().Be(1);
        adapter.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void A_disposed_adapter_leaves_presses_unhandled()
    {
        //Arrange
        var adapter = new CodeBrixMouseAdapter(_surface);
        adapter.Dispose();
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        args.Handled.Should().BeFalse();
        _parentPressedCount.Should().Be(1);
        adapter.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void A_surface_handler_that_asks_for_handled_events_still_sees_a_press_the_adapter_kept()
    {
        //Arrange - the documented way for an app to watch surface clicks itself.
        using var adapter = new CodeBrixMouseAdapter(_surface);
        var seen = 0;
        _surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => seen++), handledEventsToo: true);
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent, args);

        //Assert
        seen.Should().Be(1);
        args.Handled.Should().BeTrue();
    }
}
