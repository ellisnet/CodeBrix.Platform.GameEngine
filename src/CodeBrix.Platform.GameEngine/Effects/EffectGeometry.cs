using System;
using System.Drawing;

namespace CodeBrix.Platform.GameEngine.Effects; //was previously: Gondwana.Effects;

/// <summary>
/// Computes the directional reveal rectangles used by wipe-style display effects.
/// </summary>
internal static class EffectGeometry
{
    /// <summary>
    /// Gets the portion of <paramref name="bounds"/> that a directional reveal exposes at the
    /// supplied progress.
    /// </summary>
    /// <param name="bounds">The full presentation bounds being revealed.</param>
    /// <param name="direction">The direction the reveal travels in.</param>
    /// <param name="progress">Normalized reveal progress; values outside 0 through 1 are clamped.</param>
    /// <returns>
    /// The revealed rectangle: empty at zero progress or for a degenerate <paramref name="bounds"/>,
    /// and the whole of <paramref name="bounds"/> at full progress or when no direction is given.
    /// </returns>
    internal static RectangleF GetRevealRect(
        RectangleF bounds,
        EffectDirection direction,
        float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        if (progress <= 0f || bounds.Width <= 0f || bounds.Height <= 0f)
            return RectangleF.Empty;

        if (progress >= 1f || direction == EffectDirection.None)
            return bounds;

        float width = bounds.Width * progress;
        float height = bounds.Height * progress;

        return direction switch
        {
            EffectDirection.FromLeftToRight =>
                new RectangleF(bounds.Left, bounds.Top, width, bounds.Height),
            EffectDirection.FromRightToLeft =>
                new RectangleF(bounds.Right - width, bounds.Top, width, bounds.Height),
            EffectDirection.FromTopToBottom =>
                new RectangleF(bounds.Left, bounds.Top, bounds.Width, height),
            EffectDirection.FromBottomToTop =>
                new RectangleF(bounds.Left, bounds.Bottom - height, bounds.Width, height),
            EffectDirection.FromTopLeftToBottomRight =>
                new RectangleF(bounds.Left, bounds.Top, width, height),
            EffectDirection.FromTopRightToBottomLeft =>
                new RectangleF(bounds.Right - width, bounds.Top, width, height),
            EffectDirection.FromBottomLeftToTopRight =>
                new RectangleF(bounds.Left, bounds.Bottom - height, width, height),
            EffectDirection.FromBottomRightToTopLeft =>
                new RectangleF(bounds.Right - width, bounds.Bottom - height, width, height),
            _ => bounds
        };
    }
}
