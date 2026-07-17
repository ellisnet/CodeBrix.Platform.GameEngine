using System;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodeBrix.Platform.GameEngine.Host.Input.Keyboard;

/// <summary>
/// CodeBrix.Platform key-state collector that feeds the
/// <see cref="CodeBrix.Platform.GameEngine.Input.Keyboard.KeyboardEventPoller"/>.
/// Tracks key up/down state and the active modifier keys on a focusable
/// <see cref="UIElement"/> (typically the game surface canvas). Key codes are
/// <see cref="VirtualKey"/> values cast to <see cref="int"/>; because <see cref="VirtualKey"/>
/// uses the Windows Virtual-Key numbering, these match the codes the engine expects.
/// </summary>
/// <remarks>
/// A CodeBrix.Platform panel/canvas is not focusable by default and only receives
/// <see cref="UIElement.KeyDown"/>/<see cref="UIElement.KeyUp"/> while it holds keyboard focus.
/// This adapter sets <see cref="UIElement.IsTabStop"/> and grabs focus on load and on pointer press,
/// so clicking the game surface restores keyboard input.
/// </remarks>
public sealed class CodeBrixKeyboardAdapter : IKeyboardAdapter, IDisposable
{
    /// <summary>
    /// Converts a <see cref="VirtualKey"/> name (case-insensitive) to its integer key code.
    /// </summary>
    /// <param name="keyName">The name of the key, matching a <see cref="VirtualKey"/> enumeration value.</param>
    /// <returns>The integer key code if the name is valid; otherwise <c>null</c>.</returns>
    public static int? GetKeyCodeFromString(string keyName)
    {
        if (Enum.TryParse<VirtualKey>(keyName, true, out var key))
            return (int)key;

        Engine.Logger.LogWarning("Invalid CodeBrix.Platform key name: {KeyName}", keyName);
        return null;
    }

    private readonly UIElement _element;

    // Key state table indexed by (int)VirtualKey: 0 = up, 1 = down, 2 = release pending
    // (see OnKeyUp for the release+press coalescing).
    private readonly int[] _down = new int[512];

    // Modifier bits published lock-free.
    private int _modsBits;

    // Deferred-release machinery (UI thread), see OnKeyUp.
    private Microsoft.UI.Dispatching.DispatcherQueueHandler? _finalizeReleasesHandler;
    private int _finalizeScheduled;

    private bool _isDisposed;

    /// <summary>
    /// Gets the current state of keyboard modifiers (Shift, Ctrl, Alt).
    /// </summary>
    public KeyboardModifierState CurrentKeyboardModifiers =>
        (KeyboardModifierState)Volatile.Read(ref _modsBits);

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixKeyboardAdapter"/> class.
    /// </summary>
    /// <param name="element">
    /// The focusable element (typically the game surface canvas) that receives keyboard input.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="element"/> is null.</exception>
    public CodeBrixKeyboardAdapter(UIElement element)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));

        _element.IsTabStop = true;
        _element.KeyDown += OnKeyDown;
        _element.KeyUp += OnKeyUp;
        _element.PointerPressed += OnPointerPressed;

        if (_element is FrameworkElement frameworkElement)
            frameworkElement.Loaded += OnLoaded;

        Engine.Logger.LogInformation("CodeBrixKeyboardAdapter initialized.");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isDisposed)
            _element.Focus(FocusState.Programmatic);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Restore keyboard focus to the game surface when it is clicked.
        if (!_isDisposed)
            _element.Focus(FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isDisposed) return;
        SetDown((int)e.Key, true);
        RecomputeMods();
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_isDisposed) return;

        // Same-batch release+press coalescing (defense-in-depth against key-repeat schemes
        // that deliver synthetic KeyUp/KeyDown pairs for held keys): instead of clearing the
        // key immediately, mark the release pending and finalize it on a later dispatcher
        // pass. A KeyDown for the same key arriving in the same input batch overwrites the
        // pending mark, so a game thread polling IsDown never observes a phantom release of
        // a key that is physically held. Real releases become visible one dispatcher hop
        // (typically well under a millisecond) later.
        var keyCode = (int)e.Key;
        if ((uint)keyCode < (uint)_down.Length)
            Volatile.Write(ref _down[keyCode], 2);

        RecomputeMods();
        ScheduleFinalizeReleases();
    }

    private void ScheduleFinalizeReleases()
    {
        if (Interlocked.CompareExchange(ref _finalizeScheduled, 1, 0) != 0) return;

        _finalizeReleasesHandler ??= FinalizeReleases;
        if (_element.DispatcherQueue is { } dispatcherQueue)
        {
            dispatcherQueue.TryEnqueue(_finalizeReleasesHandler);
        }
        else
        {
            // No dispatcher (headless usage): finalize immediately.
            FinalizeReleases();
        }
    }

    private void FinalizeReleases()
    {
        Interlocked.Exchange(ref _finalizeScheduled, 0);
        for (var keyCode = 0; keyCode < _down.Length; keyCode++)
        {
            if (Volatile.Read(ref _down[keyCode]) == 2)
                Volatile.Write(ref _down[keyCode], 0);
        }
        RecomputeMods();
    }

    /// <summary>
    /// Returns <see langword="true"/> if the key represented by <paramref name="keyCode"/>
    /// (a <see cref="VirtualKey"/> value cast to <see cref="int"/>) is currently pressed.
    /// </summary>
    /// <param name="keyCode">A <see cref="VirtualKey"/> value cast to <see cref="int"/>.</param>
    /// <returns><see langword="true"/> if the key is currently down; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDown(int keyCode)
    {
        if ((uint)keyCode >= (uint)_down.Length) return false;
        return Volatile.Read(ref _down[keyCode]) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetDown(int keyCode, bool down)
    {
        if ((uint)keyCode >= (uint)_down.Length) return;
        Volatile.Write(ref _down[keyCode], down ? 1 : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsRawDown(VirtualKey key) => IsDown((int)key);

    private void RecomputeMods()
    {
        // KeyRoutedEventArgs.KeyboardModifiers is internal on CodeBrix.Platform, so derive the
        // modifier state from the tracked key-down table instead.
        int mods = 0;
        if (IsRawDown(VirtualKey.Shift) || IsRawDown(VirtualKey.LeftShift) || IsRawDown(VirtualKey.RightShift))
            mods |= (int)KeyboardModifierState.Shift;
        if (IsRawDown(VirtualKey.Control) || IsRawDown(VirtualKey.LeftControl) || IsRawDown(VirtualKey.RightControl))
            mods |= (int)KeyboardModifierState.Ctrl;
        if (IsRawDown(VirtualKey.Menu) || IsRawDown(VirtualKey.LeftMenu) || IsRawDown(VirtualKey.RightMenu))
            mods |= (int)KeyboardModifierState.Alt;
        Volatile.Write(ref _modsBits, mods);
    }

    /// <summary>
    /// Releases all resources and removes all event handlers registered by this adapter.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _element.KeyDown -= OnKeyDown;
        _element.KeyUp -= OnKeyUp;
        _element.PointerPressed -= OnPointerPressed;
        if (_element is FrameworkElement frameworkElement)
            frameworkElement.Loaded -= OnLoaded;

        Engine.Logger.LogInformation("CodeBrixKeyboardAdapter disposed.");
    }
}
