using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;

namespace SpaceDuel.Brix.Game;

/// <summary>
/// The gameplay state of one ship: the sprite the engine draws, where it respawns, how much hull it
/// has left, and the health bar floating above it.
/// </summary>
internal sealed class ShipState
{
    /// <summary>Initializes a new instance of the <see cref="ShipState"/> class.</summary>
    /// <param name="sprite">The ship's sprite.</param>
    /// <param name="spawnPosition">The grid position the ship returns to on a restart.</param>
    /// <param name="spawnRotation">The heading, in degrees, the ship returns to on a restart.</param>
    /// <param name="isPlayer">Whether this ship is flown by the player.</param>
    /// <param name="maxHealth">The hull strength the ship starts each duel with.</param>
    internal ShipState(
        Sprite sprite,
        Vector2 spawnPosition,
        float spawnRotation,
        bool isPlayer,
        float maxHealth)
    {
        Sprite = sprite;
        SpawnPosition = spawnPosition;
        SpawnRotation = spawnRotation;
        IsPlayer = isPlayer;
        MaxHealth = maxHealth;
        Health = maxHealth;
    }

    /// <summary>Gets the sprite the engine draws and rotates for this ship.</summary>
    internal Sprite Sprite { get; }

    /// <summary>Gets the grid position the ship returns to on a restart.</summary>
    internal Vector2 SpawnPosition { get; }

    /// <summary>Gets the heading, in degrees, the ship returns to on a restart.</summary>
    internal float SpawnRotation { get; }

    /// <summary>Gets a value indicating whether this ship is flown by the player.</summary>
    internal bool IsPlayer { get; }

    /// <summary>Gets or sets the world-space bar drawn above the ship.</summary>
    internal HealthBar HealthBar { get; set; } = null!;

    /// <summary>Gets the hull strength the ship starts each duel with.</summary>
    internal float MaxHealth { get; }

    /// <summary>Gets or sets the hull strength remaining.</summary>
    internal float Health { get; set; }

    /// <summary>Gets or sets the seconds left before this ship may fire again.</summary>
    internal float FireCooldown { get; set; }

    /// <summary>Gets a value indicating whether the ship is still in the duel.</summary>
    internal bool IsAlive => Health > 0f && Sprite.Visible;
}
