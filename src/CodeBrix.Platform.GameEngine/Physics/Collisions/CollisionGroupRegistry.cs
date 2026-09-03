using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Manages collision group definitions and provides bitwise mask values for collision filtering.
/// Groups are represented as bit flags to allow efficient collision detection using bitwise operations.
/// </summary>
public sealed class CollisionGroupRegistry
{
    private readonly Dictionary<string, int> _groups;
    private int _nextBit;

    // Serialization is handled by CollisionGroupRegistryJsonConverter (registered in
    // EngineState.SerializerOptions), which round-trips through the internal constructor so
    // the case-insensitive group comparer is preserved.
    internal IReadOnlyDictionary<string, int> GroupsForSerialization => _groups;
    internal int NextBitForSerialization => _nextBit;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollisionGroupRegistry"/> class with
    /// predefined collision groups: WorldStatic, Actors, Projectiles, and Triggers.
    /// </summary>
    public CollisionGroupRegistry()
    {
        _groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        Define("WorldStatic");
        Define("Actors");
        Define("Projectiles");
        Define("Triggers");
    }

    /// <summary>
    /// Constructor used during deserialization to restore internal state.
    /// </summary>
    /// <param name="groups">The serialized group dictionary.</param>
    /// <param name="nextBit">The next available bit index.</param>
    internal CollisionGroupRegistry(Dictionary<string, int> groups, int nextBit)
    {
        _groups = new Dictionary<string, int>(groups ?? throw new ArgumentNullException(nameof(groups)),
                                              StringComparer.OrdinalIgnoreCase);
        _nextBit = nextBit;
    }

    /// <summary>
    /// Defines a new collision group with the specified name, or returns the existing group value if already defined.
    /// Each group is assigned a unique bit flag value.
    /// </summary>
    public int Define(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name cannot be empty.", nameof(name));

        if (_groups.TryGetValue(name, out var existing))
            return existing;

        if (_nextBit >= 31)
            throw new InvalidOperationException("Max 31 collision groups exceeded for int masks.");

        int value = 1 << _nextBit++;
        _groups.Add(name, value);
        return value;
    }

    /// <summary>
    /// Gets the bit flag value for a previously defined collision group.
    /// </summary>
    public int Get(string name)
    {
        if (!_groups.TryGetValue(name, out var value))
            throw new KeyNotFoundException($"Collision group '{name}' not defined.");

        return value;
    }

    /// <summary>
    /// Combines previously defined collision groups into one bit mask.
    /// </summary>
    /// <param name="names">The names of the groups to combine.</param>
    /// <returns>
    /// The bitwise union of every named group's flag; <see cref="CollisionMasks.None"/> for an
    /// empty collection.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="names"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when a name is not a defined collision group.</exception>
    public int GetMask(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        int mask = CollisionMasks.None;

        foreach (var name in names)
            mask |= Get(name);

        return mask;
    }

    /// <summary>
    /// Gets a read-only collection of all defined collision group names.
    /// </summary>
    public IReadOnlyCollection<string> GetGroupNames() => _groups.Keys.ToArray();

    /// <summary>
    /// Gets the bit flag value for the WorldStatic collision group.
    /// </summary>
    public int WorldStatic => _groups["WorldStatic"];

    /// <summary>
    /// Gets the bit flag value for the Actors collision group.
    /// </summary>
    public int Actors => _groups["Actors"];

    /// <summary>
    /// Gets the bit flag value for the Projectiles collision group.
    /// </summary>
    public int Projectiles => _groups["Projectiles"];

    /// <summary>
    /// Gets the bit flag value for the Triggers collision group.
    /// </summary>
    public int Triggers => _groups["Triggers"];
}
