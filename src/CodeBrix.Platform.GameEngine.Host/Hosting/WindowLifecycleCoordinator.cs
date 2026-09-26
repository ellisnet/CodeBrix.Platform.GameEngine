using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// The decisions behind <see cref="GameWindowLifecycle"/>, free of any window: what to pause, what to resume
/// and whom to tell, given visibility and activation changes. The engine pause and the participant list are
/// passed in, so it can be exercised without a platform head. Every call is expected on the UI thread.
/// </summary>
internal sealed class WindowLifecycleCoordinator
{
    private readonly Func<bool> _isEnginePaused;
    private readonly Action _pauseEngine;
    private readonly Action _resumeEngine;
    private readonly Func<IReadOnlyList<IWindowLifecycleParticipant>> _participants;

    private bool _hidden;
    private bool _pausedByLifecycle;

    /// <summary>Creates the coordinator.</summary>
    /// <param name="isEnginePaused">Returns whether the engine is globally paused.</param>
    /// <param name="pauseEngine">Pauses the engine globally.</param>
    /// <param name="resumeEngine">Resumes the engine.</param>
    /// <param name="participants">Returns the hosts that belong to the window.</param>
    internal WindowLifecycleCoordinator(
        Func<bool> isEnginePaused,
        Action pauseEngine,
        Action resumeEngine,
        Func<IReadOnlyList<IWindowLifecycleParticipant>> participants)
    {
        _isEnginePaused = isEnginePaused ?? throw new ArgumentNullException(nameof(isEnginePaused));
        _pauseEngine = pauseEngine ?? throw new ArgumentNullException(nameof(pauseEngine));
        _resumeEngine = resumeEngine ?? throw new ArgumentNullException(nameof(resumeEngine));
        _participants = participants ?? throw new ArgumentNullException(nameof(participants));
    }

    /// <summary>Gets or sets whether hiding the window pauses the engine. Defaults to <see langword="true"/>.</summary>
    internal bool PauseWhenHidden { get; set; } = true;

    /// <summary>Gets or sets whether activating the window refocuses the hosts' canvases. Defaults to <see langword="true"/>.</summary>
    internal bool RefocusOnActivate { get; set; } = true;

    /// <summary>Gets whether the window is currently hidden, as last reported.</summary>
    internal bool IsHidden => _hidden;

    /// <summary>Gets whether the engine pause in force was made by this coordinator (and will be lifted by it).</summary>
    internal bool PausedByLifecycle => _pausedByLifecycle;

    /// <summary>
    /// Handles a visibility change. Hidden: the hosts' hidden hooks run first (so a game can latch its own
    /// pause menu while its state is still live), then the engine pauses unless it is already paused. Shown:
    /// the engine resumes only if this coordinator paused it (a pause the game made itself is left alone),
    /// then the hosts' shown hooks run. Repeated reports of the same state are ignored.
    /// </summary>
    /// <param name="visible">Whether the window is now visible.</param>
    internal void OnVisibilityChanged(bool visible)
    {
        if (visible)
        {
            if (!_hidden)
                return;

            _hidden = false;

            if (_pausedByLifecycle)
            {
                _pausedByLifecycle = false;
                _resumeEngine();
            }

            ForEach(participant => participant.NotifyWindowShown());
            return;
        }

        if (_hidden)
            return;

        _hidden = true;

        ForEach(participant => participant.NotifyWindowHidden());

        if (PauseWhenHidden && !_isEnginePaused())
        {
            _pausedByLifecycle = true;
            _pauseEngine();
        }
    }

    /// <summary>
    /// Handles an activation change: activated tells every host (refocusing its canvas first when
    /// <see cref="RefocusOnActivate"/> is set); deactivated tells every host so.
    /// </summary>
    /// <param name="activated">Whether the window was activated (as opposed to deactivated).</param>
    internal void OnActivationChanged(bool activated)
    {
        if (activated)
        {
            bool refocus = RefocusOnActivate;
            ForEach(participant => participant.NotifyWindowActivated(refocus));
        }
        else
        {
            ForEach(participant => participant.NotifyWindowDeactivated());
        }
    }

    // One host's failing hook must not stop the others hearing about the change, nor stop the pause.
    private void ForEach(Action<IWindowLifecycleParticipant> notify)
    {
        foreach (var participant in _participants())
        {
            try
            {
                notify(participant);
            }
            catch (Exception failure)
            {
                Engine.Logger.LogError(failure, "A game host's window lifecycle hook threw.");
            }
        }
    }
}
