namespace SpaceDuel.Brix.Game;

/// <summary>
/// The stage the duel has reached.
/// </summary>
internal enum GameState
{
    /// <summary>The splash is on screen and the simulation has not begun.</summary>
    Starting,

    /// <summary>The duel is running.</summary>
    Playing,

    /// <summary>Every raider was destroyed.</summary>
    Won,

    /// <summary>The player's ship was destroyed.</summary>
    Lost
}
