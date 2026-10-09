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

    // The capture tests use a surface with no parent. Outside a window no element is loaded, so none is
    // hit-test visible, and the framework silently drops a capture on such an element as soon as the event
    // bubbles on to a parent; a lone surface keeps the capture just as a loaded canvas in an app does.

    [Fact]
    public void A_left_press_on_the_surface_captures_the_pointer_on_the_surface()
    {
        //Arrange
        var surface = new Canvas();
        using var adapter = new CodeBrixMouseAdapter(surface);
        var args = TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true);

        //Act
        TestPointerEvents.Raise(surface, UIElement.PointerPressedEvent, args);

        //Assert
        surface.PointerCaptures.Should().ContainSingle(p => p.PointerId == args.Pointer.PointerId);
        adapter.PressedButtons.Should().Contain(MouseButton.Left);
    }

    [Fact]
    public void A_left_release_on_the_surface_releases_the_capture()
    {
        //Arrange
        var surface = new Canvas();
        using var adapter = new CodeBrixMouseAdapter(surface);
        TestPointerEvents.Raise(surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        surface.PointerCaptures.Should().ContainSingle();

        //Act
        TestPointerEvents.Raise(surface, UIElement.PointerReleasedEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonReleased));

        //Assert
        surface.PointerCaptures.Should().BeEmpty();
        adapter.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void A_lost_capture_clears_every_pressed_button()
    {
        //Arrange - left and right held, and the capture is then taken from the surface (as when a pane the
        //press opened takes the pointer), so the release will never reach the surface.
        var surface = new Canvas();
        using var adapter = new CodeBrixMouseAdapter(surface);
        TestPointerEvents.RaiseAsInput(surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true, rightPressed: true));
        adapter.PressedButtons.Should().HaveCount(2);
        var captureLostCount = 0;
        surface.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => captureLostCount++), handledEventsToo: true);

        //Act - the framework raises PointerCaptureLost on the surface, built from the press the adapter
        //already marked handled.
        surface.ReleasePointerCaptures();

        //Assert
        captureLostCount.Should().Be(1);
        adapter.PressedButtons.Should().BeEmpty();
        surface.PointerCaptures.Should().BeEmpty();
    }

    [Fact]
    public void A_capture_lost_event_clears_the_pressed_state_whatever_buttons_it_reports()
    {
        //Arrange
        using var adapter = new CodeBrixMouseAdapter(_surface);
        TestPointerEvents.Raise(_surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(_surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        var args = TestPointerEvents.Create(_surface, WuxPointerUpdateKind.Other, leftPressed: true);

        //Act
        TestPointerEvents.Raise(_surface, UIElement.PointerCaptureLostEvent, args);

        //Assert
        adapter.PressedButtons.Should().BeEmpty();
        args.Handled.Should().BeTrue();
    }

    [Fact]
    public void A_cancelled_pointer_clears_the_pressed_state_and_releases_the_capture()
    {
        //Arrange
        var surface = new Canvas();
        using var adapter = new CodeBrixMouseAdapter(surface);
        TestPointerEvents.Raise(surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        surface.PointerCaptures.Should().ContainSingle();

        //Act
        TestPointerEvents.Raise(surface, UIElement.PointerCanceledEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.Other, leftPressed: true));

        //Assert
        adapter.PressedButtons.Should().BeEmpty();
        surface.PointerCaptures.Should().BeEmpty();
    }

    [Fact]
    public void A_disposed_adapter_hands_back_a_capture_it_still_holds()
    {
        //Arrange
        var surface = new Canvas();
        var adapter = new CodeBrixMouseAdapter(surface);
        TestPointerEvents.Raise(surface, UIElement.PointerPressedEvent,
            TestPointerEvents.Create(surface, WuxPointerUpdateKind.LeftButtonPressed, leftPressed: true));
        surface.PointerCaptures.Should().ContainSingle();

        //Act
        adapter.Dispose();

        //Assert
        surface.PointerCaptures.Should().BeEmpty();
    }
}
