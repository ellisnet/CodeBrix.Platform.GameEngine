namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// The camera projection a provider pre-renders a three-dimensional model with.
/// </summary>
public enum ModelProjection
{
    /// <summary>
    /// Parallel projection: distance from the camera does not change the size of anything. This
    /// is what isometric and top-down sprite sheets are drawn with, and it keeps a model the same
    /// size in every direction of a sheet.
    /// </summary>
    Orthographic = 0,

    /// <summary>
    /// Perspective projection: nearer parts of the model are drawn larger, using
    /// <see cref="ModelRenderOptions.FieldOfViewDegrees"/>.
    /// </summary>
    Perspective = 1
}
