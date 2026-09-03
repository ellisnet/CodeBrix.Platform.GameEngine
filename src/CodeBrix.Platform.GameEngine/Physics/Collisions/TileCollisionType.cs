using System.Text.Json.Serialization;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Defines the default collision behavior associated with a tilesheet region, frame, or runtime tile.
/// </summary>
/// <remarks>
/// The enum is written to JSON in its string form (for example <c>"Blocking"</c>) so that
/// tilesheet definition (.gts) files stay interchangeable with the upstream tooling that
/// produced them.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TileCollisionType
{
    /// <summary>
    /// Collision is disabled.
    /// </summary>
    None = 0,

    /// <summary>
    /// Collision is enabled and overlapping solid colliders block movement.
    /// </summary>
    Blocking = 1,

    /// <summary>
    /// Collision is enabled and overlaps are reported without blocking movement.
    /// </summary>
    Trigger = 2
}
