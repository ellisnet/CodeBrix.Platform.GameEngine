using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodeBrix.Platform.GameEngine.Host.Input.Mouse;

/// <summary>
/// Provides a mouse/pointer input adapter for CodeBrix.Platform applications, tracking pointer
/// position, button states, modifier keys, and scroll events on a <see cref="UIElement"/>
/// (typically the game surface canvas).
/// </summary>
/// <remarks>
/// Every pointer event this adapter reads on the element (press, release, wheel, cancel, capture loss, and
/// a move while a button it tracks is held - a drag) is marked handled
/// (<see cref="PointerRoutedEventArgs.Handled"/>); a hover move is read but left unhandled, so hover
/// tracking above the surface keeps working. The game owns input over its surface, so ancestors of the surface (a ScrollViewer, a page-level handler) do not act on a game click
/// as well, and the framework does not take a click on the focused surface as one nobody wanted (which
/// would drop keyboard focus). Events that are not the surface's own never reach the adapter, and an event
/// something inside the surface handled first (a button placed over the game) is left alone. A handler of
/// the application's own on the surface sees the events too when it is subscribed with
/// <see cref="UIElement.AddHandler"/> and <c>handledEventsToo: true</c>; one attached with <c>+=</c> after
/// this adapter does not.
/// </remarks>
public sealed class CodeBrixMouseAdapter : IMouseAdapter, IDisposable
{
    private readonly UIElement _element;
    private readonly PointerCoordinateMapper _coordinates;
    private readonly PointerEventHandler _pressedHandler;
    private readonly PointerEventHandler _releasedHandler;
    private readonly PointerEventHandler _movedHandler;
    private readonly PointerEventHandler _wheelChangedHandler;
    private readonly HashSet<MouseButton> _pressed = new();
    private Point _currentPosition;
    private KeyboardModifierState _modifiers;
    private int _scrollDelta;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current position of the pointer cursor in logical Backbuffer ScreenPx — the space
    /// the engine's views, cameras and drawings work in — mapped from the element's own pixels
    /// through the render surface adapter's presentation transform.
    /// </summary>
    /// <remarks>
    /// A pointer over the letterbox or pillarbox margins keeps its outside coordinates (negative, or
    /// past the logical width or height) rather than being clamped to the edge of the image.
    /// </remarks>
    public Point CurrentPosition => _currentPosition;

    /// <summary>
    /// Gets the set of currently pressed mouse buttons.
    /// </summary>
    public HashSet<MouseButton> PressedButtons => _pressed;

    /// <summary>
    /// Gets the current state of keyboard modifiers (Shift, Ctrl, Alt).
    /// </summary>
    public KeyboardModifierState CurrentKeyboardModifiers => _modifiers;

    /// <summary>
    /// Gets the accumulated scroll wheel delta since the last read, then resets it to zero.
    /// </summary>
    public int ScrollDelta => Interlocked.Exchange(ref _scrollDelta, 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixMouseAdapter"/> class attached to the specified element.
    /// </summary>
    /// <param name="element">The element to monitor for pointer events.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is <see langword="null"/>.</exception>
    public CodeBrixMouseAdapter(UIElement element)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _coordinates = new PointerCoordinateMapper(element);

        // Subscribed for handled events too, so the adapters on one surface each see every event
        // whichever of them marked it handled first (see SurfacePointerEvents).
        _pressedHandler = OnPointerPressed;
        _releasedHandler = OnPointerReleased;
        _movedHandler = OnPointerMoved;
        _wheelChangedHandler = OnPointerWheelChanged;
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerPressedEvent, _pressedHandler);
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerReleasedEvent, _releasedHandler);
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerMovedEvent, _movedHandler);
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerWheelChangedEvent, _wheelChangedHandler);
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerCanceledEvent, _releasedHandler);
        SurfacePointerEvents.Subscribe(_element, UIElement.PointerCaptureLostEvent, _releasedHandler);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!SurfacePointerEvents.IsAvailable(e)) return;

        var point = e.GetCurrentPoint(_element);
        var props = point.Properties;
        SyncButtons(props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);
        UpdatePosition(point.Position, e.KeyModifiers);
        SurfacePointerEvents.Consume(e);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!SurfacePointerEvents.IsAvailable(e)) return;

        var point = e.GetCurrentPoint(_element);
        var props = point.Properties;
        // Re-sync from the live button state so the button that was just released is cleared.
        SyncButtons(props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);
        UpdatePosition(point.Position, e.KeyModifiers);
        SurfacePointerEvents.Consume(e);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!SurfacePointerEvents.IsAvailable(e)) return;

        var point = e.GetCurrentPoint(_element);
        UpdatePosition(point.Position, e.KeyModifiers);

        // Only a drag is kept; a hover move still bubbles to the surface's ancestors.
        if (_pressed.Count > 0) SurfacePointerEvents.Consume(e);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!SurfacePointerEvents.IsAvailable(e)) return;

        // MouseWheelDelta is already in Windows WHEEL_DELTA units (120 per notch);
        // positive = scroll up/away, matching the engine convention.
        var delta = e.GetCurrentPoint(_element).Properties.MouseWheelDelta;
        Interlocked.Add(ref _scrollDelta, delta);
        SurfacePointerEvents.Consume(e);
    }

    private void SyncButtons(bool left, bool right, bool middle)
    {
        if (left) _pressed.Add(MouseButton.Left); else _pressed.Remove(MouseButton.Left);
        if (right) _pressed.Add(MouseButton.Right); else _pressed.Remove(MouseButton.Right);
        if (middle) _pressed.Add(MouseButton.Middle); else _pressed.Remove(MouseButton.Middle);
    }

    private void UpdatePosition(global::Windows.Foundation.Point position, VirtualKeyModifiers keyModifiers)
    {
        _currentPosition = _coordinates.ToScreenPx(position);

        var modifiers = KeyboardModifierState.None;
        if ((keyModifiers & VirtualKeyModifiers.Shift) != 0) modifiers |= KeyboardModifierState.Shift;
        if ((keyModifiers & VirtualKeyModifiers.Control) != 0) modifiers |= KeyboardModifierState.Ctrl;
        if ((keyModifiers & VirtualKeyModifiers.Menu) != 0) modifiers |= KeyboardModifierState.Alt;
        _modifiers = modifiers;
    }

    /// <summary>
    /// Releases all resources and removes all event handlers registered by this adapter.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerPressedEvent, _pressedHandler);
        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerReleasedEvent, _releasedHandler);
        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerMovedEvent, _movedHandler);
        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerWheelChangedEvent, _wheelChangedHandler);
        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerCanceledEvent, _releasedHandler);
        SurfacePointerEvents.Unsubscribe(_element, UIElement.PointerCaptureLostEvent, _releasedHandler);
    }
}
