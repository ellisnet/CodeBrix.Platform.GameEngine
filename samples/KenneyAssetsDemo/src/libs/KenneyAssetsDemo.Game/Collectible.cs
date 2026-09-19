using CodeBrix.Platform.GameEngine.Drawing.Sprites;

namespace KenneyAssetsDemo.Game;

/// <summary>
/// One thing to walk into: a sprite showing one named frame of a Kenney sprite atlas.
/// </summary>
/// <param name="sprite">The sprite drawing the atlas frame.</param>
/// <param name="frameName">The atlas frame's name, which is also the tilesheet region it became.</param>
internal sealed class Collectible(Sprite sprite, string frameName)
{
    /// <summary>Gets the sprite drawing the atlas frame.</summary>
    internal Sprite Sprite { get; } = sprite;

    /// <summary>Gets the atlas frame's name, for the pick-up message.</summary>
    internal string FrameName { get; } = frameName;

    /// <summary>Gets or sets a value indicating whether this one has been picked up.</summary>
    internal bool Collected { get; set; }
}
