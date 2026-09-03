using Spot.Brix.Game;
using SkiaSharp;
using System.Linq;

namespace Spot.Brix;

/// <summary>
/// Shared default values used by the new-game UI (dialog and overlay).
/// </summary>
public static class GameConfig
{
    /// <summary>The selectable player colors, in display order.</summary>
    public static readonly ColorItem[] AvailableColors =
    [
        new ColorItem("Red",    SKColors.Red,    SKColors.White),
        new ColorItem("Blue",   SKColors.Blue,   SKColors.White),
        new ColorItem("Yellow", SKColors.Yellow, SKColors.Blue),
        new ColorItem("Violet", SKColors.Violet, SKColors.White),
        new ColorItem("Green",  SKColors.Green,  SKColors.Black),
    ];

    /// <summary>The selectable board dimension values (3 to 12 inclusive).</summary>
    public static readonly string[] BoardSizes =
        Enumerable.Range(3, 10).Select(n => n.ToString()).ToArray();

    /// <summary>The smallest selectable board dimension.</summary>
    public const int MinimumBoardSize = 3;

    /// <summary>The largest selectable board dimension.</summary>
    public const int MaximumBoardSize = 12;

    /// <summary>The smallest number of players a game can be started with.</summary>
    public const int MinimumPlayerCount = 2;

    /// <summary>The largest number of players a game can be started with.</summary>
    public const int MaximumPlayerCount = 4;

    /// <summary>Default board width/height index in <see cref="BoardSizes"/> (8×8).</summary>
    public const int DefaultBoardSizeIndex = 5;

    /// <summary>Default player-count index (4 players).</summary>
    public const int DefaultPlayerCountIndex = 2;

    /// <summary>Default player names.</summary>
    public static readonly string[] DefaultPlayerNames = ["Eugene", "Ward", "Robert", "Patrick"];
}
