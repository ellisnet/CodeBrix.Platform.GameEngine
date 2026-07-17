using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Input.Touch;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace CodeBrix.Platform.GameEngine.Host.Input.Touch;

/// <summary>
/// Provides a passive touch/pointer state adapter for CodeBrix.Platform applications, implementing
/// <see cref="ITouchAdapter"/> by translating <see cref="UIElement.PointerPressed"/>,
/// <see cref="UIElement.PointerMoved"/>, <see cref="UIElement.PointerReleased"/>, and
/// <see cref="UIElement.PointerCaptureLost"/> events into engine touch state. Events are not raised
/// directly; the <see cref="CodeBrix.Platform.GameEngine.Input.Touch.TouchEventPoller"/> polls this
/// adapter each engine frame to detect transitions and raise events.
/// </summary>
/// <remarks>
/// <para>
/// On physical touch devices, each finger contact is tracked by the pointer ID and exposed as a
/// unique <see cref="TouchPoint"/>. On desktop platforms with no hardware touch screen, mouse
/// pointer events are emulated as a single touch point with <c>Id = 0</c>.
/// </para>
/// <para>
/// Dispose this adapter to unsubscribe from all pointer events.
/// </para>
/// </remarks>
public sealed class CodeBrixTouchInputAdapter : ITouchAdapter, IDisposable
{
    private readonly UIElement _element;
    private readonly Dictionary<int, TouchPoint> _activeTouches = new();
    private TouchPoint[] _activeTouchesSnapshot = Array.Empty<TouchPoint>();
    private readonly ConcurrentQueue<TouchPoint> _pendingEnds = new();
    private bool _isDisposed;

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ActiveTouches => _activeTouchesSnapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixTouchInputAdapter"/> class,
    /// attaching pointer event handlers to the specified element.
    /// </summary>
    /// <param name="element">
    /// The element whose pointer events will be translated into touch events. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is <see langword="null"/>.</exception>
    public CodeBrixTouchInputAdapter(UIElement element)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));

        _element.PointerPressed += OnPointerPressed;
        _element.PointerMoved += OnPointerMoved;
        _element.PointerReleased += OnPointerReleased;
        _element.PointerCaptureLost += OnPointerCaptureLost;
        _element.PointerCanceled += OnPointerCaptureLost;

        Engine.Logger.LogInformation("CodeBrixTouchInputAdapter initialized.");
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_element);

        // For mouse pointers, only emulate touch for the primary (left) button.
        if (IsMouse(e) && !point.Properties.IsLeftButtonPressed)
            return;

        var id = GetTouchId(e);
        var touch = new TouchPoint(id, ToPoint(point.Position), TouchPhase.Began);

        _activeTouches[id] = touch;
        RebuildSnapshot();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var id = GetTouchId(e);

        // Only track movement for contacts that are already active (button/finger held).
        if (!_activeTouches.ContainsKey(id))
            return;

        var point = e.GetCurrentPoint(_element);
        var touch = new TouchPoint(id, ToPoint(point.Position), TouchPhase.Moved);

        _activeTouches[id] = touch;
        RebuildSnapshot();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var id = GetTouchId(e);

        if (!_activeTouches.ContainsKey(id))
            return;

        var point = e.GetCurrentPoint(_element);
        var touch = new TouchPoint(id, ToPoint(point.Position), TouchPhase.Ended);

        _activeTouches.Remove(id);
        RebuildSnapshot();
        _pendingEnds.Enqueue(touch);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // The system cancelled the pointer.
        var id = GetTouchId(e);

        if (!_activeTouches.TryGetValue(id, out var existing))
            return;

        var touch = new TouchPoint(existing.Id, existing.Position, TouchPhase.Cancelled);
        _activeTouches.Remove(id);
        RebuildSnapshot();
        _pendingEnds.Enqueue(touch);
    }

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ConsumeEndedTouches()
    {
        if (_pendingEnds.IsEmpty)
            return Array.Empty<TouchPoint>();

        var snapshot = new List<TouchPoint>(_pendingEnds.Count);
        while (_pendingEnds.TryDequeue(out var touch))
            snapshot.Add(touch);
        return snapshot;
    }

    private void RebuildSnapshot()
    {
        _activeTouchesSnapshot = _activeTouches.Values.ToArray();
    }

    private static bool IsMouse(PointerRoutedEventArgs e)
        => e.Pointer.PointerDeviceType == global::Microsoft.UI.Input.PointerDeviceType.Mouse;

    /// <summary>
    /// Maps a pointer to a stable integer touch ID. On desktop (mouse), all contacts map to <c>0</c>;
    /// on touch and stylus devices, the platform pointer ID is used directly.
    /// </summary>
    private static int GetTouchId(PointerRoutedEventArgs e)
        => IsMouse(e) ? 0 : (int)e.Pointer.PointerId;

    private static Point ToPoint(global::Windows.Foundation.Point position)
        => new Point((int)position.X, (int)position.Y);

    /// <summary>
    /// Releases all resources held by this adapter, unsubscribing from all pointer events.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _element.PointerPressed -= OnPointerPressed;
        _element.PointerMoved -= OnPointerMoved;
        _element.PointerReleased -= OnPointerReleased;
        _element.PointerCaptureLost -= OnPointerCaptureLost;
        _element.PointerCanceled -= OnPointerCaptureLost;

        Engine.Logger.LogInformation("CodeBrixTouchInputAdapter disposed.");
    }
}
