using System;
using System.Drawing;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;

/// <summary>
/// Aspect-preserving mapping between logical Backbuffer ScreenPx and the pixels of the render
/// surface adapter that presents them.
/// </summary>
/// <remarks>
/// The transform is immutable: an adapter publishes a new one whenever its own size or the logical
/// Backbuffer size changes, so presentation and pointer normalization always agree on the same
/// numbers. A default instance (<see cref="Scale"/> of zero) means "nothing can be presented yet".
/// </remarks>
/// <param name="Scale">The uniform scale applied to logical ScreenPx when presenting.</param>
/// <param name="DestinationRect">
/// The adapter-space rectangle the complete logical image is drawn into, centred on both axes.
/// </param>
public readonly record struct PresentationTransform(float Scale, SKRect DestinationRect)
{
    /// <summary>
    /// Fits the entire logical image inside the adapter, centred on both axes (letterboxing or
    /// pillarboxing the remainder).
    /// </summary>
    /// <param name="bufferWidth">The logical Backbuffer width in pixels.</param>
    /// <param name="bufferHeight">The logical Backbuffer height in pixels.</param>
    /// <param name="adapterWidth">The adapter width in pixels.</param>
    /// <param name="adapterHeight">The adapter height in pixels.</param>
    /// <returns>
    /// The fitted transform, or a default instance when any dimension is not positive (for example
    /// a minimized window, whose adapter reports a zero size).
    /// </returns>
    public static PresentationTransform Fit(int bufferWidth, int bufferHeight, int adapterWidth, int adapterHeight)
    {
        if (bufferWidth <= 0 || bufferHeight <= 0 || adapterWidth <= 0 || adapterHeight <= 0)
            return default;

        float scale = Math.Min((float)adapterWidth / bufferWidth, (float)adapterHeight / bufferHeight);
        float width = bufferWidth * scale;
        float height = bufferHeight * scale;

        return new(scale, SKRect.Create((adapterWidth - width) / 2f, (adapterHeight - height) / 2f, width, height));
    }

    /// <summary>
    /// Maps an adapter-space input position to logical ScreenPx without clamping it into the
    /// presented image.
    /// </summary>
    /// <remarks>
    /// Coordinates outside the presented image are still mapped (they come back negative, or beyond
    /// the logical size) so a captured pointer, a drag, and a leave notification stay routable.
    /// </remarks>
    /// <param name="adapterPx">The position in adapter pixels.</param>
    /// <param name="screenPx">
    /// When this method returns, the position in logical ScreenPx, or <c>(-1, -1)</c> when nothing
    /// can be presented.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the position falls inside the presented game area; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public bool TryAdapterPxToScreenPx(PointF adapterPx, out PointF screenPx)
    {
        screenPx = Scale > 0
            ? new((adapterPx.X - DestinationRect.Left) / Scale, (adapterPx.Y - DestinationRect.Top) / Scale)
            : new(-1, -1);

        return Scale > 0 && DestinationRect.Contains(adapterPx.X, adapterPx.Y);
    }

    /// <summary>
    /// Maps a logical dirty rectangle to the adapter rectangle that must be repainted, rounding its
    /// edges outwards so fractional presentation scales cannot leave seams between adjacent rects.
    /// </summary>
    /// <param name="screenRect">The dirty rectangle in logical ScreenPx.</param>
    /// <returns>
    /// The adapter-space rectangle to repaint, or <see cref="Rectangle.Empty"/> when nothing can be
    /// presented.
    /// </returns>
    public Rectangle ScreenRectToAdapterRect(Rectangle screenRect)
        => Scale <= 0 ? Rectangle.Empty : Rectangle.FromLTRB(
            (int)Math.Floor(DestinationRect.Left + screenRect.Left * Scale),
            (int)Math.Floor(DestinationRect.Top + screenRect.Top * Scale),
            (int)Math.Ceiling(DestinationRect.Left + screenRect.Right * Scale),
            (int)Math.Ceiling(DestinationRect.Top + screenRect.Bottom * Scale));

    internal static int ScaleDimension(int dimension, float renderScale)
    {
        double scaled = Math.Round(dimension * (double)renderScale, MidpointRounding.AwayFromZero);

        if (scaled > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(renderScale), "Scaled dimension exceeds the supported integer range.");

        return Math.Max(1, (int)scaled);
    }
}

/// <summary>
/// Filtering used only when presenting the finished Backbuffer image to the adapter; it is
/// independent of <see cref="Views.Viewport.Zoom"/> and of per-tile filtering.
/// </summary>
public enum RenderScalingFilter
{
    /// <summary>Bilinear presentation filtering.</summary>
    Linear,

    /// <summary>Unfiltered nearest-pixel presentation, for pixel art.</summary>
    NearestNeighbor
}
