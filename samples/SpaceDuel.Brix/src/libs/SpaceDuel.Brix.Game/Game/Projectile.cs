using CodeBrix.Platform.GameEngine.Drawing.Sprites;

namespace SpaceDuel.Brix.Game;

/// <summary>
/// One laser bolt in flight: its sprite, the ship that fired it, and how long it has existed.
/// </summary>
internal sealed class Projectile
{
    /// <summary>Initializes a new instance of the <see cref="Projectile"/> class.</summary>
    /// <param name="sprite">The bolt's sprite.</param>
    /// <param name="owner">The ship that fired the bolt.</param>
    internal Projectile(Sprite sprite, ShipState owner)
    {
        Sprite = sprite;
        Owner = owner;
    }

    /// <summary>Gets the sprite the engine draws and rotates for this bolt.</summary>
    internal Sprite Sprite { get; }

    /// <summary>Gets the ship that fired this bolt.</summary>
    internal ShipState Owner { get; }

    /// <summary>Gets or sets the seconds this bolt has been in flight.</summary>
    internal float Age { get; set; }
}
