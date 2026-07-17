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
public sealed class CodeBrixMouseAdapter : IMouseAdapter, IDisposable
{
    private readonly UIElement _element;
    private readonly HashSet<MouseButton> _pressed = new();
    private Point _currentPosition;
    private KeyboardModifierState _modifiers;
    private int _scrollDelta;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current position of the pointer cursor in client (element-local) coordinates.
    /// </summary>
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

        _element.PointerPressed += OnPointerPressed;
        _element.PointerReleased += OnPointerReleased;
        _element.PointerMoved += OnPointerMoved;
        _element.PointerWheelChanged += OnPointerWheelChanged;
        _element.PointerCanceled += OnPointerReleased;
        _element.PointerCaptureLost += OnPointerReleased;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_element);
        var props = point.Properties;
        SyncButtons(props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);
        UpdatePosition(point.Position, e.KeyModifiers);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_element);
        var props = point.Properties;
        // Re-sync from the live button state so the button that was just released is cleared.
        SyncButtons(props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);
        UpdatePosition(point.Position, e.KeyModifiers);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_element);
        UpdatePosition(point.Position, e.KeyModifiers);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // MouseWheelDelta is already in Windows WHEEL_DELTA units (120 per notch);
        // positive = scroll up/away, matching the engine convention.
        var delta = e.GetCurrentPoint(_element).Properties.MouseWheelDelta;
        Interlocked.Add(ref _scrollDelta, delta);
    }

    private void SyncButtons(bool left, bool right, bool middle)
    {
        if (left) _pressed.Add(MouseButton.Left); else _pressed.Remove(MouseButton.Left);
        if (right) _pressed.Add(MouseButton.Right); else _pressed.Remove(MouseButton.Right);
        if (middle) _pressed.Add(MouseButton.Middle); else _pressed.Remove(MouseButton.Middle);
    }

    private void UpdatePosition(global::Windows.Foundation.Point position, VirtualKeyModifiers keyModifiers)
    {
        _currentPosition = new Point((int)position.X, (int)position.Y);

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

        _element.PointerPressed -= OnPointerPressed;
        _element.PointerReleased -= OnPointerReleased;
        _element.PointerMoved -= OnPointerMoved;
        _element.PointerWheelChanged -= OnPointerWheelChanged;
        _element.PointerCanceled -= OnPointerReleased;
        _element.PointerCaptureLost -= OnPointerReleased;
    }
}
