using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Stores named collision-filtering profiles for a scene. Profiles resolve their group names
/// through the scene's <see cref="CollisionGroupRegistry"/>.
/// </summary>
public sealed class CollisionProfileRegistry
{
    private readonly Dictionary<string, CollisionProfile> _profiles;

    // Serialization is handled by CollisionProfileRegistryJsonConverter (registered in
    // EngineState.SerializerOptions), which round-trips through the internal constructor so
    // the case-insensitive profile lookup and the standard profiles are preserved.
    internal IReadOnlyDictionary<string, CollisionProfile> ProfilesForSerialization => _profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollisionProfileRegistry"/> class carrying the
    /// standard collision profiles: <c>World</c>, <c>Actor</c>, <c>Projectile</c> and <c>Sensor</c>.
    /// </summary>
    public CollisionProfileRegistry()
    {
        _profiles = new Dictionary<string, CollisionProfile>(StringComparer.OrdinalIgnoreCase);

        EnsureStandardProfiles();
    }

    /// <summary>
    /// Constructor used during deserialization to restore the saved profiles. Any standard
    /// profile the save file did not carry is re-installed.
    /// </summary>
    /// <param name="profiles">The deserialized profiles, keyed by name.</param>
    internal CollisionProfileRegistry(Dictionary<string, CollisionProfile>? profiles)
    {
        _profiles = new Dictionary<string, CollisionProfile>(
            profiles ?? new Dictionary<string, CollisionProfile>(),
            StringComparer.OrdinalIgnoreCase);

        EnsureStandardProfiles();
    }

    /// <summary>
    /// Defines a named profile, replacing any profile already registered under that name.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="collisionGroup">The registered collision group name assigned to the profile.</param>
    /// <param name="collidesWith">The registered group names the profile interacts with, or <see langword="null"/> for none.</param>
    /// <param name="collidesWithAll">Whether the profile interacts with every collision group.</param>
    /// <returns>The newly defined profile.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> or <paramref name="collisionGroup"/> is null or whitespace.
    /// </exception>
    public CollisionProfile Define(
        string name,
        string collisionGroup,
        IEnumerable<string>? collidesWith = null,
        bool collidesWithAll = false)
    {
        var profile = new CollisionProfile(
            name,
            collisionGroup,
            collidesWith,
            collidesWithAll);

        _profiles[name] = profile;

        return profile;
    }

    /// <summary>
    /// Gets a previously defined profile.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <returns>The registered profile.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no profile is registered under that name.</exception>
    public CollisionProfile Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collision profile name cannot be empty.", nameof(name));

        if (!_profiles.TryGetValue(name, out var profile))
            throw new KeyNotFoundException($"Collision profile '{name}' is not defined.");

        return profile;
    }

    /// <summary>
    /// Attempts to get a previously defined profile.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="profile">
    /// When this method returns <see langword="true"/>, the registered profile; otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when a profile is registered under that name; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out CollisionProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            profile = null;
            return false;
        }

        return _profiles.TryGetValue(name, out profile);
    }

    /// <summary>
    /// Gets the names of all defined profiles.
    /// </summary>
    /// <returns>A snapshot of the registered profile names.</returns>
    public IReadOnlyCollection<string> GetProfileNames() => _profiles.Keys.ToArray();

    private void EnsureStandardProfiles()
    {
        DefineIfMissing(
            CollisionProfileNames.World,
            "WorldStatic",
            new[] { "Actors", "Projectiles" });

        DefineIfMissing(
            CollisionProfileNames.Actor,
            "Actors",
            new[] { "WorldStatic", "Actors", "Projectiles", "Triggers" });

        DefineIfMissing(
            CollisionProfileNames.Projectile,
            "Projectiles",
            new[] { "WorldStatic", "Actors" });

        DefineIfMissing(
            CollisionProfileNames.Sensor,
            "Triggers",
            new[] { "Actors" });
    }

    private void DefineIfMissing(
        string name,
        string collisionGroup,
        IEnumerable<string> collidesWith)
    {
        if (!_profiles.ContainsKey(name))
            Define(name, collisionGroup, collidesWith);
    }
}
