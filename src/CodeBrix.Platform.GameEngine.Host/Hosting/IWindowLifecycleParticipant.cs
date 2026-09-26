using CodeBrix.Platform.GameEngine.Host.Rendering;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// A game host that <see cref="GameWindowLifecycle"/> tells about its window: implemented by
/// <see cref="CodeBrixGameHost"/> and <see cref="SoftwareRenderedGameHostBase"/>, which forward each call to
/// their protected virtual hooks. Every call arrives on the UI thread.
/// </summary>
internal interface IWindowLifecycleParticipant
{
    /// <summary>Gets the canvas the host renders to, used to match the host to a window and to refocus it.</summary>
    GameSurfaceCanvas? LifecycleSurface { get; }

    /// <summary>The window was minimized or otherwise hidden.</summary>
    void NotifyWindowHidden();

    /// <summary>The window is visible again.</summary>
    void NotifyWindowShown();

    /// <summary>The window was activated (it has input focus again).</summary>
    /// <param name="refocusSurface">Whether to hand keyboard focus back to the host's canvas first.</param>
    void NotifyWindowActivated(bool refocusSurface);

    /// <summary>The window lost activation (another window has input focus).</summary>
    void NotifyWindowDeactivated();
}
