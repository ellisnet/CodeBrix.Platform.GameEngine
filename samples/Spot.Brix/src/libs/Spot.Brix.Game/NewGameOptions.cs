using Spot.Brix.Game;
using System.Collections.Generic;

namespace Spot.Brix;

/// <summary>
/// The choices made in the New Game dialog: the board size and the players who will sit at it.
/// </summary>
public class NewGameOptions
{
    /// <summary>Gets or sets the number of columns on the board.</summary>
    public int BoardWidth { get; set; }

    /// <summary>Gets or sets the number of rows on the board.</summary>
    public int BoardHeight { get; set; }

    /// <summary>Gets or sets the players, in seating order. Two to four entries.</summary>
    public List<Player> Players { get; set; } = new();

    /// <summary>Gets the number of players the board will be set up for.</summary>
    public int PlayerCount => Players.Count;
}
