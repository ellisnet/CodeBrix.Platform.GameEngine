using System;
using System.Threading;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using Microsoft.Extensions.Logging;
using Windows.Devices.Input;
using Windows.Foundation;

// The relative-mouse surface (MouseDevice.MouseMoved) is implemented by current
// CodeBrix.Platform sources; against older platform packages the members are
// [NotImplemented] stubs, which BeginCore already handles at runtime
// (NotImplementedException -> session stays inactive, game runs keyboard-only). The
// analyzer's not-implemented flag on these references is therefore expected here.
#pragma warning disable Uno0001

namespace CodeBrix.Platform.GameEngine.Host.Input.Mouse;

/// <summary>
/// An FPS-style relative mouse session ("mouse look") for software-rendered games:
/// <see cref="Begin"/> hides the cursor over the game surface, confines the pointer, and
/// starts accumulating raw motion deltas; the game thread calls <see cref="ConsumeDelta"/>
/// once per tic; <see cref="End"/> restores everything. This maps 1:1 onto the classic
/// grab-mouse / release-mouse + per-tic delta-read model.
/// </summary>
/// <remarks>
/// <para>
/// Built on CodeBrix.Platform's <c>MouseDevice.MouseMoved</c> relative-mouse surface:
/// subscribing hides nothing by itself but confines the pointer and delivers unbounded raw
/// deltas while the session is active. On platform heads (or CodeBrix.Platform versions)
/// without relative-mouse support, <see cref="Begin"/> logs an error and the session stays
/// inactive — the game keeps running keyboard-only.
/// </para>
/// <para>
/// <see cref="Begin"/> and <see cref="End"/> marshal themselves to the UI thread;
/// <see cref="ConsumeDelta"/> and <see cref="IsActive"/> are safe from any thread.
/// </para>
/// </remarks>
public sealed class RelativeMouseSession : IDisposable
{
    private readonly GameSurfaceCanvas _canvas;
    private readonly TypedEventHandler<MouseDevice, MouseEventArgs> _onMouseMoved;

    private MouseDevice? _device;
    private volatile bool _isActive;
    private int _pendingDeltaX;
    private int _pendingDeltaY;

    /// <summary>
    /// Creates a session bound to the given game surface (whose cursor is hidden while the
    /// session is active).
    /// </summary>
    /// <param name="renderSurface">The game surface canvas.</param>
    public RelativeMouseSession(GameSurfaceCanvas renderSurface)
    {
        _canvas = renderSurface ?? throw new ArgumentNullException(nameof(renderSurface));
        _onMouseMoved = OnMouseMoved;
    }

    /// <summary>True while the session is active (between <see cref="Begin"/> and <see cref="End"/>).</summary>
    public bool IsActive => _isActive;

    /// <summary>
    /// Starts the session: hides the cursor over the game surface, confines the pointer, and
    /// begins accumulating deltas. Safe to call from any thread; no-op when already active.
    /// </summary>
    public void Begin()
    {
        RunOnUiThread(BeginCore);
    }

    /// <summary>
    /// Ends the session: releases confinement, stops delta delivery, and restores the
    /// cursor. Safe to call from any thread; no-op when not active.
    /// </summary>
    public void End()
    {
        RunOnUiThread(EndCore);
    }

    /// <summary>
    /// Returns the mouse motion accumulated since the previous call and resets the
    /// accumulator — call once per tic from the game thread. Zero while the session is
    /// inactive.
    /// </summary>
    public (int DeltaX, int DeltaY) ConsumeDelta()
        => (Interlocked.Exchange(ref _pendingDeltaX, 0), Interlocked.Exchange(ref _pendingDeltaY, 0));

    /// <summary>Ends the session if active.</summary>
    public void Dispose() => End();

    private void BeginCore()
    {
        if (_isActive)
        {
            return;
        }

        try
        {
            _device = MouseDevice.GetForCurrentView();
            _device.MouseMoved += _onMouseMoved; // first subscriber: confinement + raw deltas activate
        }
        catch (NotImplementedException)
        {
            _device = null;
            Engine.Logger.LogError(
                "Relative mouse (MouseDevice.MouseMoved) is not implemented by this CodeBrix.Platform version/head; the relative mouse session stays inactive.");
            return;
        }

        _canvas.SetPointerCursorHidden(true);
        Interlocked.Exchange(ref _pendingDeltaX, 0);
        Interlocked.Exchange(ref _pendingDeltaY, 0);
        _isActive = true;
    }

    private void EndCore()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;

        if (_device is { } device)
        {
            device.MouseMoved -= _onMouseMoved; // last subscriber: confinement releases
            _device = null;
        }

        _canvas.SetPointerCursorHidden(false);
    }

    private void OnMouseMoved(MouseDevice sender, MouseEventArgs args)
    {
        Interlocked.Add(ref _pendingDeltaX, args.MouseDelta.X);
        Interlocked.Add(ref _pendingDeltaY, args.MouseDelta.Y);
    }

    private void RunOnUiThread(Action action)
    {
        var dispatcherQueue = _canvas.DispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ = dispatcherQueue.TryEnqueue(() => action());
        }
    }
}
