using System;
using CodeBrix.Platform.GameEngine.Host.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Host.Input.Mouse;
using CodeBrix.Platform.GameEngine.Host.Input.Touch;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.GameEngine.Host;

/// <summary>
/// Provides extension methods for wiring CodeBrix.Platform input adapters into the game engine.
/// </summary>
public static class EngineExtensions
{
    /// <summary>
    /// Initializes the CodeBrix.Platform keyboard adapter for the specified element and registers it
    /// with the <see cref="KeyboardEventPoller"/>. Key codes correspond to
    /// <see cref="global::Windows.System.VirtualKey"/> values cast to <see cref="int"/>.
    /// </summary>
    /// <param name="engine">The engine instance to configure.</param>
    /// <param name="element">The element (typically the game surface canvas) to capture keyboard input from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null.</exception>
    public static void InitializeCodeBrixKeyboardAdapter(this Engine engine, UIElement element)
    {
        Engine.Logger.LogInformation("Initializing CodeBrixKeyboardAdapter...");

        if (element == null)
        {
            Engine.Logger.LogError("CodeBrixKeyboardAdapter initialization failed: element cannot be null.");
            throw new ArgumentNullException(nameof(element));
        }

        KeyboardEventPoller.Initialize(new CodeBrixKeyboardAdapter(element));
    }

    /// <summary>
    /// Initializes the CodeBrix.Platform mouse adapter for the specified element and registers it
    /// with the <see cref="MouseEventPoller"/>.
    /// </summary>
    /// <param name="engine">The engine instance to configure.</param>
    /// <param name="element">The element to capture mouse/pointer input from.</param>
    /// <param name="mouseEventConfiguration">Optional configuration for mouse event handling.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null.</exception>
    public static void InitializeCodeBrixMouseAdapter(this Engine engine, UIElement element, MouseEventConfiguration? mouseEventConfiguration = null)
    {
        Engine.Logger.LogInformation("Initializing CodeBrixMouseAdapter...");

        if (element == null)
        {
            Engine.Logger.LogError("CodeBrixMouseAdapter initialization failed: element cannot be null.");
            throw new ArgumentNullException(nameof(element));
        }

        MouseEventPoller.Initialize(new CodeBrixMouseAdapter(element), mouseEventConfiguration);
    }

    /// <summary>
    /// Initializes the CodeBrix.Platform touch adapter for the specified element and registers it with
    /// <see cref="Engine"/>'s input systems, enabling touch and pointer gesture input.
    /// </summary>
    /// <remarks>
    /// Mouse pointers are ignored by default, keeping mouse clicks distinct from physical touch
    /// contacts on desktop heads that also register a mouse adapter. Set <paramref name="emulateMouse"/>
    /// to <c>true</c> only when a mouse should also produce touch ID 0. After calling this method,
    /// access the touch system via <c>engine.Input.TouchEventPoller</c> and attach gesture recognizers
    /// from the <c>CodeBrix.Platform.GameEngine.Input.Touch.Gestures</c> namespace.
    /// </remarks>
    /// <param name="engine">The engine instance to configure.</param>
    /// <param name="element">The element to capture pointer/touch input from.</param>
    /// <param name="emulateMouse">Whether primary mouse input should also emulate touch input.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null.</exception>
    public static void InitializeCodeBrixTouchAdapter(
        this Engine engine,
        UIElement element,
        bool emulateMouse = false)
    {
        Engine.Logger.LogInformation("Initializing CodeBrixTouchInputAdapter...");

        if (element == null)
        {
            Engine.Logger.LogError("CodeBrixTouchInputAdapter initialization failed: element cannot be null.");
            throw new ArgumentNullException(nameof(element));
        }

        engine.Input.TouchAdapter = new CodeBrixTouchInputAdapter(element, emulateMouse);
    }
}
