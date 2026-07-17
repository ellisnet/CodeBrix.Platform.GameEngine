namespace CodeBrix.Platform.GameEngine.Rendering; //CodeBrix (not from Gondwana)

/// <summary>
/// How the pixels of a frame presented through <see cref="PixelFramePresenter"/> are laid
/// out in memory relative to the logical frame the viewer sees.
/// </summary>
public enum FrameOrientation
{
    /// <summary>
    /// Row-major: memory rows are screen rows (index = y * width + x). The common layout.
    /// </summary>
    Identity,

    /// <summary>
    /// Column-major: memory runs down each screen column (index = x * height + y), the
    /// layout of classic column-oriented renderers. The presenter draws it correctly with a
    /// single transformed draw call — no CPU transpose ever happens.
    /// </summary>
    Rotate90,
}
