using System;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Rendering;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// Provides a base class for hosting CodeBrix.Platform.GameEngine games on a CodeBrix.Platform surface.
/// Supplies the render surface and the CodeBrix.Platform keyboard/mouse/touch input adapters; game
/// applications derive from this class and override the content-creation hooks from
/// <see cref="GameHostBase"/> (for example <see cref="GameHostBase.LoadAssets"/>,
/// <see cref="GameHostBase.CreateInitialScene"/>, and <see cref="GameHostBase.CreateSprites"/>).
/// </summary>
public abstract class CodeBrixGameHost : GameHostBase, IWindowLifecycleParticipant
{
    /// <summary>
    /// Gets the render surface control used for displaying game content and capturing input.
    /// </summary>
    public GameSurfaceCanvas RenderSurface { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixGameHost"/> class.
    /// </summary>
    /// <param name="renderSurface">The render surface control to use for rendering and input.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderSurface"/> is null.</exception>
    protected CodeBrixGameHost(GameSurfaceCanvas renderSurface)
    {
        RenderSurface = renderSurface ?? throw new ArgumentNullException(nameof(renderSurface));
        WindowLifecycleParticipants.Add(this);
    }

    /// <summary>
    /// Configures CodeBrix.Platform-specific platform features.
    /// </summary>
    protected sealed override void ConfigurePlatform()
    {
        OnConfigurePlatform();
    }

    /// <summary>
    /// Configures the keyboard adapter for the render surface.
    /// </summary>
    protected sealed override void ConfigureKeyboard()
    {
        Engine.InitializeCodeBrixKeyboardAdapter(RenderSurface);
        OnKeyboardAdapterInitialized();
    }

    /// <summary>
    /// Configures the mouse adapter for the render surface.
    /// </summary>
    protected sealed override void ConfigureMouse()
    {
        Engine.InitializeCodeBrixMouseAdapter(RenderSurface);
        OnMouseAdapterInitialized();
    }

    /// <summary>
    /// Configures gamepad support.
    /// </summary>
    protected sealed override void ConfigureGamepads()
    {
        OnConfigureGamepads();
    }

    /// <summary>
    /// Gets a value indicating whether primary mouse input should also be delivered as touch
    /// contact ID 0. The default is <c>false</c>, so a desktop click raises mouse events only.
    /// Override and return <c>true</c> for a game that drives everything from the touch stream.
    /// </summary>
    protected virtual bool EmulateMouseAsTouch => false;

    /// <summary>
    /// Configures the touch adapter for the render surface.
    /// </summary>
    protected sealed override void ConfigureTouch()
    {
        Engine.InitializeCodeBrixTouchAdapter(RenderSurface, EmulateMouseAsTouch);
        OnTouchAdapterInitialized();
    }

    /// <summary>
    /// Binds the current scene to the render surface host.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no scene has been created.</exception>
    protected sealed override void BindScene()
    {
        var scene = Scene
            ?? throw new InvalidOperationException(
                $"{nameof(BindScene)} cannot be called before {nameof(Scene)} has been created.");

        RenderSurface.Host.Bind(scene, false);

        // Notify the game whenever the render surface is resized. The backbuffer keeps its
        // established resolution across a resize, so this is a presentation change; see
        // OnRenderSurfaceResized for what a game should re-anchor from.
        RenderSurface.RenderSurfaceAdapter.Resized += OnRenderSurfaceAdapterResized;

        // OnSceneBound() is raised by GameHostBase.InitializeGameContent() immediately after this
        // method returns; raising it here as well made every subclass hook see the scene twice.
    }

    private void OnRenderSurfaceAdapterResized(RenderSurfaceAdapterResizedEventArgs args)
    {
        OnRenderSurfaceResized(args.NewWidth, args.NewHeight);
    }

    /// <summary>
    /// Provides a hook for configuring additional CodeBrix.Platform-specific settings during initialization.
    /// </summary>
    protected virtual void OnConfigurePlatform()
    {
    }

    /// <summary>
    /// Called after the keyboard adapter has been initialized.
    /// </summary>
    protected virtual void OnKeyboardAdapterInitialized()
    {
    }

    /// <summary>
    /// Called after the mouse adapter has been initialized.
    /// </summary>
    protected virtual void OnMouseAdapterInitialized()
    {
    }

    /// <summary>
    /// Provides a hook for configuring gamepad support.
    /// </summary>
    protected virtual void OnConfigureGamepads()
    {
    }

    /// <summary>
    /// Called after the touch adapter has been initialized.
    /// </summary>
    protected virtual void OnTouchAdapterInitialized()
    {
    }

    /// <summary>
    /// Called on the UI thread whenever the render surface changes size. Override to reposition
    /// size-anchored content (for example HUD or score overlays that are pinned to a window edge or
    /// corner). The base implementation does nothing.
    /// </summary>
    /// <remarks>
    /// The reported size is the surface's own, in the pixels it presents into — <em>not</em> the
    /// logical Backbuffer ScreenPx that drawings, views and pointer input use. A resize changes
    /// presentation only: the established render resolution, and therefore everything anchored to
    /// it, is unaffected. Anchor game content from <c>RenderSurface.Host.Backbuffer.Width</c> and
    /// <c>Height</c>, which change only on an explicit render-resolution change (
    /// <see cref="Rendering.GameSurfaceCanvas.SetRenderResolution"/>,
    /// <see cref="CodeBrix.Platform.GameEngine.Configuration.EngineConfiguration.RenderScale"/> or
    /// <see cref="Rendering.GameSurfaceCanvas.TrackWindowSize"/>).
    /// </remarks>
    /// <param name="width">The new render surface width, in the surface's own pixels.</param>
    /// <param name="height">The new render surface height, in the surface's own pixels.</param>
    protected virtual void OnRenderSurfaceResized(int width, int height)
    {
    }

    /// <summary>
    /// Called on the UI thread when the window this host's canvas is in was minimized or otherwise hidden,
    /// when the application wires the window with <see cref="GameWindowLifecycle.Attach"/>. Runs BEFORE the
    /// engine pauses, while the game is still live: the place to latch the game's own pause menu so it is
    /// showing when the player returns. May run before the host has initialized. The base implementation
    /// does nothing.
    /// </summary>
    protected virtual void OnWindowHidden()
    {
    }

    /// <summary>
    /// Called on the UI thread when the window is visible again (see <see cref="OnWindowHidden"/>), after
    /// the engine has resumed from a pause <see cref="GameWindowLifecycle"/> made. The base implementation
    /// does nothing.
    /// </summary>
    protected virtual void OnWindowShown()
    {
    }

    /// <summary>
    /// Called on the UI thread when the window is activated, after keyboard focus has been handed back to
    /// the canvas (when <see cref="GameWindowLifecycle.RefocusOnActivate"/> is on). Requires
    /// <see cref="GameWindowLifecycle.Attach"/>. The base implementation does nothing.
    /// </summary>
    protected virtual void OnWindowActivated()
    {
    }

    /// <summary>
    /// Called on the UI thread when the window loses activation (another window has input focus). Requires
    /// <see cref="GameWindowLifecycle.Attach"/>. The base implementation does nothing.
    /// </summary>
    protected virtual void OnWindowDeactivated()
    {
    }

    GameSurfaceCanvas? IWindowLifecycleParticipant.LifecycleSurface => RenderSurface;

    void IWindowLifecycleParticipant.NotifyWindowHidden() => OnWindowHidden();

    void IWindowLifecycleParticipant.NotifyWindowShown() => OnWindowShown();

    void IWindowLifecycleParticipant.NotifyWindowActivated(bool refocusSurface)
    {
        if (refocusSurface)
            GameWindowLifecycle.RefocusSurface(RenderSurface);
        OnWindowActivated();
    }

    void IWindowLifecycleParticipant.NotifyWindowDeactivated() => OnWindowDeactivated();

    /// <inheritdoc />
    protected override void OnDisposing()
    {
        WindowLifecycleParticipants.Remove(this);
        RenderSurface.RenderSurfaceAdapter.Resized -= OnRenderSurfaceAdapterResized;
        base.OnDisposing();
    }
}
