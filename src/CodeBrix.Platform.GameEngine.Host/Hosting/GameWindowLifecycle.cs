using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// Opt-in window wiring for a game: pauses the engine while the window is minimized or hidden, resumes it
/// when the window is shown again, and hands keyboard focus back to the game canvas whenever the window is
/// activated. One call from the application, where the window is created; the game hosts react through
/// their own virtual hooks.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Attach"/> once per window, typically in <c>App.OnLaunched</c> right after creating the
/// window. It does not matter whether the game host exists yet: every <see cref="CodeBrixGameHost"/> and
/// <see cref="SoftwareRenderedGameHostBase"/> alive when a window event arrives is told about it, provided its
/// canvas is in that window (a canvas not yet in any window counts as in every window).
/// </para>
/// <para>
/// Hidden (minimized): the hosts' <c>OnWindowHidden</c> hooks run first, while the game is still live, so a
/// game can latch its own pause menu for the player's return; then the engine pauses globally
/// (<see cref="Engine.Pause"/>: the loop parks and audio suspends) unless it is already paused. Shown: the
/// engine resumes only if this helper paused it - a pause the game made itself stays - and then the hosts'
/// <c>OnWindowShown</c> hooks run. Workspace switches are not visibility changes and do not pause.
/// </para>
/// <para>
/// Activated: the hosts' canvases take keyboard focus (deferred to the dispatcher, so it lands after
/// whatever moved focus finishes) and their <c>OnWindowActivated</c> hooks run. Without this, alt-tabbing
/// away and back leaves keys going to whatever else held focus, and the keyboard seems dead until the canvas
/// is clicked. Deactivated: the hosts' <c>OnWindowDeactivated</c> hooks run. Keys held while focus leaves
/// the canvas are already released by the keyboard adapter itself.
/// </para>
/// <para>
/// Every hook runs on the UI thread. Dispose the returned object to detach from the window.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// protected override void OnLaunched(LaunchActivatedEventArgs args)
/// {
///     MainWindow = new Window { Title = "My Game" };
///     // ... navigate to the page that holds the game canvas ...
///     GameWindowLifecycle.Attach(MainWindow);
///     MainWindow.Activate();
/// }
/// </code>
/// </example>
public sealed class GameWindowLifecycle : IDisposable
{
    private readonly WindowLifecycleCoordinator _coordinator;
    private bool _disposed;

    private GameWindowLifecycle(Window window, bool pauseWhenHidden, bool refocusOnActivate)
    {
        Window = window;
        _coordinator = new WindowLifecycleCoordinator(
            () => Engine.Instance.IsPaused,
            () => Engine.Instance.Pause(),
            () => Engine.Instance.Resume(),
            ParticipantsInWindow)
        {
            PauseWhenHidden = pauseWhenHidden,
            RefocusOnActivate = refocusOnActivate,
        };

        Window.VisibilityChanged += OnVisibilityChanged;
        Window.Activated += OnActivated;
    }

    /// <summary>
    /// Attaches the lifecycle wiring to <paramref name="window"/> (see the class remarks).
    /// </summary>
    /// <param name="window">The application window that shows the game canvas.</param>
    /// <param name="pauseWhenHidden">
    /// Whether hiding (minimizing) the window pauses the engine. The hosts' hidden/shown hooks run either way.
    /// </param>
    /// <param name="refocusOnActivate">
    /// Whether activating the window hands keyboard focus back to the game canvas. The hosts' activation hooks
    /// run either way.
    /// </param>
    /// <returns>The attached lifecycle; dispose it to detach.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public static GameWindowLifecycle Attach(Window window, bool pauseWhenHidden = true, bool refocusOnActivate = true)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new GameWindowLifecycle(window, pauseWhenHidden, refocusOnActivate);
    }

    /// <summary>Gets the window this lifecycle is attached to.</summary>
    public Window Window { get; }

    /// <summary>
    /// Gets or sets whether hiding the window pauses the engine. Changing it while the window is hidden
    /// affects the next hide, not the current one.
    /// </summary>
    public bool PauseWhenHidden
    {
        get => _coordinator.PauseWhenHidden;
        set => _coordinator.PauseWhenHidden = value;
    }

    /// <summary>Gets or sets whether activating the window hands keyboard focus back to the game canvas.</summary>
    public bool RefocusOnActivate
    {
        get => _coordinator.RefocusOnActivate;
        set => _coordinator.RefocusOnActivate = value;
    }

    /// <summary>Gets whether the window is hidden, as last reported by the window.</summary>
    public bool IsWindowHidden => _coordinator.IsHidden;

    /// <summary>Detaches from the window. A pause this lifecycle made stays in force; resume it yourself if needed.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Window.VisibilityChanged -= OnVisibilityChanged;
        Window.Activated -= OnActivated;
    }

    private void OnVisibilityChanged(object sender, Windows.UI.Core.VisibilityChangedEventArgs e) =>
        _coordinator.OnVisibilityChanged(e.Visible);

    private void OnActivated(object sender, WindowActivatedEventArgs e) =>
        _coordinator.OnActivationChanged(
            e.WindowActivationState != Windows.UI.Core.CoreWindowActivationState.Deactivated);

    private IReadOnlyList<IWindowLifecycleParticipant> ParticipantsInWindow()
    {
        var windowRoot = Window.Content?.XamlRoot;
        var inWindow = new List<IWindowLifecycleParticipant>();

        foreach (var participant in WindowLifecycleParticipants.Snapshot())
        {
            var surfaceRoot = participant.LifecycleSurface?.XamlRoot;
            if (windowRoot is null || surfaceRoot is null || ReferenceEquals(windowRoot, surfaceRoot))
                inWindow.Add(participant);
        }

        return inWindow;
    }

    /// <summary>
    /// Hands keyboard focus to <paramref name="surface"/>, deferred to its dispatcher so it lands after
    /// whatever moved focus away finishes. Shared by the hosts' activation handling.
    /// </summary>
    /// <param name="surface">The canvas to focus.</param>
    internal static void RefocusSurface(GameSurfaceCanvas? surface)
    {
        if (surface is null)
            return;

        if (surface.DispatcherQueue is { } dispatcher)
            dispatcher.TryEnqueue(() => surface.Focus(FocusState.Programmatic));
        else
            surface.Focus(FocusState.Programmatic);
    }
}
