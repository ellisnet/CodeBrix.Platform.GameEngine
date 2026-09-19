namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Identifies the broad category of an asset offered by an <see cref="IGameAssetProvider"/>.
/// </summary>
/// <remarks>
/// The kind decides which capability interface can materialize the asset: <see cref="Image"/>,
/// <see cref="SpriteAtlas"/> and <see cref="Vector"/> are handled by
/// <see cref="ITilesheetAssetSource"/>, <see cref="Audio"/> by <see cref="IAudioAssetSource"/>,
/// <see cref="Font"/> by <see cref="IFontAssetSource"/>, <see cref="TiledMap"/> by
/// <see cref="ITiledMapAssetSource"/>, and <see cref="Model3D"/> by
/// <see cref="IModelAssetSource"/> as model data or, where the provider offers it, by
/// <see cref="ITilesheetAssetSource"/> as pre-rendered sprites. The remaining values describe
/// content a provider can list but not turn into an engine object; asking for them throws
/// <see cref="UnsupportedGameAssetException"/>.
/// </remarks>
public enum GameAssetKind
{
    /// <summary>
    /// The asset kind could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A single raster image (for example .png or .jpg) with no companion atlas description.
    /// </summary>
    Image = 1,

    /// <summary>
    /// A raster image accompanied by an atlas description that names sub-rectangles within it.
    /// </summary>
    SpriteAtlas = 2,

    /// <summary>
    /// An audio clip (for example .ogg, .wav, .mp3 or .flac).
    /// </summary>
    Audio = 3,

    /// <summary>
    /// A font file (for example .ttf or .otf).
    /// </summary>
    Font = 4,

    /// <summary>
    /// A vector image (for example .svg) that is rasterized on materialization.
    /// </summary>
    Vector = 5,

    /// <summary>
    /// A tile map document that can be imported into a <see cref="Scenes.Scene"/>.
    /// </summary>
    TiledMap = 6,

    /// <summary>
    /// A three-dimensional model or its material. It is materialized as engine-native model data
    /// by <see cref="IModelAssetSource"/> and, where the provider supports it, as pre-rendered
    /// sprite frames by <see cref="ITilesheetAssetSource"/>. The engine itself does not draw 3D:
    /// the model data is for a game's own renderer, and the sprite frames are what the engine's
    /// two-dimensional drawing uses.
    /// </summary>
    Model3D = 7,

    /// <summary>
    /// A readable document such as a licence, readme or preview page.
    /// </summary>
    Document = 8,

    /// <summary>
    /// A nested archive that the provider chose to list rather than expand.
    /// </summary>
    Archive = 9,

    /// <summary>
    /// Recognized content that fits none of the other kinds.
    /// </summary>
    Other = 10
}
