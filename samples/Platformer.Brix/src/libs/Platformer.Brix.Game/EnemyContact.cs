using System;
using System.Drawing;

namespace Platformer.Brix.Game;

/// <summary>
/// Decides how the player met an angry mushroom: landing on its head, or running into it. The
/// decision is made from collision rectangles only, so it stays testable and independent of the
/// engine's collision response.
/// </summary>
internal static class EnemyContact
{
    /// <summary>
    /// Determines whether the player stomped the enemy during the frame that moved the two
    /// rectangles from their previous to their current positions.
    /// </summary>
    /// <param name="previousPlayer">The player's collision rectangle before the move.</param>
    /// <param name="player">The player's collision rectangle after the move.</param>
    /// <param name="previousEnemy">The enemy's collision rectangle before the move.</param>
    /// <param name="enemy">The enemy's collision rectangle after the move.</param>
    /// <param name="verticalVelocity">
    /// The player's vertical velocity; positive while falling, which is the only direction a
    /// stomp can come from.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the player's feet crossed the enemy's head during the frame
    /// and the two overlapped horizontally at the moment of crossing; otherwise
    /// <see langword="false"/>.
    /// </returns>
    internal static bool IsStomp(Rectangle previousPlayer, Rectangle player,
        Rectangle previousEnemy, Rectangle enemy, float verticalVelocity)
    {
        // Test the top crossing before overlap, so a fast fall cannot tunnel through the head.
        var oldGap = previousPlayer.Bottom - previousEnemy.Top;
        var newGap = player.Bottom - enemy.Top;

        if (verticalVelocity <= 0f || oldGap > 1 || newGap < 0 || newGap <= oldGap)
            return false;

        var fraction = Math.Clamp(-oldGap / (float)(newGap - oldGap), 0f, 1f);
        var playerLeft = previousPlayer.Left + (player.Left - previousPlayer.Left) * fraction;
        var enemyLeft = previousEnemy.Left + (enemy.Left - previousEnemy.Left) * fraction;

        return playerLeft < enemyLeft + enemy.Width && playerLeft + player.Width > enemyLeft;
    }
}
