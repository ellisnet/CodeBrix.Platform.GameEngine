using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// An object layer read from a tile map, with the objects it contains. This is data only; see
/// <see cref="TiledObject"/>.
/// </summary>
public sealed record TiledObjectGroup
{
    /// <summary>
    /// Gets the layer's name as written in the map document.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the objects of the layer, in map order.
    /// </summary>
    public IReadOnlyList<TiledObject> Objects { get; init; } = [];

    /// <summary>
    /// Gets the custom properties defined on the layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
