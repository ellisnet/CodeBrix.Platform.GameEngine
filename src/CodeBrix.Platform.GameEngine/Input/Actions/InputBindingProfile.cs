using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// A named set of bindings from action names to physical inputs ("Fire" to Space and the gamepad's A button,
/// say). An <see cref="InputActionMap"/> reads one profile at a time (<see cref="InputActionMap.Profile"/>), so a
/// game offers several control schemes as several profiles and swaps between them.
/// </summary>
/// <remarks>
/// Action names are case-sensitive. A profile may be edited while a map uses it; the map picks the change up at
/// its next poll. Safe to use from any thread.
/// </remarks>
public sealed class InputBindingProfile
{
    private readonly object _gate = new();
    private readonly List<string> _order = new();
    private readonly Dictionary<string, List<InputBinding>> _bindings = new(StringComparer.Ordinal);
    private int _version;

    /// <summary>Creates an empty profile.</summary>
    /// <param name="name">The profile name, for settings screens and logs.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public InputBindingProfile(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>The profile name.</summary>
    public string Name { get; }

    /// <summary>The actions this profile binds, in the order they were first bound.</summary>
    public IReadOnlyList<string> Actions
    {
        get
        {
            lock (_gate)
            {
                return _order.ToArray();
            }
        }
    }

    /// <summary>Every distinct key code bound by this profile.</summary>
    public IReadOnlyList<int> KeyCodes
    {
        get
        {
            lock (_gate)
            {
                return _order.SelectMany(action => _bindings[action])
                    .Where(binding => binding.Kind == InputBindingKind.Key)
                    .Select(binding => binding.KeyCode)
                    .Distinct()
                    .ToArray();
            }
        }
    }

    /// <summary>Changes every time a binding changes; lets a map notice edits.</summary>
    internal int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>Adds bindings to an action (a binding it has already is not added twice).</summary>
    /// <param name="action">The action name.</param>
    /// <param name="bindings">The inputs that drive it.</param>
    /// <returns>This profile, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="action"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    public InputBindingProfile Bind(string action, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        ArgumentNullException.ThrowIfNull(bindings);

        lock (_gate)
        {
            if (!_bindings.TryGetValue(action, out var list))
            {
                list = new List<InputBinding>();
                _bindings.Add(action, list);
                _order.Add(action);
            }

            foreach (var binding in bindings)
            {
                if (!list.Contains(binding))
                {
                    list.Add(binding);
                }
            }

            _version++;
        }

        return this;
    }

    /// <summary>Replaces every binding of an action.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="bindings">The inputs that drive it from now on.</param>
    /// <returns>This profile, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="action"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    public InputBindingProfile Rebind(string action, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        ArgumentNullException.ThrowIfNull(bindings);

        lock (_gate)
        {
            if (_bindings.TryGetValue(action, out var list))
            {
                list.Clear();
            }
        }

        return Bind(action, bindings);
    }

    /// <summary>Removes an action and all its bindings.</summary>
    /// <param name="action">The action name.</param>
    /// <returns>This profile, for chaining.</returns>
    public InputBindingProfile Unbind(string action)
    {
        lock (_gate)
        {
            if (action is not null && _bindings.Remove(action))
            {
                _order.Remove(action);
                _version++;
            }
        }

        return this;
    }

    /// <summary>Returns the bindings of an action (empty when it has none).</summary>
    /// <param name="action">The action name.</param>
    /// <returns>A copy of the bindings.</returns>
    public IReadOnlyList<InputBinding> GetBindings(string action)
    {
        lock (_gate)
        {
            return action is not null && _bindings.TryGetValue(action, out var list) ? list.ToArray() : Array.Empty<InputBinding>();
        }
    }

    /// <summary>Returns a copy of this profile under a new name - the start of a second control scheme.</summary>
    /// <param name="name">The new profile's name.</param>
    /// <returns>The copy.</returns>
    public InputBindingProfile Copy(string name)
    {
        var copy = new InputBindingProfile(name);
        lock (_gate)
        {
            foreach (var action in _order)
            {
                copy.Bind(action, _bindings[action].ToArray());
            }
        }

        return copy;
    }

    /// <summary>Returns every (action, binding) pair and the version they belong to, in one consistent read.</summary>
    /// <param name="version">The version of the returned bindings.</param>
    /// <returns>The pairs.</returns>
    internal List<(string Action, InputBinding Binding)> Snapshot(out int version)
    {
        lock (_gate)
        {
            version = _version;
            var pairs = new List<(string, InputBinding)>();
            foreach (var action in _order)
            {
                foreach (var binding in _bindings[action])
                {
                    pairs.Add((action, binding));
                }
            }

            return pairs;
        }
    }

    /// <summary>Returns the profile name.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
