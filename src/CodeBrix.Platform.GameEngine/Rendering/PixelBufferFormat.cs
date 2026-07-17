namespace CodeBrix.Platform.GameEngine.Rendering; //CodeBrix (not from Gondwana)

/// <summary>
/// The in-memory pixel layout of a CPU-rendered frame presented through
/// <see cref="PixelFramePresenter"/>. Four bytes per pixel in both layouts.
/// </summary>
public enum PixelBufferFormat
{
    /// <summary>Bytes in memory order R, G, B, A.</summary>
    Rgba8888,

    /// <summary>Bytes in memory order B, G, R, A.</summary>
    Bgra8888,
}
