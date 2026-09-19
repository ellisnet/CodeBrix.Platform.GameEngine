using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// One catalogued asset of a pack: the file it lives in, what kind of asset it is, the key it answers
/// to, and whatever the pack index parsed about it while building the catalog.
/// </summary>
/// <remarks>
/// An entry is built once, when the pack is indexed, from the archive's directory listing plus the few
/// small documents that decide what an asset IS - a sprite atlas XML, a tile map, a tile set. Asset
/// bytes are never read at that point: a Kenney collection holds thousands of model files, and opening
/// them to catalog them would make registering a source cost as much as loading the whole pack.
/// </remarks>
internal sealed class KenneyAssetEntry
{
    private readonly KenneyArchiveEntry _file;

    private GameAssetDescriptor? _descriptor;
    private string? _descriptorProviderId;

    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyAssetEntry"/> class.
    /// </summary>
    /// <param name="pack">The pack the asset belongs to.</param>
    /// <param name="file">The archive file the asset lives in.</param>
    /// <param name="kind">The kind of asset the file holds.</param>
    /// <param name="keySegment">The pack-relative part of the asset's key, with the extension already kept or dropped.</param>
    /// <param name="isMaterializable">Whether the provider can turn the asset into an engine object.</param>
    /// <param name="spriteAtlas">The parsed atlas document when the asset is a sprite atlas; otherwise null.</param>
    /// <param name="tiledMap">The parsed map document when the asset is a tile map that parsed; otherwise null.</param>
    /// <param name="tiledMapError">Why the asset's map document could not be parsed, when it could not; otherwise null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pack"/> or <paramref name="file"/> is null.</exception>
    public KenneyAssetEntry(
        KenneyPack pack,
        KenneyArchiveEntry file,
        GameAssetKind kind,
        string keySegment,
        bool isMaterializable,
        SpriteAtlasDocument? spriteAtlas = null,
        TiledMapDocument? tiledMap = null,
        string? tiledMapError = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(keySegment);

        Pack = pack;
        _file = file;
        Kind = kind;
        KeySegment = keySegment;
        IsMaterializable = isMaterializable;
        SpriteAtlas = spriteAtlas;
        TiledMap = tiledMap;
        TiledMapError = tiledMapError;
    }

    /// <summary>
    /// Gets the pack the asset belongs to.
    /// </summary>
    public KenneyPack Pack { get; }

    /// <summary>
    /// Gets the asset's path inside its pack, using forward slashes and no leading slash.
    /// </summary>
    public string Path => _file.Path;

    /// <summary>
    /// Gets the asset's file name without its extension.
    /// </summary>
    public string Name => _file.Name;

    /// <summary>
    /// Gets the asset's lower-case file extension without a leading dot, or an empty string when it has
    /// none.
    /// </summary>
    public string Extension => _file.Extension;

    /// <summary>
    /// Gets the size of the asset's file in bytes.
    /// </summary>
    public long SizeBytes => _file.SizeBytes;

    /// <summary>
    /// Gets the kind of asset the file holds.
    /// </summary>
    public GameAssetKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the provider can turn this asset into an engine object.
    /// </summary>
    public bool IsMaterializable { get; }

    /// <summary>
    /// Gets the pack-relative part of the asset's key: the asset's path, without its extension when the
    /// asset is materializable and with it when it is not.
    /// </summary>
    public string KeySegment { get; }

    /// <summary>
    /// Gets the provider-relative identifier of the asset, which is the pack slug followed by
    /// <see cref="KeySegment"/>. It picks up the pack's final slug, so it is only meaningful once the
    /// pack has been added to an index.
    /// </summary>
    public string KeyPath => $"{Pack.Slug}/{KeySegment}";

    /// <summary>
    /// Gets the parsed atlas document when the asset is a sprite atlas, or <see langword="null"/>
    /// otherwise. Its image path is resolved and exists in the pack.
    /// </summary>
    public SpriteAtlasDocument? SpriteAtlas { get; }

    /// <summary>
    /// Gets the parsed map document when the asset is a tile map that could be parsed, or
    /// <see langword="null"/> otherwise.
    /// </summary>
    public TiledMapDocument? TiledMap { get; }

    /// <summary>
    /// Gets the reason the asset's map document could not be parsed, or <see langword="null"/> when it
    /// parsed or is not a tile map.
    /// </summary>
    public string? TiledMapError { get; }

