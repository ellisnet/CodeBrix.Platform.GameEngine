using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Models;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// Serves the assets of Kenney bundles to the engine: it catalogs what the bundles hold and turns an
/// asset into the engine object that asset is for - a tilesheet, an audio resource, a typeface, scene
/// layers imported from a tile map, or three-dimensional model data.
/// </summary>
/// <remarks>
/// <para>
/// REGISTER IT WITH
/// <see cref="EngineKenneyAssetsExtensions.UseKenneyAssets(Engine, string[])"/> rather than building
/// one by hand; that is what puts it in <c>Engine.Instance.Managers.AssetProviders</c>, where
/// <c>LoadTilesheet</c>, <c>LoadAudio</c>, <c>LoadFont</c>, <c>ImportTiledMap</c>, <c>LoadModel</c> and
/// <c>LoadModelAnimation</c> reach it by key.
/// </para>
/// <para>
/// KEYS. Every asset is addressed by
/// <c>&lt;providerId&gt;:&lt;pack-slug&gt;/&lt;path-inside-the-pack&gt;</c>, compared
/// case-insensitively, with the extension dropped from an asset the provider can materialize -
/// <c>kenney:puzzle-pack/PNG/Double/ballBlue</c>. A file listed for discovery only keeps its
/// extension, because a model ships as several files that would otherwise share one key. The key is
/// also the key the materialized object is registered under in the engine's own registry, so game code
/// addresses a Kenney asset exactly the way it addresses its own.
/// </para>
/// <para>
/// FIRST MATERIALIZATION OF A KEY WINS. The engine's registries hold one object per key and disposing
/// what is already there would take away frames, voices and typefaces a game is using, so asking twice
/// for one key hands back the object from the first call and the second call's options are ignored. A
/// second variant of one asset is asked for under its own key with
/// <see cref="TilesheetMaterializeOptions.RegisterAs"/>.
/// </para>
/// <para>
/// WHAT IS READ WHEN. Registering a source reads the archive's file listing plus the few small
/// documents that decide what an asset IS - sprite atlas, tile set and tile map XML. No image, audio,
/// font or model file is opened until it is materialized, so pointing the provider at a folder of
/// thousands of models costs a directory walk.
/// </para>
/// <para>
/// THREAD SAFETY. Safe to use from several threads, as
/// <see cref="IGameAssetProvider"/> requires: the catalog is swapped rather than edited in place, and
/// each materializing route serializes its own check-then-load so two threads asking for one key get
/// one object.
/// </para>
/// </remarks>
public sealed class KenneyGameAssetProvider : IGameAssetProvider, ITilesheetAssetSource, IAudioAssetSource,
    IFontAssetSource, ITiledMapAssetSource, IModelAssetSource
{
    /// <summary>
    /// The provider identifier asset keys carry unless a game asks for another one.
    /// </summary>
    public const string DefaultProviderId = KenneyPackIndex.DefaultProviderId;

    //Frozen rather than a HashSet behind the interface: the set is shared by every provider instance
    //  and is handed to callers, so it must not be mutable through a cast
    private static readonly FrozenSet<GameAssetKind> MaterializableKinds = FrozenSet.ToFrozenSet(
    [
        GameAssetKind.Image,
        GameAssetKind.SpriteAtlas,
        GameAssetKind.Audio,
        GameAssetKind.Font,
        GameAssetKind.Vector,
        GameAssetKind.TiledMap,
        GameAssetKind.Model3D,
    ]);

    //Linux file systems tell case apart; the Windows and macOS defaults do not
    private static readonly StringComparison SourcePathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private readonly object _gate = new();
    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _sourceWarnings = [];
    private readonly KenneyPackIndex _index;

    private readonly TilesheetMaterializer _tilesheets = new();
    private readonly AudioMaterializer _audio = new();
    private readonly FontMaterializer _fonts = new();
    private readonly TiledMapImporter _maps = new();
    private readonly GltfModelReader _modelReader = new();
    private readonly ModelTilesheetMaterializer _modelSheets;

    private KenneyPackSummary[] _packs = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyGameAssetProvider"/> class and registers the
    /// asset sources the options name.
    /// </summary>
    /// <param name="options">
    /// The sources to register, the identifier to namespace their keys with, and how to treat a source
    /// that cannot be read; or <see langword="null"/> for an empty provider with the default
    /// identifier, which sources can be added to later.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="KenneyAssetsOptions.ProviderId"/> is null, empty, whitespace or contains
    /// a colon; and, when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off, when
    /// a source path is blank.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and no file
    /// or folder exists at a source path.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and a source
    /// zip file cannot be read.
    /// </exception>
    /// <remarks>
    /// A game normally calls
    /// <see cref="EngineKenneyAssetsExtensions.UseKenneyAssets(Engine, KenneyAssetsOptions)"/> instead,
    /// which builds the provider and registers it with the engine in one step.
    /// </remarks>
    public KenneyGameAssetProvider(KenneyAssetsOptions? options = null)
    {
        KenneyAssetsOptions settings = options ?? new KenneyAssetsOptions();

        if (string.IsNullOrWhiteSpace(settings.ProviderId)
            || settings.ProviderId.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An asset provider identifier must not be empty and must not contain a colon, because " +
                $"a colon separates it from the rest of a key: '{settings.ProviderId}'.",
                nameof(options));
        }

        _index = new KenneyPackIndex(settings.ProviderId);
        ProviderId = _index.ProviderId;
        _modelSheets = new ModelTilesheetMaterializer(_modelReader);

        try
        {
            AddSourcesCore(
                settings.Sources, settings.RecursiveFolders, settings.IgnoreUnreadableSources);
        }
        catch
        {
            //A source that opened before the bad one still holds an archive open
            ReleaseEverything();
            throw;
        }
    }

    /// <inheritdoc/>
    public string ProviderId { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Images, sprite atlases, audio, fonts, vectors, tile maps and three-dimensional models. A model
    /// is offered both as data and, through <see cref="MaterializeTilesheet"/>, as pre-rendered sprite
    /// frames. Documents, nested archives and the file types listed in the remarks of
    /// <see cref="GameAssetKind.Other"/> are catalogued but never materialized.
    /// </remarks>
    public IReadOnlySet<GameAssetKind> SupportedKinds => MaterializableKinds;

    /// <summary>
    /// Gets a summary of each registered pack, in the order the packs were registered.
    /// </summary>
    public IReadOnlyList<KenneyPackSummary> Packs
    {
        get
        {
            lock (_gate) { return _packs; }
        }
    }

    /// <summary>
    /// Gets the number of assets the provider can address by key, across every registered pack.
    /// </summary>
    public int AssetCount => _index.Count;

    /// <summary>
    /// Gets everything that could not be done exactly, oldest first: a source that would not open, a
    /// sprite atlas whose sheet image is missing, a tile map that does not parse, two files wanting one
    /// key, a pack slug that had to be made unique, and whatever a materialized asset had to give up -
    /// two atlas frames whose names collide, a frame outside its sheet, an unusable tile size.
    /// </summary>
    /// <remarks>
    /// Each read is a snapshot, and the list only grows: warnings from registering sources are there
    /// from the start, and materializing adds to them. A tile map import's own warnings are NOT here -
    /// they belong to that one import and are returned in <see cref="TiledMapImport.Warnings"/>.
    /// </remarks>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            List<string> warnings;

            lock (_gate) { warnings = [.. _sourceWarnings]; }

            warnings.AddRange(_index.Warnings);
            warnings.AddRange(_tilesheets.Warnings);

            return warnings;
        }
    }

    /// <summary>
    /// Registers more asset sources with this provider, using the folder and error-handling settings
    /// the options carry.
    /// </summary>
    /// <param name="options">
    /// The sources to add and how to read them. Its <see cref="KenneyAssetsOptions.ProviderId"/> must
    /// name this provider, because a provider's identifier is fixed when it is created.
    /// </param>
    /// <returns>
    /// What this call could not do exactly: a source that would not open and the catalog warnings of
    /// the packs it added. Empty when every source opened and catalogued cleanly.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the options name a different provider; and, when
    /// <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off, when a source path is
    /// blank.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and no file
    /// or folder exists at a source path.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and a source
    /// zip file cannot be read.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// Adding is safe while the game is reading: the catalog is rebuilt and swapped in, so a lookup in
    /// flight sees either the old catalog or the new one. Assets already materialized keep working, and
    /// a pack whose slug is already taken is given a <c>-2</c>, <c>-3</c> suffix, which appears in its
    /// keys.
    /// </remarks>
    public IReadOnlyList<string> AddSources(KenneyAssetsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        ThrowIfOtherProvider(options);

        return AddSourcesCore(options.Sources, options.RecursiveFolders, options.IgnoreUnreadableSources);
    }

    /// <summary>
    /// Registers more asset sources with this provider, with the default settings.
    /// </summary>
    /// <param name="zipFilesOrFolders">
    /// The paths of the Kenney bundles (.zip files) and extracted bundle folders to add.
    /// </param>
    /// <returns>
    /// What this call could not do exactly: a source that would not open and the catalog warnings of
    /// the packs it added. Empty when every source opened and catalogued cleanly.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipFilesOrFolders"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// This is the same as passing a <see cref="KenneyAssetsOptions"/> holding these paths and nothing
    /// else, so an unreadable source becomes a warning and a source folder is taken as one pack. Pass
    /// the options overload to change either.
    /// </remarks>
    public IReadOnlyList<string> AddSources(params string[] zipFilesOrFolders)
    {
        ArgumentNullException.ThrowIfNull(zipFilesOrFolders);

        return AddSources(new KenneyAssetsOptions { ProviderId = ProviderId, Sources = zipFilesOrFolders });
    }

    /// <summary>
    /// Registers more asset sources with this provider, leaving out any path it already holds, and
    /// reports what happened to each path.
    /// </summary>
    /// <param name="options">
    /// The sources to add and how to read them. Its <see cref="KenneyAssetsOptions.ProviderId"/> must
    /// name this provider, because a provider's identifier is fixed when it is created.
    /// </param>
    /// <returns>
    /// One <see cref="KenneySourceResult"/> per path, in the order given - read, already registered,
    /// missing or unreadable, with the packs it stands for - plus the warnings this call added.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the options name a different provider; and, when
    /// <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off, when a source path is
    /// blank.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and no file
    /// or folder exists at a source path.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when <see cref="KenneyAssetsOptions.IgnoreUnreadableSources"/> is turned off and a source
    /// zip file cannot be read.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// This is <see cref="AddSources(KenneyAssetsOptions)"/> made safe to call again: a zip file or
    /// folder is recognized by its full path, and a path this provider already read - earlier, or
    /// earlier in the same call - is reported as <see cref="KenneySourceStatus.AlreadyRegistered"/>
    /// instead of being added a second time under a <c>-2</c> slug with a second set of keys. For a
    /// path read once the outcome is exactly what <c>AddSources</c> gives.
    /// </para>
    /// <para>
    /// The same bundle copied to another path is a different source and IS read again.
    /// </para>
    /// </remarks>
    public KenneyAssetsRegistration AddNewSources(KenneyAssetsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        ThrowIfOtherProvider(options);

        IReadOnlyList<string> warnings = AddSourcesCore(
            options.Sources,
            options.RecursiveFolders,
            options.IgnoreUnreadableSources,
            skipRegistered: true,
            out IReadOnlyList<KenneySourceResult> results);

        return new KenneyAssetsRegistration(this, results, warnings);
    }

    /// <summary>
    /// Registers more asset sources with this provider, with the default settings, leaving out any
    /// path it already holds, and reports what happened to each path.
    /// </summary>
    /// <param name="zipFilesOrFolders">
    /// The paths of the Kenney bundles (.zip files) and extracted bundle folders to add.
    /// </param>
    /// <returns>One result per path, in the order given, plus the warnings this call added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipFilesOrFolders"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// The same as <see cref="AddNewSources(KenneyAssetsOptions)"/> with options holding these paths
    /// and nothing else, so an unreadable source is reported rather than thrown.
    /// </remarks>
    public KenneyAssetsRegistration AddNewSources(params string[] zipFilesOrFolders)
    {
        ArgumentNullException.ThrowIfNull(zipFilesOrFolders);

        return AddNewSources(new KenneyAssetsOptions { ProviderId = ProviderId, Sources = zipFilesOrFolders });
    }

    /// <summary>
    /// Gets one credit line per registered pack, in registration order, with a line two packs would
    /// share listed once.
    /// </summary>
    /// <remarks>
    /// Each line is <see cref="KenneyPackSummary.CreditLine"/>, so a credits screen built from this
    /// credits exactly what the game loaded.
    /// </remarks>
    public IReadOnlyList<string> CreditLines =>
        Packs.Select(pack => pack.CreditLine).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Checks a list of asset keys against the catalog without materializing anything: which keys
    /// resolve, which do not, and the kind and size of each one that does.
    /// </summary>
    /// <param name="keys">
    /// The keys to check, in either form <see cref="TryDescribe"/> accepts. A null or blank entry is
    /// reported as not found.
    /// </param>
    /// <returns>One status per key, in the order given, with the missing keys and a count per kind.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keys"/> is null.</exception>
    /// <remarks>
    /// This is what a game's test calls with every key constant it hand-writes, asserting
    /// <see cref="KenneyKeyCheck.MissingKeys"/> is empty, and what a start-up log summarizes with
    /// <see cref="KenneyKeyCheck.ToString"/>. A key carrying another provider's prefix is not found
    /// here, since this provider does not hold it.
    /// </remarks>
    public KenneyKeyCheck CheckKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        List<KenneyKeyStatus> statuses = [];
        foreach (string key in keys)
        {
            if (_index.TryGetEntry(key, out KenneyAssetEntry? entry))
            {
                statuses.Add(new KenneyKeyStatus(
                    key, true, entry.Kind, entry.SizeBytes, entry.SpriteAtlas?.Frames.Count ?? 0));
            }
            else
            {
                statuses.Add(new KenneyKeyStatus(key ?? string.Empty, false, GameAssetKind.Unknown, 0, 0));
            }
        }

        return new KenneyKeyCheck(statuses);
    }

    /// <summary>
    /// Writes a readable catalog of the keys: every key grouped by pack, with its kind, and under
    /// each sprite atlas the names of the frames inside it.
    /// </summary>
    /// <param name="writer">Where to write the catalog.</param>
    /// <param name="query">A filter on the keys listed, or <see langword="null"/> to list every key.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="writer"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This is for a developer choosing assets and copying exact spellings: keys are spelled as the
    /// registry wants them, and the frame names are the region names the materialized atlas gets
    /// with the default options (image extension removed), so <c>sheet["frameName", 0, 0]</c>
    /// works as written. Nothing is materialized; only the catalog and the atlas documents already
    /// read at registration are used.
    /// </para>
    /// <para>
    /// The format is plain text meant for reading, not parsing: a heading line, then per pack a
    /// blank line, a <c>slug - title - N key(s)</c> line, and one indented <c>key  [Kind]</c> line
    /// per key, with an atlas's frame names indented further below it. Packs with no listed key are
    /// left out.
    /// </para>
    /// </remarks>
    public void WriteKeyCatalog(TextWriter writer, GameAssetQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        KenneyKeyCatalogWriter.Write(writer, ProviderId, Packs, _index.Describe(query), _index, query is not null);
    }

    /// <summary>
    /// Returns the readable key catalog <see cref="WriteKeyCatalog"/> writes, as one string.
    /// </summary>
    /// <param name="query">A filter on the keys listed, or <see langword="null"/> to list every key.</param>
    /// <returns>The catalog text.</returns>
    public string GetKeyCatalog(GameAssetQuery? query = null)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        WriteKeyCatalog(writer, query);

        return writer.ToString();
    }

    /// <inheritdoc/>
    public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null) =>
        _index.Describe(query);

    /// <inheritdoc/>
    public bool TryDescribe(string key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor) =>
        _index.TryGetDescriptor(key, out descriptor);

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the asset's file is no longer in its pack.</exception>
    /// <remarks>
    /// This is the escape hatch, so it opens ANY asset the provider lists, including the documents,
    /// nested archives and model formats it cannot materialize.
    /// </remarks>
    public Stream OpenRaw(GameAssetDescriptor descriptor) => ResolveEntry(descriptor).Open();

    /// <summary>
    /// Materializes an image, sprite atlas, vector or three-dimensional model asset as a tilesheet and
    /// registers it in the engine's <see cref="TilesheetRegistry"/>.
    /// </summary>
    /// <param name="descriptor">The asset to materialize, as this provider described it.</param>
    /// <param name="options">
    /// The grid, collision, rasterization and model-rendering options, or <see langword="null"/> for
    /// the defaults.
    /// </param>
    /// <returns>
    /// The registered tilesheet. An image or a rasterized vector is one whole-image tile, so
    /// <c>sheet[0, 0]</c> is the picture; a sprite atlas is one region per frame, addressed
    /// <c>sheet["ballBlue", 0, 0]</c> with the frame name's image extension stripped; a model is one
    /// uniform-grid region per rendered animation, addressed <c>sheet["walk", frame, direction]</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a vector's raster size or scale is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a model sheet would be larger than the renderer's limit, or an animation the options
    /// named is not one the model offers; the message says what to reduce.
    /// </exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset cannot become a tilesheet.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a sheet image, buffer or texture the asset names is not in its pack.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes cannot be decoded.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// The sheet is registered under the descriptor's own key, or under
    /// <see cref="TilesheetMaterializeOptions.RegisterAs"/> when that is given. A key the registry
    /// already holds is handed back as it is and this call's options are ignored, because registering
    /// over a key disposes the sheet and every frame taken from it.
    /// </remarks>
    public Tilesheet MaterializeTilesheet(
        GameAssetDescriptor descriptor, TilesheetMaterializeOptions? options = null)
    {
        KenneyAssetEntry entry = ResolveMaterializable(descriptor);

        //A model becomes sprite frames, which is a different job from cutting up a picture. The model
        //  route takes the registry key already resolved; the picture route resolves it itself.
        if (entry.Kind != GameAssetKind.Model3D)
        {
            return _tilesheets.MaterializeTilesheet(entry, descriptor.Key, options);
        }

        string registryKey = string.IsNullOrWhiteSpace(options?.RegisterAs)
            ? descriptor.Key
            : options.RegisterAs;

        return _modelSheets.Materialize(entry, registryKey, options);
    }

    /// <summary>
    /// Materializes an audio asset as an <see cref="AudioResource"/> and registers it in the engine's
    /// <see cref="AudioResourceManager"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize, as this provider described it.</param>
    /// <param name="volume">The resource's initial volume, from 0 (silent) to 1 (unattenuated).</param>
    /// <param name="pan">The resource's initial stereo pan, from -1 (left) to 1 (right).</param>
    /// <returns>The registered audio resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not audio the provider can read.</exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the engine has no reader registered for the asset's audio format; the message names
    /// the formats it does have.
    /// </exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes cannot be decoded.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// The first materialization of a key wins: when the audio manager already holds the key, that
    /// resource is returned and this call's <paramref name="volume"/> and <paramref name="pan"/> are
    /// IGNORED, because re-registering would dispose the resource the game is playing from. Set them on
    /// the resource itself, or unload the key first.
    /// </remarks>
    public AudioResource MaterializeAudio(
        GameAssetDescriptor descriptor, float volume = 1.0f, float pan = 0.0f) =>
        _audio.Materialize(ResolveMaterializable(descriptor), descriptor.Key, volume, pan);

    /// <summary>
    /// Materializes a font asset as an <see cref="SKTypeface"/> and registers it in the engine's
    /// <see cref="Rendering.Text.FontManager"/> under the descriptor's key.
    /// </summary>
    /// <param name="descriptor">The asset to materialize, as this provider described it.</param>
    /// <returns>The registered typeface, which is the instance to hand to a text element's font.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a font.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes are not a readable font.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// The first materialization of a key wins: a key the font manager already holds is handed back as
    /// it is. Kenney's per-device input-prompt fonts are ICON fonts with no letters in them, so a font
    /// that draws no text is not necessarily a failure.
    /// </remarks>
    public SKTypeface MaterializeFont(GameAssetDescriptor descriptor) =>
        _fonts.Materialize(ResolveMaterializable(descriptor), descriptor.Key);

    /// <summary>
    /// Imports a tile map asset into a scene, adding one <see cref="SceneLayer"/> per tile layer of the
    /// map and registering one tilesheet per tile set it references.
    /// </summary>
    /// <param name="descriptor">The map asset to import, as this provider described it.</param>
    /// <param name="scene">The scene that receives the imported layers.</param>
    /// <param name="options">
    /// The z-order, parallax, collision and layer-filtering options, or <see langword="null"/> for the
    /// defaults.
    /// </param>
    /// <returns>The layers, tilesheets, object data and warnings the import produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> or <paramref name="scene"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not a tile map, or the map or a tile set of it uses something the engine
    /// cannot represent: a non-orthogonal or infinite map, a tile set holding one image per tile, or a
    /// tile set whose tiles are smaller than the map's grid cell.
    /// </exception>
    /// <exception cref="TiledMapParseException">
    /// Thrown when the map document cannot be read, or a tile set's declared grid does not fit its
    /// image.
    /// </exception>
    /// <exception cref="FileNotFoundException">Thrown when a tile set document or image is not in the pack.</exception>
    /// <exception cref="InvalidDataException">Thrown when a tile set image cannot be decoded.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// Each tile set becomes a tilesheet keyed <c>&lt;map key&gt;#&lt;tile set name&gt;</c>, with the
    /// tile grid in a region called <c>tiles</c> and one further region per flip combination the map
    /// uses. A sheet already registered under its key is reused, but layers are always ADDED to the
    /// scene passed in, so importing one map into one scene twice gives that scene two sets of layers.
    /// </remarks>
    public TiledMapImport MaterializeTiledMap(
        GameAssetDescriptor descriptor, Scene scene, TiledMapImportOptions? options = null) =>
        _maps.Import(ResolveMaterializable(descriptor), descriptor.Key, scene, options);

    /// <summary>
    /// Materializes a three-dimensional model asset as engine-native <see cref="GameModel"/> data.
    /// </summary>
    /// <param name="descriptor">The asset to materialize, as this provider described it.</param>
    /// <param name="options">
    /// Which animations to bake and at what rate, or <see langword="null"/> to bake none.
    /// </param>
    /// <returns>
    /// The model. <see cref="GameModel.AnimationNames"/> always lists every animation the asset offers,
    /// whether or not it was baked.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an animation the options named is not one the asset offers.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the options ask for fewer than one frame per second.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not a glTF model; the <c>.fbx</c>, <c>.obj</c>, <c>.mtl</c>,
    /// <c>.dae</c> and <c>.stl</c> copies Kenney ships beside it are catalogued for discovery only.
    /// </exception>
    /// <exception cref="FileNotFoundException">Thrown when a buffer or texture the model names is not in its pack.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset is not a loadable glTF document, or carries no triangle geometry.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// Model data is registered nowhere: it is returned to the caller, which owns it. The engine draws
    /// no 3D, so a game either renders the data itself or asks for the same asset through
    /// <see cref="MaterializeTilesheet"/> and gets sprite frames its 2D drawing can use. Asking for one
    /// asset twice re-uses the parsed document and costs only the clips that were asked for.
    /// </remarks>
    public GameModel MaterializeModel(
        GameAssetDescriptor descriptor, ModelMaterializeOptions? options = null) =>
        _modelReader.Read(ResolveMaterializable(descriptor), options);

    /// <summary>
    /// Bakes one animation of a three-dimensional model asset into vertex frames, on demand.
    /// </summary>
    /// <param name="descriptor">The asset to bake from, as this provider described it.</param>
    /// <param name="animationName">
    /// The animation's name, matched case-insensitively against <see cref="GameModel.AnimationNames"/>.
    /// </param>
    /// <param name="framesPerSecond">The rate to sample the animation at.</param>
    /// <returns>
    /// The baked clip, aligned with the model <see cref="MaterializeModel"/> returns for the same
    /// asset.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="animationName"/> is null, empty, whitespace, or names an animation
    /// the asset does not offer; the message lists the ones it does.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="framesPerSecond"/> is less than one.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a glTF model.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a buffer or texture the model names is not in its pack.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset is not a loadable glTF document.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the provider has been disposed.</exception>
    /// <remarks>
    /// This is the cheap way to add an animation to a model that has already been read: the parsed
    /// document of an animated model is kept, so nothing is read again. Nothing is registered - the
    /// clip belongs to the caller.
    /// </remarks>
    public GameModelAnimationClip MaterializeModelAnimation(
        GameAssetDescriptor descriptor, string animationName, int framesPerSecond = 24) =>
        _modelReader.BakeClip(ResolveMaterializable(descriptor), animationName, framesPerSecond);

    /// <summary>
    /// Closes every archive the provider opened and releases the parsed model documents it kept.
    /// Disposing twice is harmless.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine calls this when the provider is replaced, unregistered, or when the engine shuts
    /// down. Afterwards the catalog can still be listed - <see cref="Describe"/>,
    /// <see cref="TryDescribe"/>, <see cref="Packs"/> and <see cref="Warnings"/> are pure data - but
    /// nothing can be read or materialized any more.
    /// </para>
    /// <para>
    /// Objects already handed to the engine's registries are NOT withdrawn: the tilesheets, audio
    /// resources and typefaces a game is drawing and playing with stay registered and keep working,
    /// because they are the engine's now. A game that wants them gone unloads them from the registry
    /// that holds them. Models and animation clips are plain data and stay valid too.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
            ReleaseEverything();
        }
    }

    private IReadOnlyList<string> AddSourcesCore(
        IReadOnlyList<string>? paths, bool recursiveFolders, bool ignoreUnreadableSources) =>
        AddSourcesCore(paths, recursiveFolders, ignoreUnreadableSources, skipRegistered: false, out _);

    //The one registration routine. skipRegistered = false is the historical AddSources behavior: every
    //  path is opened, and a pack whose slug is taken gets a numeric suffix. skipRegistered = true
    //  leaves out a path the provider already holds a source for, and reports every path.
    private IReadOnlyList<string> AddSourcesCore(
        IReadOnlyList<string>? paths,
        bool recursiveFolders,
        bool ignoreUnreadableSources,
        bool skipRegistered,
        out IReadOnlyList<KenneySourceResult> results)
    {
        if (paths is null || paths.Count == 0)
        {
            results = [];
            return [];
        }

        List<string> added = [];
        List<(string Path, KenneySourceStatus Status, KenneyAssetSource? Source, string? Message)> outcomes = [];

        lock (_gate)
        {
            int catalogWarningsBefore = _index.Warnings.Count;

            foreach (string path in paths)
            {
                if (skipRegistered && FindRegisteredSource(path) is { } registered)
                {
                    outcomes.Add((path, KenneySourceStatus.AlreadyRegistered, registered, null));
                    continue;
                }

                KenneyAssetSource source;

                if (ignoreUnreadableSources)
                {
                    if (!KenneyAssetSource.TryOpen(
                        path, out KenneyAssetSource? opened, out string? warning, recursiveFolders))
                    {
                        _sourceWarnings.Add(warning!);
                        added.Add(warning!);
                        outcomes.Add((path, SourceExists(path)
                            ? KenneySourceStatus.Unreadable
                            : KenneySourceStatus.Missing, null, warning));
                        continue;
                    }

                    source = opened!;
                }
                else
                {
                    source = KenneyAssetSource.Open(path, recursiveFolders);
                }

                _sources.Add(source);
                _index.AddSource(source);
                outcomes.Add((path, KenneySourceStatus.Read, source, null));
            }

            added.AddRange(_index.Warnings.Skip(catalogWarningsBefore));
            _packs = BuildPackSummaries();

            Dictionary<KenneyPack, KenneyPackSummary> summaries = new(ReferenceEqualityComparer.Instance);
            IReadOnlyList<KenneyPack> indexedPacks = _index.Packs;
            for (int index = 0; index < indexedPacks.Count; index++) { summaries[indexedPacks[index]] = _packs[index]; }

            results = outcomes
                .Select(outcome => new KenneySourceResult
                {
                    SourcePath = outcome.Path,
                    Status = outcome.Status,
                    Packs = outcome.Source is null
                        ? []
                        : outcome.Source.Packs
                            .Where(summaries.ContainsKey)
                            .Select(pack => summaries[pack])
                            .ToList(),
                    Message = outcome.Message,
                })
                .ToList();
        }

        return added;
    }

    //The source already registered from this path, compared as full paths
    private KenneyAssetSource? FindRegisteredSource(string? path)
    {
        string? wanted = NormalizeSourcePath(path);
        if (wanted is null) { return null; }

        foreach (KenneyAssetSource source in _sources)
        {
            if (string.Equals(NormalizeSourcePath(source.SourcePath), wanted, SourcePathComparison))
            {
                return source;
            }
        }

        return null;
    }

    private static string? NormalizeSourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { return null; }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool SourceExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    //One summary per pack, counting only the assets that ended up addressable, so the numbers agree
    //  with what Describe lists
    private KenneyPackSummary[] BuildPackSummaries()
    {
        IReadOnlyList<KenneyPack> packs = _index.Packs;
        KenneyPackSummary[] summaries = new KenneyPackSummary[packs.Count];

        for (int index = 0; index < packs.Count; index++)
        {
            KenneyPack pack = packs[index];
            Dictionary<GameAssetKind, int> countsByKind = [];
            int addressable = 0;
            int materializable = 0;

            foreach (KenneyAssetEntry entry in pack.Entries)
            {
                if (!_index.TryGetEntry(entry.GetKey(ProviderId), out KenneyAssetEntry? indexed)
                    || !ReferenceEquals(indexed, entry))
                {
                    //Another file of this pack took the key; the index has already said so
                    continue;
                }

                addressable++;
                countsByKind[entry.Kind] = countsByKind.GetValueOrDefault(entry.Kind) + 1;

                if (entry.IsMaterializable) { materializable++; }
            }

            summaries[index] = new KenneyPackSummary
            {
                Slug = pack.Slug,
                DisplayName = pack.DisplayName,
                Version = pack.Version,
                LicenseTitle = pack.LicenseTitle,
                SourcePath = pack.SourcePath,
                AssetCount = addressable,
                MaterializableAssetCount = materializable,
                CountsByKind = countsByKind,
            };
        }

        return summaries;
    }

    //The asset a descriptor names, whatever the provider can do with it
    private KenneyAssetEntry ResolveEntry(GameAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (!string.Equals(descriptor.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException(
                $"The asset '{descriptor.Key}' belongs to the provider '{descriptor.ProviderId}', not " +
                $"to '{ProviderId}'.");
        }

        if (!_index.TryGetEntry(descriptor.Key, out KenneyAssetEntry? entry))
        {
            throw new KeyNotFoundException(
                $"The asset provider '{ProviderId}' does not hold an asset with the key " +
                $"'{descriptor.Key}'.");
        }

        return entry;
    }

    //The same, refused up front when the asset is one the provider only lists: a document, a nested
    //  archive, or a model in a format it does not read
    private KenneyAssetEntry ResolveMaterializable(GameAssetDescriptor descriptor)
    {
        KenneyAssetEntry entry = ResolveEntry(descriptor);

        if (!entry.IsMaterializable)
        {
            throw new UnsupportedGameAssetException(entry.Kind, descriptor.Key);
        }

        return entry;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ThrowIfOtherProvider(KenneyAssetsOptions options)
    {
        if (!string.Equals(options.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"These options name the asset provider '{options.ProviderId}' while this provider is " +
                $"'{ProviderId}'. A provider's identifier is fixed when it is created.",
                nameof(options));
        }
    }

    private void ReleaseEverything()
    {
        _modelReader.Dispose();

        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }

        _sources.Clear();
    }
}
