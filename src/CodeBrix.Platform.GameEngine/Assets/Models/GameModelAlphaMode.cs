namespace CodeBrix.Platform.GameEngine.Assets.Models; //CodeBrix (not from Gondwana)
/// <summary>
/// How the alpha channel of a <see cref="GameModelMaterial"/> is interpreted.
/// </summary>
public enum GameModelAlphaMode
{
    /// <summary>
    /// Fully opaque; the alpha channel is ignored. This is the default for a material that says
    /// nothing about its alpha.
    /// </summary>
    Opaque = 0,

    /// <summary>
    /// Alpha-tested: a sample is either fully opaque or fully transparent, decided against
    /// <see cref="GameModelMaterial.AlphaCutoff"/>.
    /// </summary>
    Mask = 1,

    /// <summary>
    /// Alpha-blended: the sample is composited over what is behind it, as translucent surfaces
    /// such as glass are.
    /// </summary>
    Blend = 2
}
