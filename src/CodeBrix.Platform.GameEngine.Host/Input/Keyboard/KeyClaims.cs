using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Input.Keyboard;

namespace CodeBrix.Platform.GameEngine.Host.Input.Keyboard;

/// <summary>
/// The key codes a game has claimed on a <see cref="CodeBrixKeyboardAdapter"/>, plus the rule that decides
/// whether the game uses a key: a key is used when it is claimed, or when it is registered for monitoring
/// on the <see cref="KeyboardEventPoller"/> that the adapter feeds. Safe to use from any thread.
/// </summary>
internal sealed class KeyClaims
{
    private readonly ConcurrentDictionary<int, byte> _claimed = new();

    public void Claim(int keyCode) => _claimed[keyCode] = 0;

    public void Claim(IEnumerable<int> keyCodes)
    {
        ArgumentNullException.ThrowIfNull(keyCodes);

        foreach (var keyCode in keyCodes)
        {
            Claim(keyCode);
        }
    }

    public void Unclaim(int keyCode) => _claimed.TryRemove(keyCode, out _);

    public void UnclaimAll() => _claimed.Clear();

    public bool IsClaimed(int keyCode) => _claimed.ContainsKey(keyCode);

    /// <summary>
    /// Returns <see langword="true"/> if the game uses <paramref name="keyCode"/>: it is claimed, or it is
    /// monitored by <paramref name="poller"/> and that poller is fed by <paramref name="adapter"/> (a poller
    /// fed by some other adapter says nothing about this one's keys).
    /// </summary>
    public bool IsUsed(int keyCode, IKeyboardAdapter adapter, KeyboardEventPoller? poller) =>
        IsClaimed(keyCode) ||
        (poller is not null && ReferenceEquals(poller.Adapter, adapter) && poller.IsMonitoringKey(keyCode));
}
