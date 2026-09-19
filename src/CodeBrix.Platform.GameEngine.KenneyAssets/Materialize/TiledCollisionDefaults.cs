using System;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// The rule the Tiled importer applies when the caller supplies no
/// <see cref="TiledMapImportOptions.CollisionSelector"/>: a tile collides when it, or the tile set it
/// belongs to, carries a <c>collision</c> custom property.
/// </summary>
/// <remarks>
/// A value of <c>blocking</c> or <c>true</c> means <see cref="TileCollisionType.Blocking"/>,
/// <c>trigger</c> means <see cref="TileCollisionType.Trigger"/>, and anything else means
/// <see cref="TileCollisionType.None"/>. Values are matched without regard to case or surrounding
/// white space. A property on the tile answers for that tile even when its value is not one of the
/// three above, so a tile can opt out of a tile set that opts in.
/// </remarks>
internal static class TiledCollisionDefaults
{
    /// <summary>
    /// The name of the custom property the default rule reads.
    /// </summary>
    public const string CollisionPropertyName = "collision";

    /// <summary>
    /// The property value that asks for <see cref="TileCollisionType.Blocking"/>.
    /// </summary>
    public const string BlockingValue = "blocking";

    /// <summary>
    /// The property value that asks for <see cref="TileCollisionType.Trigger"/>.
    /// </summary>
    public const string TriggerValue = "trigger";

    /// <summary>
    /// The property value that asks for <see cref="TileCollisionType.Blocking"/> in shorthand.
    /// </summary>
    public const string TrueValue = "true";

    /// <summary>
    /// Decides the collision type of one tile from its own and its tile set's custom properties.
    /// </summary>
    /// <param name="info">The tile the importer is about to place.</param>
    /// <returns>The collision type the tile takes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="info"/> is null.</exception>
    public static TileCollisionType Resolve(TiledTileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (info.TileProperties.TryGetValue(CollisionPropertyName, out string? tileValue))
        {
            return Map(tileValue);
        }

        return info.TilesetProperties.TryGetValue(CollisionPropertyName, out string? tilesetValue)
            ? Map(tilesetValue)
            : TileCollisionType.None;
    }

    private static TileCollisionType Map(string? value)
    {
        string text = value?.Trim() ?? string.Empty;

        if (text.Equals(BlockingValue, StringComparison.OrdinalIgnoreCase)
            || text.Equals(TrueValue, StringComparison.OrdinalIgnoreCase))
        {
            return TileCollisionType.Blocking;
        }

        return text.Equals(TriggerValue, StringComparison.OrdinalIgnoreCase)
            ? TileCollisionType.Trigger
            : TileCollisionType.None;
    }
}
