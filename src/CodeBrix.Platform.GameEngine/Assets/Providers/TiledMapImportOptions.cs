using System;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Options that steer how an <see cref="ITiledMapAssetSource"/> imports a tile map into a scene.
/// Every member is optional; the defaults reproduce the provider's own behaviour.
/// </summary>
public sealed record TiledMapImportOptions
{
    /// <summary>
    /// Gets the z-order given to the first imported layer. Later layers are placed above it, one
    /// step at a time, in map order.
    /// </summary>
    public int ZOrderBase { get; init; }

    /// <summary>
    /// Gets the parallax factor applied to imported layers that do not carry their own.
    /// </summary>
    public float Parallax { get; init; } = 1.0f;

    /// <summary>
    /// Gets a callback that decides the collision type of each imported tile, or
    /// <see langword="null"/> to use the provider's property-driven default.
    /// </summary>
    public Func<TiledTileInfo, TileCollisionType>? CollisionSelector { get; init; }

    /// <summary>
    /// Gets the name of the collision profile assigned to imported tiles that collide, or
    /// <see langword="null"/> to leave the layer default in place.
    /// </summary>
    public string? CollisionProfileName { get; init; }

    /// <summary>
    /// Gets a predicate that selects which map layers are imported by name, or
    /// <see langword="null"/> to import every layer.
    /// </summary>
    public Func<string, bool>? LayerFilter { get; init; }

    /// <summary>
    /// Gets a value indicating whether object layers are captured as data in
    /// <see cref="TiledMapImport.ObjectGroups"/>. Defaults to <see langword="true"/>. Objects are
    /// never turned into engine entities by the import itself.
    /// </summary>
    public bool ImportObjectLayers { get; init; } = true;
}
