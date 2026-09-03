using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Describes a reusable, scene-level collision-filtering role using collision group names
/// rather than scene-specific integer masks.
/// </summary>
public sealed class CollisionProfile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollisionProfile"/> class.
    /// </summary>
    /// <param name="name">The stable profile name.</param>
    /// <param name="collisionGroup">The registered collision group name assigned to the profile.</param>
    /// <param name="collidesWith">The registered group names the profile interacts with, or <see langword="null"/> for none.</param>
    /// <param name="collidesWithAll">Whether the profile interacts with every collision group.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> or <paramref name="collisionGroup"/> is null or whitespace.
    /// </exception>
    public CollisionProfile(
        string name,
        string collisionGroup,
        IEnumerable<string>? collidesWith = null,
        bool collidesWithAll = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collision profile name cannot be empty.", nameof(name));

        if (string.IsNullOrWhiteSpace(collisionGroup))
            throw new ArgumentException("Collision profile group cannot be empty.", nameof(collisionGroup));

        Name = name;
        CollisionGroup = collisionGroup;
        CollidesWith = collidesWith?.ToList() ?? new List<string>();
        CollidesWithAll = collidesWithAll;
    }

    /// <summary>
    /// Gets the stable profile name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets or sets the registered collision group name assigned to this profile.
    /// </summary>
    public string CollisionGroup { get; set; }

    /// <summary>
    /// Gets the registered group names with which this profile interacts.
    /// </summary>
    public List<string> CollidesWith { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this profile interacts with every collision group.
    /// </summary>
    public bool CollidesWithAll { get; set; }

    /// <summary>
    /// Resolves this profile's own group through the supplied scene registry.
    /// </summary>
    /// <param name="groups">The scene's collision group registry.</param>
    /// <returns>The bit mask of this profile's collision group.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groups"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the group name is not defined in <paramref name="groups"/>.</exception>
    public int ResolveCollisionGroup(CollisionGroupRegistry groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        return groups.Get(CollisionGroup);
    }

    /// <summary>
    /// Resolves this profile's interaction mask through the supplied scene registry.
    /// </summary>
    /// <param name="groups">The scene's collision group registry.</param>
    /// <returns>
    /// <see cref="CollisionMasks.All"/> when <see cref="CollidesWithAll"/> is set; otherwise the
    /// combined mask of every name in <see cref="CollidesWith"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groups"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when a group name is not defined in <paramref name="groups"/>.</exception>
    public int ResolveCollidesWith(CollisionGroupRegistry groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        return CollidesWithAll
            ? CollisionMasks.All
            : groups.GetMask(CollidesWith);
    }
}
