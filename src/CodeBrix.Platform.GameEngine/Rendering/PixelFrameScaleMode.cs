namespace CodeBrix.Platform.GameEngine.Rendering; //CodeBrix (not from Gondwana)

/// <summary>
/// How a frame presented through <see cref="PixelFramePresenter"/> is scaled to the output
/// surface.
/// </summary>
public enum PixelFrameScaleMode
{
    /// <summary>
    /// Scale to the largest size that fits while preserving aspect ratio, centered; the
    /// uncovered surface shows as letterbox/pillarbox bars. The default.
    /// </summary>
    Fit,

    /// <summary>Fill the whole surface, ignoring aspect ratio.</summary>
    Stretch,

    /// <summary>
    /// Scale by the largest whole-number factor that fits, centered — crisp pixel-art
    /// scaling. Falls back to <see cref="Fit"/> when the surface is smaller than the frame.
    /// </summary>
    PixelPerfect,

    /// <summary>Draw at 1:1 pixel size, centered, with no scaling.</summary>
    Center,
}
