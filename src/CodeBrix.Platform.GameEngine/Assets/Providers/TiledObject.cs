using System.Collections.Generic;
using System.Drawing;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// A single object read from a tile map's object layer. This is data only: the import never turns
/// an object into an engine entity, it hands the values to the game to act on.
/// </summary>
public sealed record TiledObject
{
    /// <summary>
    /// Gets the identifier the map assigned to the object.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the object's name, or an empty string when it is unnamed.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object's type or class, or an empty string when it has none.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object's rectangle in map pixels, as written in the map document.
    /// </summary>
    public RectangleF Bounds { get; init; }

    /// <summary>
    /// Gets the custom properties defined on the object.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
