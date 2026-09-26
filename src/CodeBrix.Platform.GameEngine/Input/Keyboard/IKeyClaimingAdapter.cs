using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Input.Keyboard; //CodeBrix (not from Gondwana)

/// <summary>
/// A keyboard adapter that lets a game declare the keys it uses (key claims), so the host passes those keys
/// to the game and leaves every other key to the application. The Host package's keyboard adapter implements
/// it; an <see cref="Actions.InputActionMap"/> uses it to claim the keys its bindings name.
/// </summary>
public interface IKeyClaimingAdapter
{
    /// <summary>Declares that the game uses every key in <paramref name="keyCodes"/>.</summary>
    /// <param name="keyCodes">The key codes (the same codes <see cref="IKeyboardAdapter.IsDown"/> takes).</param>
    void ClaimKeys(IEnumerable<int> keyCodes);

    /// <summary>Withdraws a claim made with <see cref="ClaimKeys"/>.</summary>
    /// <param name="keyCode">The key code.</param>
    void UnclaimKey(int keyCode);

    /// <summary>
    /// Returns <see langword="true"/> if the game uses <paramref name="keyCode"/> already: it is claimed, or it
    /// is registered for monitoring on the <see cref="KeyboardEventPoller"/> the adapter feeds.
    /// </summary>
    /// <param name="keyCode">The key code.</param>
    /// <returns><see langword="true"/> if the key is used; otherwise <see langword="false"/>.</returns>
    bool IsKeyUsed(int keyCode);
}
