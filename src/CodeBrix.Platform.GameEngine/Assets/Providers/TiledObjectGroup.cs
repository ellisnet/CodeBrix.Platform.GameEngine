using System.Collections.Generic;
using System.Drawing;

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
    /// Gets the offset in pixels the layer is drawn at, which applies to every object in it. Empty
    /// for a layer the map did not offset.
    /// </summary>
    /// <remarks>
    /// The offset is NOT folded into <see cref="TiledObject.Bounds"/>, which carries the coordinates
    /// the map wrote; a game that places objects adds this to them.
    /// </remarks>
    public PointF Offset { get; init; }

    /// <summary>
    /// Gets a value indicating whether the map marks the layer as visible. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Visible { get; init; } = true;

    /// <summary>
    /// Gets the layer's opacity, from <c>0</c> (fully transparent) to <c>1</c> (fully opaque).
    /// Defaults to <c>1</c>.
    /// </summary>
    public float Opacity { get; init; } = 1.0f;

    /// <summary>
    /// Gets the layer's position among ALL of the map's layers, tile layers and object layers alike,
    /// counted in document order from zero.
    /// </summary>
    /// <remarks>
    /// An import places a tile layer at <see cref="TiledMapImportOptions.ZOrderBase"/> plus that
    /// layer's own document position, so this index is what lets a game draw an object layer's content
    /// between the right two entries of <see cref="TiledMapImport.Layers"/>.
    /// </remarks>
    public int DocumentIndex { get; init; }

    /// <summary>
    /// Gets the custom properties defined on the layer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
