using Spot.Brix;
using CodeBrix.Platform.GameEngine.Drawing;

namespace Spot.Brix.Game;

/// <summary>
/// One seat at the board: who plays it, what it is called, and how its spots are drawn.
/// </summary>
public sealed class Player
{
    /// <summary>Gets or sets the name shown on the player's score panel.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Gets or sets whether the seat is played by a person or by the AI.</summary>
    public PlayerType Type { get; set; }

    /// <summary>Gets or sets the player's colour.</summary>
    public ColorItem ColorItem { get; set; } = null!;

    /// <summary>Gets or sets the frame drawn for the player's idle spots.</summary>
    public Frame DefaultFrame { get; set; }

    /// <summary>Gets or sets the frame drawn for the player's selected spot.</summary>
    public Frame ActiveFrame { get; set; }
}
