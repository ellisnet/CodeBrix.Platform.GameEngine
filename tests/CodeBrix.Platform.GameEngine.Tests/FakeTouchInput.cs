using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Input.Touch;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// A hand-driven <see cref="ITouchInput"/> source for gesture-recognizer tests. Each helper method
/// raises the matching lifecycle event with an explicit engine tick, so the recognizers can be
/// exercised deterministically without an engine loop.
/// </summary>
internal sealed class FakeTouchInput : ITouchInput
{
    private readonly Dictionary<int, TouchPoint> _active = new();

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ActiveTouches => _active.Values.ToArray();

    /// <inheritdoc />
    public event EventHandler<TouchEventArgs>? TouchBegan;

    /// <inheritdoc />
    public event EventHandler<TouchEventArgs>? TouchMoved;

    /// <inheritdoc />
    public event EventHandler<TouchEventArgs>? TouchEnded;

    /// <summary>Raises <see cref="TouchBegan"/> for a new contact.</summary>
    public void Begin(int id, Point position, long tick)
    {
        var point = new TouchPoint(id, position, TouchPhase.Began);
        _active[id] = point;
        TouchBegan?.Invoke(this, new TouchEventArgs(point, tick));
    }

    /// <summary>Raises <see cref="TouchMoved"/> for an existing contact.</summary>
    public void Move(int id, Point position, long tick)
    {
        var point = new TouchPoint(id, position, TouchPhase.Moved);
        _active[id] = point;
        TouchMoved?.Invoke(this, new TouchEventArgs(point, tick));
    }

    /// <summary>Raises <see cref="TouchEnded"/> for an existing contact.</summary>
    public void End(int id, Point position, long tick)
    {
        _active.Remove(id);
        TouchEnded?.Invoke(this, new TouchEventArgs(new TouchPoint(id, position, TouchPhase.Ended), tick));
    }
}

/// <summary>
/// A hand-driven <see cref="ITouchAdapter"/> that queues beginnings and endings the way the
/// platform adapters do, so poller tests can simulate contacts that begin and end between polls.
/// </summary>
internal sealed class FakeTouchAdapter : ITouchAdapter, IDisposable
{
    private readonly Dictionary<int, TouchPoint> _active = new();
    private readonly Queue<TouchPoint> _began = new();
    private readonly Queue<TouchPoint> _ended = new();

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ActiveTouches => _active.Values.ToArray();

    /// <summary>Gets a value indicating whether this adapter has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Queues a beginning and marks the contact active.</summary>
    public void Begin(int id, Point position)
    {
        var point = new TouchPoint(id, position, TouchPhase.Began);
        _active[id] = point;
        _began.Enqueue(point);
    }

    /// <summary>Moves an active contact without raising anything.</summary>
    public void Move(int id, Point position)
        => _active[id] = new TouchPoint(id, position, TouchPhase.Moved);

    /// <summary>Queues an ending and clears the contact.</summary>
    public void End(int id, Point position)
    {
        _active.Remove(id);
        _ended.Enqueue(new TouchPoint(id, position, TouchPhase.Ended));
    }

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ConsumeBeganTouches() => Drain(_began);

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ConsumeEndedTouches() => Drain(_ended);

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;

    private static IReadOnlyList<TouchPoint> Drain(Queue<TouchPoint> queue)
    {
        var result = queue.ToArray();
        queue.Clear();
        return result;
    }
}

/// <summary>
/// A minimal <see cref="ITouchAdapter"/> that exposes a fixed active-contact snapshot and relies on
/// the default (empty) beginning queue, mirroring an adapter that never queues beginnings.
/// </summary>
internal sealed class SnapshotOnlyTouchAdapter : ITouchAdapter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotOnlyTouchAdapter"/> class.
    /// </summary>
    /// <param name="points">The fixed set of active contacts to report.</param>
    public SnapshotOnlyTouchAdapter(params TouchPoint[] points) => ActiveTouches = points;

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ActiveTouches { get; }

    /// <inheritdoc />
    public IReadOnlyList<TouchPoint> ConsumeEndedTouches() => Array.Empty<TouchPoint>();
}
