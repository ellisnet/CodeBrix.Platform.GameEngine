using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// The keys this library puts into <see cref="GameAssetDescriptor.Properties"/>. A caller must not
/// assume any of them is present: each one appears only when the asset has something to say about it.
/// </summary>
public static class KenneyAssetProperties
{
    /// <summary>
    /// The display name of the pack the asset belongs to, which is its licence title when the pack
    /// carries a licence file and its file or folder name otherwise.
    /// </summary>
    public const string PackName = "packName";

    /// <summary>
    /// The pack version as its licence file states it, for example <c>1.2</c>.
    /// </summary>
    public const string PackVersion = "packVersion";

    /// <summary>
    /// The first line of the pack's licence file, which names the pack and its version.
    /// </summary>
    public const string License = "license";

    /// <summary>
    /// The asset's lower-case file extension without a leading dot.
    /// </summary>
    public const string Extension = "extension";

    /// <summary>
    /// <c>true</c> when the provider can turn the asset into an engine object, <c>false</c> when the
    /// asset is listed for discovery only. Always present.
    /// </summary>
    public const string Materializable = "materializable";

    /// <summary>
    /// The number of named frames a sprite atlas declares.
    /// </summary>
    public const string AtlasFrameCount = "atlasFrameCount";

    /// <summary>
    /// The archive path of the sheet image a sprite atlas cuts its frames from.
    /// </summary>
    public const string AtlasImagePath = "atlasImagePath";

    /// <summary>
    /// The width in tiles of a tile map.
    /// </summary>
    public const string MapWidth = "mapWidth";

    /// <summary>
    /// The height in tiles of a tile map.
    /// </summary>
    public const string MapHeight = "mapHeight";

    /// <summary>
    /// The width in pixels of one grid cell of a tile map.
    /// </summary>
    public const string TileWidth = "tileWidth";

    /// <summary>
    /// The height in pixels of one grid cell of a tile map.
    /// </summary>
    public const string TileHeight = "tileHeight";

    /// <summary>
    /// The number of tile layers a tile map holds.
    /// </summary>
    public const string TileLayerCount = "tileLayerCount";

    /// <summary>
    /// The number of tile sets a tile map draws from.
    /// </summary>
    public const string TilesetCount = "tilesetCount";

    /// <summary>
    /// Why a tile map could not be parsed. Present only on a tile map that cannot be imported, and
    /// carrying the same message the parser would have thrown.
    /// </summary>
    public const string TiledMapError = "tiledMapError";
}
