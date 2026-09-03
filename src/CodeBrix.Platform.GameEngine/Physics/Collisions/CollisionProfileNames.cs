namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Provides the names of the collision profiles installed in every scene by default.
/// </summary>
public static class CollisionProfileNames
{
    /// <summary>
    /// The profile applied to fixed layer tiles: the <c>WorldStatic</c> group, colliding with
    /// <c>Actors</c> and <c>Projectiles</c>.
    /// </summary>
    public const string World = "World";

    /// <summary>
    /// The profile applied to newly created sprites: the <c>Actors</c> group, colliding with
    /// <c>WorldStatic</c>, <c>Actors</c>, <c>Projectiles</c> and <c>Triggers</c>.
    /// </summary>
    public const string Actor = "Actor";

    /// <summary>
    /// The profile intended for shots and other short-lived movers: the <c>Projectiles</c> group,
    /// colliding with <c>WorldStatic</c> and <c>Actors</c>.
    /// </summary>
    public const string Projectile = "Projectile";

    /// <summary>
    /// The profile intended for trigger volumes: the <c>Triggers</c> group, colliding with
    /// <c>Actors</c>.
    /// </summary>
    public const string Sensor = "Sensor";
}
