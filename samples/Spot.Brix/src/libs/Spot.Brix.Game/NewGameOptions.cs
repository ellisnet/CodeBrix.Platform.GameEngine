using Spot.Brix.Game;
using System.Collections.Generic;

namespace Spot.Brix;

internal class NewGameOptions
{
    internal int BoardWidth { get; set; }
    internal int BoardHeight { get; set; }
    internal List<Player> Players { get; set; } = new();
}
