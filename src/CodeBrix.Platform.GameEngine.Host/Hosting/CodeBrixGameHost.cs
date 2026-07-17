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
public abstract class CodeBrixGameHost : GameHostBase
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
    /// Configures the touch adapter for the render surface.
    /// </summary>
    protected sealed override void ConfigureTouch()
    {
        Engine.InitializeCodeBrixTouchAdapter(RenderSurface);
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

        // Notify the game whenever the render surface (and its backbuffer) is resized, so
        // size-anchored content — HUD/score overlays and the like — can be repositioned.
        RenderSurface.RenderSurfaceAdapter.Resized += OnRenderSurfaceAdapterResized;

        OnSceneBound();
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
    /// Called on the UI thread whenever the render surface — and therefore the backbuffer — changes
    /// size. Override to reposition size-anchored content (for example HUD or score overlays that are
    /// pinned to a window edge or corner). The base implementation does nothing.
    /// </summary>
    /// <param name="width">The new render surface width, in pixels.</param>
    /// <param name="height">The new render surface height, in pixels.</param>
    protected virtual void OnRenderSurfaceResized(int width, int height)
    {
    }

    /// <inheritdoc />
    protected override void OnDisposing()
    {
        RenderSurface.RenderSurfaceAdapter.Resized -= OnRenderSurfaceAdapterResized;
        base.OnDisposing();
    }
}
