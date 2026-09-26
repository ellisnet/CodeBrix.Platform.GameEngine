using System;
using System.Collections.Generic;
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
/// <para>
/// Keys the game uses are marked handled (<see cref="KeyRoutedEventArgs.Handled"/>) as they arrive, so while
/// the game surface has focus they stay with the game: they do not bubble on to the application's
/// keyboard accelerators (a menu item's Ctrl+S, say) or to Tab / arrow-key focus navigation, which would
/// move focus off the surface. A key is used when it is registered for monitoring on the
/// <see cref="KeyboardEventPoller"/> this adapter feeds (<see cref="KeyboardEventPoller.StartMonitoringKey"/>,
/// <see cref="KeyboardEventPoller.StartMonitoringAllKeys"/>, ...) or claimed with <see cref="ClaimKey"/> /
/// <see cref="ClaimKeys"/> - the way a game that only polls <see cref="IsDown"/> declares its keys. Every
/// other key passes through unhandled, so application shortcuts keep working while the game has focus.
/// Set <see cref="MarkUsedKeysHandled"/> to <see langword="false"/> to leave every key unhandled.
/// The claim methods implement <see cref="IKeyClaimingAdapter"/>, through which an
/// <see cref="CodeBrix.Platform.GameEngine.Input.Actions.InputActionMap"/> claims the keys of its binding profile.
/// </para>
/// <para>
/// Losing keyboard focus releases every held key: a key released while focus is elsewhere (in a menu, a
/// dialog, another window) never sends its KeyUp here and would otherwise stay down.
/// </para>
/// </remarks>
public sealed class CodeBrixKeyboardAdapter : IKeyboardAdapter, IKeyClaimingAdapter, IDisposable
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

    private readonly KeyClaims _claims = new();

    private bool _isDisposed;

    /// <summary>
    /// Gets or sets whether keys the game uses are marked handled as they arrive (see the class remarks).
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool MarkUsedKeysHandled { get; set; } = true;

    /// <summary>
    /// Declares that the game uses <paramref name="keyCode"/> (a <see cref="VirtualKey"/> value cast to
    /// <see cref="int"/>), so it is marked handled while the game surface has focus. Keys registered for
    /// monitoring on the <see cref="KeyboardEventPoller"/> are used already and need no claim.
    /// </summary>
    /// <param name="keyCode">A <see cref="VirtualKey"/> value cast to <see cref="int"/>.</param>
    public void ClaimKey(int keyCode) => _claims.Claim(keyCode);

    /// <summary>
    /// Declares that the game uses every key in <paramref name="keyCodes"/> - a polling game's whole
    /// binding set, for example. Equivalent to calling <see cref="ClaimKey"/> for each code.
    /// </summary>
    /// <param name="keyCodes"><see cref="VirtualKey"/> values cast to <see cref="int"/>.</param>
    public void ClaimKeys(IEnumerable<int> keyCodes) => _claims.Claim(keyCodes);

    /// <summary>
    /// Withdraws a claim made with <see cref="ClaimKey"/> or <see cref="ClaimKeys"/>. A key that is also
    /// registered for monitoring stays used.
    /// </summary>
    /// <param name="keyCode">A <see cref="VirtualKey"/> value cast to <see cref="int"/>.</param>
    public void UnclaimKey(int keyCode) => _claims.Unclaim(keyCode);

    /// <summary>
    /// Withdraws every claim made with <see cref="ClaimKey"/> or <see cref="ClaimKeys"/>.
    /// </summary>
    public void UnclaimAllKeys() => _claims.UnclaimAll();

    /// <summary>
    /// Returns <see langword="true"/> if the game uses <paramref name="keyCode"/>: it is claimed, or it is
    /// registered for monitoring on the <see cref="KeyboardEventPoller"/> this adapter feeds.
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="keyCode">A <see cref="VirtualKey"/> value cast to <see cref="int"/>.</param>
    /// <returns><see langword="true"/> if the game uses the key; otherwise <see langword="false"/>.</returns>
    public bool IsKeyUsed(int keyCode) => _claims.IsUsed(keyCode, this, KeyboardEventPoller.Instance);

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
        _element.LostFocus += OnLostFocus;
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

    // A key released after focus has moved away (into a menu, a dialog, another window) never sends its
    // KeyUp to this element, so it would stay down for good - a stuck Alt, a stuck movement key. Losing
    // focus releases every held key, through the same deferred release as OnKeyUp, so a KeyDown for a
    // key still physically held (focus coming straight back) keeps it down.
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;

        var anyReleased = false;
        for (var keyCode = 0; keyCode < _down.Length; keyCode++)
        {
            if (Volatile.Read(ref _down[keyCode]) == 1)
            {
                Volatile.Write(ref _down[keyCode], 2);
                anyReleased = true;
            }
        }

        if (anyReleased)
            ScheduleFinalizeReleases();
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
        MarkHandledIfUsed(e);
    }

    // A key the game uses stays with the game (see the class remarks).
    private void MarkHandledIfUsed(KeyRoutedEventArgs e)
    {
        if (MarkUsedKeysHandled && IsKeyUsed((int)e.Key))
            e.Handled = true;
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
        MarkHandledIfUsed(e);
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
        _element.LostFocus -= OnLostFocus;
        _element.PointerPressed -= OnPointerPressed;
        if (_element is FrameworkElement frameworkElement)
            frameworkElement.Loaded -= OnLoaded;

        Engine.Logger.LogInformation("CodeBrixKeyboardAdapter disposed.");
    }
}