    /// <summary>
    /// Builds the namespaced key the asset answers to.
    /// </summary>
    /// <param name="providerId">The identifier of the provider that owns the asset.</param>
    /// <returns>The key, of the form <c>&lt;providerId&gt;:&lt;pack-slug&gt;/&lt;path&gt;</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId"/> is null, empty or whitespace.</exception>
    public string GetKey(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return $"{providerId}:{KeyPath}";
    }

    /// <summary>
    /// Opens the asset's bytes for reading.
    /// </summary>
    /// <returns>A readable, seekable stream positioned at the start of the asset. The caller disposes it.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the asset's file is no longer in the archive.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the pack's archive has been disposed.</exception>
    public Stream Open() => Pack.Archive.Open(Path);

    /// <summary>
    /// Reads the whole of the asset's bytes.
    /// </summary>
    /// <returns>The asset's bytes.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the asset's file is no longer in the archive.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the pack's archive has been disposed.</exception>
    public byte[] ReadBytes() => Pack.Archive.ReadBytes(Path);

    /// <summary>
    /// Describes the asset the way the engine's provider contract expects.
    /// </summary>
    /// <param name="providerId">The identifier of the provider that owns the asset.</param>
    /// <returns>The descriptor, carrying the asset's key, kind, pack, path, size and properties.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId"/> is null, empty or whitespace.</exception>
    /// <remarks>
    /// <para>
    /// <see cref="GameAssetDescriptor.Pack"/> carries the pack's SLUG rather than its display name, so
    /// that a query's pack name and the pack part of a key are the same string. The display name is in
    /// <see cref="KenneyAssetProperties.PackName"/>.
    /// </para>
    /// <para>
    /// The descriptor is built once and kept, so every caller asking for the same asset gets the same
    /// instance and can compare descriptors by reference. The pack index primes it while it builds its
    /// lookup, which is before any caller can reach the entry.
    /// </para>
    /// </remarks>
    public GameAssetDescriptor ToDescriptor(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (_descriptor is not null
            && string.Equals(_descriptorProviderId, providerId, StringComparison.Ordinal))
        {
            return _descriptor;
        }

        GameAssetDescriptor descriptor = new()
        {
            ProviderId = providerId,
            Key = GetKey(providerId),
            Kind = Kind,
            Name = Name,
            Pack = Pack.Slug,
            Path = Path,
            SizeBytes = SizeBytes,
            Properties = BuildProperties(),
        };

        _descriptorProviderId = providerId;
        _descriptor = descriptor;
        return descriptor;
    }

    private IReadOnlyDictionary<string, string> BuildProperties()
    {
        Dictionary<string, string> properties = new(StringComparer.OrdinalIgnoreCase)
        {
            [KenneyAssetProperties.PackName] = Pack.DisplayName,
            [KenneyAssetProperties.Materializable] = IsMaterializable ? "true" : "false",
        };

        if (Extension.Length > 0)
        {
            properties[KenneyAssetProperties.Extension] = Extension;
        }

        if (!string.IsNullOrEmpty(Pack.Version))
        {
            properties[KenneyAssetProperties.PackVersion] = Pack.Version;
        }

        if (!string.IsNullOrEmpty(Pack.LicenseTitle))
        {
            properties[KenneyAssetProperties.License] = Pack.LicenseTitle;
        }

        if (SpriteAtlas is not null)
        {
            properties[KenneyAssetProperties.AtlasFrameCount] =
                SpriteAtlas.Frames.Count.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.AtlasImagePath] = SpriteAtlas.ImagePath;
        }

        if (TiledMap is not null)
        {
            properties[KenneyAssetProperties.MapWidth] =
                TiledMap.Width.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.MapHeight] =
                TiledMap.Height.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.TileWidth] =
                TiledMap.TileWidth.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.TileHeight] =
                TiledMap.TileHeight.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.TileLayerCount] =
                TiledMap.TileLayers.Count.ToString(CultureInfo.InvariantCulture);
            properties[KenneyAssetProperties.TilesetCount] =
                TiledMap.Tilesets.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(TiledMapError))
        {
            properties[KenneyAssetProperties.TiledMapError] = TiledMapError;
        }

        return properties;
    }
}
