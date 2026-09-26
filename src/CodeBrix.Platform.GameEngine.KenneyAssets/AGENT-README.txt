================================================================================
AGENT-README: CodeBrix.Platform.GameEngine.KenneyAssets
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.GameEngine.KenneyAssets lets a game load the assets of a
KENNEY ASSET BUNDLE straight out of the bundle. Target: .NET 10 or later.

Kenney (www.kenney.nl) publishes game art, audio, fonts and 3D models as zip
bundles, all of it CC0. A bundle holds a licence file, sprite sheets with their
TextureAtlas XML documents, loose images, .ogg sounds, .ttf fonts, .svg vectors,
Tiled .tmx maps with their .tsx tile sets, glTF models with their textures — and
a certain amount of content no engine can use (Flash exports, authoring
sources, other engines' project files). This package reads that layout as it is:
NOTHING HAS TO BE UNPACKED, RENAMED OR REPACKED, and a game ships the bundles it
downloaded.

The engine package defines the seam — IGameAssetProvider, the capability
interfaces, and the provider registry at Engine.Managers.AssetProviders — and
this package fills it with a provider for Kenney bundles. One call attaches it:

    KenneyGameAssetProvider kenney = Engine.Instance.UseKenneyAssets(
        "assets/kenney_pixel-platformer.zip",
        "assets/kenney_sci-fi-sounds.zip");

From then on every asset in those bundles has a KEY, and the engine's own
registry turns a key into an engine object: a Tilesheet, an AudioResource, an
SKTypeface, scene layers imported from a tile map, or model data.

    Tilesheet sheet = Engine.Instance.Managers.AssetProviders.LoadTilesheet(
        "kenney:pixel-platformer/Tiles/tile_0001");

TWO LAYERS, AND THAT IS THE WHOLE IDEA. A CATALOG answers "what is in these
bundles?" without opening a single picture, and MATERIALIZING turns one
catalogued asset into the engine object that asset is for, registered under its
own key. Nothing Kenney-shaped lives in the engine, and the engine's registries
are where the results land, so game code addresses a Kenney asset exactly the
way it addresses art the game made itself.

THE BUNDLES ARE NOT IN THIS PACKAGE. It carries no art, no audio and no fonts:
it is the reader. A game ships the bundles it uses and points this package at
them. Kenney's content is CC0 (see LICENSING OF KENNEY CONTENT at the end).

OTHER PACKAGES FROM THE SAME REPOSITORY
---------------------------------------
  CodeBrix.Platform.GameEngine.MitLicenseForever — the game engine itself
  (engine core + CodeBrix.Platform host layer). License: MIT. It is a hard
  dependency of this package; see AGENT-README.txt in the repository root for
  everything about scenes, sprites, tilesheets, animation, rendering, audio and
  the engine lifecycle, and for the ASSET PROVIDERS section that documents the
  seam this package plugs into. THIS file covers Kenney bundles only.

  CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever — gamepads (optional
  add-on, unrelated to assets); see
  src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt.

INSTALLATION
============
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever

The assembly and namespaces are CodeBrix.Platform.GameEngine.KenneyAssets[.*]
(WITHOUT the license suffix).

License: MIT — the code in this package. The Kenney bundles a game reads with it
are CC0 and are the game's own content, not part of this package.

NuGet dependencies (pulled in automatically, listed by id):
    CodeBrix.Platform.GameEngine.MitLicenseForever   -- the engine + host
    CodeBrix.Graphics3D.Gltf2.MitLicenseForever      -- glTF model reading

The engine dependency is an ORDINARY PackageReference on a published version,
not a lock-step pairing: this package is versioned and published INDEPENDENTLY
of the engine package and the two do NOT share a version number. Take the latest
of each; there is no "matching versions" rule to observe.

NO NATIVE PREREQUISITE, NOTHING TO INSTALL ON THE TARGET MACHINE. Zip reading,
image decoding, audio decoding, SVG rasterizing and font loading all come from
the engine's own dependency set; the glTF reader is fully managed and the model
renderer in this package is a software rasterizer, so a pre-rendered model sheet
needs no GPU and works headless.

WHERE TO PUT THE BUNDLES. Anywhere the game can open by path. The usual shape is
a content folder copied to the output directory, addressed from
AppContext.BaseDirectory so the path does not depend on the working directory:

    string assets = Path.Combine(AppContext.BaseDirectory, "assets");
    Engine.Instance.UseKenneyAssets(
        Path.Combine(assets, "kenney_pixel-platformer.zip"));

An EXTRACTED bundle folder works just as well as a zip, and so does a folder
holding many extracted bundles (see KenneyAssetsOptions.RecursiveFolders).

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.GameEngine.KenneyAssets;          // the ONE entry
                                                              //   point:
                                                              //   UseKenneyAssets
                                                              //   (and
                                                              //   RegisterKenneyAssets)
                                                              //   + the provider,
                                                              //   its options,
                                                              //   KenneyPackSummary
                                                              //   and the result
                                                              //   types
    using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;  // KenneyAssetProperties
    using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;  // TiledMapParseException

THE PUBLIC SURFACE OF THIS PACKAGE IS SMALL:
EngineKenneyAssetsExtensions, KenneyAssetsOptions, KenneyGameAssetProvider,
KenneyPackSummary, KenneyAssetProperties and TiledMapParseException, plus the
result types of registering and checking keys (KenneyAssetsRegistration,
KenneySourceResult, KenneySourceStatus, KenneyKeyCheck, KenneyKeyStatus), which
live in the root namespace like the entry point. Everything
else — the archive readers, the classifier, the atlas and Tiled parsers, the
materializers, the glTF reader and the model rasterizer — is internal on
purpose: it is transformation logic, and a consumer works in terms of the
engine's own types instead.

From the engine package (this is where almost everything a game touches lives):

    using CodeBrix.Platform.GameEngine;                    // Engine
    using CodeBrix.Platform.GameEngine.Assets.Providers;   // GameAssetDescriptor,
                                                           //   GameAssetQuery,
                                                           //   GameAssetKind,
                                                           //   TilesheetMaterializeOptions,
                                                           //   TiledMapImportOptions,
                                                           //   ModelMaterializeOptions,
                                                           //   ModelRenderOptions,
                                                           //   TiledMapImport,
                                                           //   TiledObjectGroup,
                                                           //   TiledObject,
                                                           //   TiledTileInfo,
                                                           //   UnsupportedGameAssetException
    using CodeBrix.Platform.GameEngine.Assets.Models;      // GameModel,
                                                           //   GameModelMesh,
                                                           //   GameModelMaterial,
                                                           //   GameModelAnimationClip
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets; // Tilesheet,
                                                           //   TilesheetRegion
    using CodeBrix.Platform.GameEngine.Drawing.Animation;  // FrameSequence, Cycle
    using CodeBrix.Platform.GameEngine.Audio;              // AudioResource
    using CodeBrix.Platform.GameEngine.Scenes;             // Scene, SceneLayer

ASSET KEYS
==========
Every asset is addressed by ONE STRING:

    <providerId>:<pack-slug>/<path-inside-the-pack>

    kenney:puzzle-pack/PNG/Double/ballBlue
    kenney:puzzle-pack/Spritesheet/spritesheet_default
    kenney:sci-fi-sounds/Audio/laserSmall_000
    kenney:simulated-bundle/Fonts/Kenney Space
    kenney:simulated-bundle/Tiled/tilemap-example-a
    kenney:blocky-characters/Models/GLB format/character-a
    kenney:blocky-characters/Models/FBX format/character-a.fbx
    kenney:blocky-characters/License.txt

THE RULES, all of them:

  * providerId is "kenney" unless the game asked for another one
    (KenneyAssetsOptions.ProviderId). It is the prefix the engine's registry
    routes on, so it may not contain a colon.
  * pack-slug comes from the pack's licence TITLE LINE, lower-cased and
    hyphenated — "Puzzle Pack (1.1)" gives puzzle-pack — and from the zip file
    or folder name when the pack carries no licence file. Two packs wanting one
    slug are told apart by a -2, -3 suffix on the later arrival, which appears
    in its keys. The slug is what GameAssetDescriptor.Pack carries and what
    GameAssetQuery.Pack matches; the pack's human name is in
    Properties["packName"] and in KenneyPackSummary.DisplayName.
  * The path is the asset's path inside the pack, with forward slashes, keeping
    the bundle's own folders — which is what tells PNG/Default/ballBlue from
    PNG/Double/ballBlue, two different pictures with one file name.
  * THE EXTENSION IS DROPPED from an asset the provider can materialize, and
    KEPT by one that is listed for discovery only. That is not decoration: a
    Kenney model ships as character-a.glb, character-a.fbx, character-a.obj and
    character-a.mtl, which would otherwise be four files wanting one key. Ask
    for the .glb without an extension, and for anything non-materializable with
    the extension it has on disk.
  * Should two materializable assets of one pack still want the same key, the
    first of them — ordered by asset kind, then by path — keeps the short key
    and the others keep their extension. The outcome is the same on every run,
    and the collision is reported in KenneyGameAssetProvider.Warnings.
  * KEY LOOKUP IS CASE-INSENSITIVE, and TryDescribe also accepts the key without
    its "kenney:" prefix.
  * THE KEY IS ALSO THE REGISTRY KEY of the materialized object, so
    TilesheetRegistry.Instance["kenney:puzzle-pack/PNG/Double/ballBlue"] is the
    sheet the provider registered.

ALWAYS PASS THE KEY THE PROVIDER GAVE YOU — descriptor.Key, or the exact
spelling Describe reported. Asset keys are matched case-insensitively but the
engine's TilesheetRegistry is ORDINAL, so two spellings of one key resolve to
one asset and would register TWO tilesheets for it.

WHAT IS NOT AN ASSET OF ITS OWN
-------------------------------
  * A sprite atlas is ONE asset, keyed by its XML document. The sheet image it
    cuts frames from is not listed separately — addressing it alone would hand
    back a picture with no frames.
  * A tile set's grid image is reached through the map that uses it, and a .tsx
    tile set document is not an asset at all.
  * An .xml file that is not a TextureAtlas document, or whose sheet image is
    missing from the pack, stays an ordinary Document (and the missing image is
    reported in Warnings).
  * The one-image-per-tile pictures of an image-collection tile set ARE listed
    individually. They are ordinary sprites, and a map built on such a tile set
    cannot be imported anyway (see TILED MAPS), so listing them is the only way
    to reach that art.
  * Documents, nested zips, Flash exports, authoring sources, web fonts, other
    engines' project files and a .gltf model's satellite .bin buffer are all
    LISTED — with materializable = "false" — so a browser can show a bundle
    honestly. OpenRaw opens any of them; the materializing calls refuse them
    with UnsupportedGameAssetException.

CORE API REFERENCE
==================

EngineKenneyAssetsExtensions (namespace CodeBrix.Platform.GameEngine.KenneyAssets)
---------------------------------------------------------------------------------
The entry point for the whole package.

    public static class EngineKenneyAssetsExtensions
    {
        public static KenneyGameAssetProvider UseKenneyAssets(
            this Engine engine, params string[] zipFilesOrFolders);

        public static KenneyGameAssetProvider UseKenneyAssets(
            this Engine engine, KenneyAssetsOptions options);

        public static KenneyAssetsRegistration RegisterKenneyAssets(
            this Engine engine, params string[] zipFilesOrFolders);

        public static KenneyAssetsRegistration RegisterKenneyAssets(
            this Engine engine, KenneyAssetsOptions options);
    }

  * Builds the provider, registers the sources, puts the provider in
    Engine.Managers.AssetProviders and returns it. Nothing else has to be
    plumbed, and there is no module initializer: NOTHING HAPPENS UNTIL A GAME
    CALLS THIS.
  * CALLING IT AGAIN ADDS SOURCES to the provider already serving that
    identifier and returns that same provider. That is how downloadable content
    or a mod folder joins the catalog later. Two sets of assets that must stay
    apart take one ProviderId each.
  * A SOURCE THAT CANNOT BE READ COSTS THE GAME THOSE ASSETS, NOT ITS START-UP.
    By default a missing or damaged bundle is written to the engine log and
    recorded in Warnings, and the other sources still register. Turn
    KenneyAssetsOptions.IgnoreUnreadableSources off while developing, when a
    bundle that is not there is a mistake worth stopping for.
  * Both overloads log one information line naming the provider, the pack count
    and the asset count, then one warning line per thing this call could not do
    exactly.
  * Throws ArgumentNullException for a null engine or a null argument;
    InvalidOperationException when ANOTHER KIND of provider already holds the
    identifier (the registry's own rule is replace-and-dispose, and doing that
    silently would dispose a provider whose owner is still using its keys);
    ArgumentException for a blank ProviderId or one containing a colon.
  * WHERE TO PUT THE CALL: any time after the engine has been initialized and
    before the assets are needed. In a CodeBrixGameHost game (Mode A) the
    natural place is the LoadAssets override, ahead of LoadTilesheets; a
    Mode B game registers in OnLoadContent.
  * RegisterKenneyAssets is UseKenneyAssets MADE SAFE TO CALL AGAIN, with a
    report. A zip file or folder the provider already read (compared by full
    path) is NOT added a second time - UseKenneyAssets would add it again under
    a "-2" slug with a second set of keys - and the call returns a
    KenneyAssetsRegistration saying per path what happened. For a path read
    once the outcome, the log lines and the exceptions are exactly those of
    UseKenneyAssets. See REGISTERING MORE THAN ONCE below.

KenneyAssetsOptions (namespace CodeBrix.Platform.GameEngine.KenneyAssets)
------------------------------------------------------------------------
    public sealed record KenneyAssetsOptions
    {
        public IReadOnlyList<string> Sources { get; init; } = [];
        public string ProviderId { get; init; }        // "kenney"
        public bool RecursiveFolders { get; init; }    // false
        public bool IgnoreUnreadableSources { get; init; } = true;
    }

  * Sources — the zip files and folders to register. A zip file is always ONE
    pack. A folder that carries its own License.txt is one pack.
  * ProviderId — the key prefix. Defaults to
    KenneyGameAssetProvider.DefaultProviderId ("kenney"). There is no separate
    key-prefix knob: the identifier IS the prefix, and the registry routes keys
    by it.
  * RecursiveFolders — turn this ON to point at a folder that HOLDS pack
    folders, which is the shape of Kenney's own "all in one" download
    (2D assets/<Pack>/License.txt). Each child folder carrying a licence file
    becomes a pack of its own. The search stops at the first folder with a
    licence file and never goes more than three folder levels down. It has no
    effect on a zip file, or on a folder that carries a licence file itself.
  * IgnoreUnreadableSources — a source that will not open becomes a warning
    (true, the default) or an exception (false: FileNotFoundException for a
    missing path, IOException for a damaged zip, ArgumentException for a blank
    one).
  * It is a record, so keep one instance as the house settings and derive the
    rest with a `with` expression.

KenneyGameAssetProvider (namespace CodeBrix.Platform.GameEngine.KenneyAssets)
----------------------------------------------------------------------------
    public sealed class KenneyGameAssetProvider
        : IGameAssetProvider, ITilesheetAssetSource, IAudioAssetSource,
          IFontAssetSource, ITiledMapAssetSource, IModelAssetSource
    {
        public const string DefaultProviderId = "kenney";

        public KenneyGameAssetProvider(KenneyAssetsOptions? options = null);

        public string ProviderId { get; }
        public IReadOnlySet<GameAssetKind> SupportedKinds { get; }
        public IReadOnlyList<KenneyPackSummary> Packs { get; }
        public int AssetCount { get; }
        public IReadOnlyList<string> Warnings { get; }

        public IReadOnlyList<string> AddSources(KenneyAssetsOptions options);
        public IReadOnlyList<string> AddSources(params string[] zipFilesOrFolders);
        public KenneyAssetsRegistration AddNewSources(KenneyAssetsOptions options);
        public KenneyAssetsRegistration AddNewSources(params string[] zipFilesOrFolders);

        public IReadOnlyList<string> CreditLines { get; }
        public KenneyKeyCheck CheckKeys(IEnumerable<string> keys);
        public void WriteKeyCatalog(TextWriter writer, GameAssetQuery? query = null);
        public string GetKeyCatalog(GameAssetQuery? query = null);

        public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null);
        public bool TryDescribe(string key, out GameAssetDescriptor? descriptor);
        public Stream OpenRaw(GameAssetDescriptor descriptor);

        public Tilesheet MaterializeTilesheet(GameAssetDescriptor descriptor,
            TilesheetMaterializeOptions? options = null);
        public AudioResource MaterializeAudio(GameAssetDescriptor descriptor,
            float volume = 1.0f, float pan = 0.0f);
        public SKTypeface MaterializeFont(GameAssetDescriptor descriptor);
        public TiledMapImport MaterializeTiledMap(GameAssetDescriptor descriptor,
            Scene scene, TiledMapImportOptions? options = null);
        public GameModel MaterializeModel(GameAssetDescriptor descriptor,
            ModelMaterializeOptions? options = null);
        public GameModelAnimationClip MaterializeModelAnimation(
            GameAssetDescriptor descriptor, string animationName,
            int framesPerSecond = 24);

        public void Dispose();
    }

  * PREFER THE REGISTRY to the provider's own Materialize* calls:
    Engine.Managers.AssetProviders.LoadTilesheet(key) resolves the provider from
    the key's prefix, so game code does not have to hold the provider at all.
    The Materialize* methods are the same work with a descriptor in hand, and
    they are what the registry calls.
  * SupportedKinds is Image, SpriteAtlas, Audio, Font, Vector, TiledMap and
    Model3D. The registry gates on this set AS WELL AS on the capability
    interface, so an asset of any other kind raises
    UnsupportedGameAssetException before the provider is even asked.
  * Packs is one KenneyPackSummary per registered pack, in registration order —
    enough for a pack list, an asset browser or a credits screen.
  * AssetCount is how many assets can be addressed by key, across every pack.
  * Describe(null) lists everything. A GameAssetQuery filters by Pack (the
    slug), Kind, NameContains (matched against the file stem) and PathPrefix,
    combined with AND. THE DESCRIPTOR FOR ONE ASSET IS THE SAME INSTANCE EVERY
    TIME, so descriptors can be compared by reference and cached freely.
  * TryDescribe takes "kenney:pack/path" or the bare "pack/path"; another
    provider's prefix is simply not found.
  * OpenRaw is the escape hatch and opens ANY listed asset, including the
    documents, nested archives and model formats the provider cannot
    materialize. The caller disposes the stream.
  * AddSources returns the warnings THAT CALL produced, which is what
    UseKenneyAssets logs. The params overload takes the DEFAULT settings rather
    than remembering the flags the provider was created with, so
    provider.AddSources(paths) and UseKenneyAssets(engine, paths) mean exactly
    the same thing. AddSources(options) throws ArgumentException when the
    options name a different provider — an identifier is fixed at construction.
  * AddNewSources is AddSources that leaves out a path the provider already
    holds (earlier, or earlier in the same call) and returns a
    KenneyAssetsRegistration instead of bare warnings; it is what
    RegisterKenneyAssets calls. The same bundle copied to ANOTHER path is a
    different source and is read again.
  * CreditLines is one KenneyPackSummary.CreditLine per registered pack, in
    registration order, a line two packs share listed once.
  * CheckKeys and WriteKeyCatalog / GetKeyCatalog: see CHECKING KEYS and THE
    KEY CATALOG below. Neither materializes anything.
  * Adding sources is safe while the game is reading: the catalog is rebuilt and
    swapped in, so a lookup in flight sees either the old catalog or the new
    one, and assets already materialized keep working.
  * Warnings is a SNAPSHOT that only grows: source failures first, then catalog
    warnings (a missing atlas image, an unparsable map, two files wanting one
    key, a slug made unique), then what materializing had to give up (colliding
    atlas frame names, a frame outside its sheet, an unusable tile size). A MAP
    IMPORT'S OWN WARNINGS ARE NOT HERE — they belong to that one import and come
    back in TiledMapImport.Warnings.
  * There is normally no reason to construct one by hand. Doing so registers
    nothing with the engine, so keys will not resolve through the registry.

KenneyPackSummary (namespace CodeBrix.Platform.GameEngine.KenneyAssets)
----------------------------------------------------------------------
    public sealed record KenneyPackSummary
    {
        public required string Slug { get; init; }
        public required string DisplayName { get; init; }
        public string? Version { get; init; }
        public string? LicenseTitle { get; init; }
        public required string SourcePath { get; init; }
        public int AssetCount { get; init; }
        public int MaterializableAssetCount { get; init; }
        public IReadOnlyDictionary<GameAssetKind, int> CountsByKind { get; init; }
        public string CreditLine { get; }           // "<title> - Kenney (CC0)"
        public override string ToString();          // "slug (Display Name)"
    }

  * Slug is the pack part of a key; DisplayName and Version come from the
    licence title line; LicenseTitle is that line as written, which is the line
    to credit the pack by.
  * AssetCount counts ADDRESSABLE assets, so it always equals
    Describe(new GameAssetQuery { Pack = Slug }).Count — fewer than the files in
    the bundle, for the reasons under WHAT IS NOT AN ASSET OF ITS OWN.
  * CountsByKind OMITS a kind the pack holds none of, rather than mapping it to
    zero. Use GetValueOrDefault.
  * CreditLine is the line a credits screen shows for the pack: the licence
    title, trimmed (the display name when the pack has no licence file),
    followed by " - Kenney (CC0)", e.g. "Puzzle Pack (1.1) - Kenney (CC0)".

KenneyAssetsRegistration, KenneySourceResult, KenneySourceStatus
----------------------------------------------------------------
    public sealed class KenneyAssetsRegistration
    {
        public KenneyGameAssetProvider Provider { get; }
        public IReadOnlyList<KenneySourceResult> Sources { get; }   // one per path, in order
        public IReadOnlyList<KenneyPackSummary> Packs { get; }      // read now or earlier, once each
        public IReadOnlyList<string> Warnings { get; }              // what THIS call added
        public IReadOnlyList<KenneySourceResult> Unavailable { get; } // Missing + Unreadable
    }

    public sealed record KenneySourceResult
    {
        public required string SourcePath { get; init; }   // as passed in
        public required KenneySourceStatus Status { get; init; }
        public IReadOnlyList<KenneyPackSummary> Packs { get; init; }
        public string? Message { get; init; }       // why, for Missing / Unreadable
        public bool IsAvailable { get; }            // Read or AlreadyRegistered
        public override string ToString();          // "kenney_planets.zip: Read - planets (Planets)"
    }

    public enum KenneySourceStatus { Read, AlreadyRegistered, Missing, Unreadable }

  * Packs of an AlreadyRegistered result are the packs of the EARLIER
    registration, so a game logs one line per pack the same way on every call.
  * Missing = no file or folder at the path (or a blank path); Unreadable = it
    exists but would not open (a damaged zip). Both only when
    IgnoreUnreadableSources is on (the default); with it off they throw, as
    UseKenneyAssets does.

KenneyKeyCheck, KenneyKeyStatus
-------------------------------
    public sealed class KenneyKeyCheck
    {
        public IReadOnlyList<KenneyKeyStatus> Keys { get; }   // one per key, in order
        public IReadOnlyList<string> MissingKeys { get; }
        public bool AllFound { get; }
        public IReadOnlyDictionary<GameAssetKind, int> CountsByKind { get; } // found keys only
        public long TotalSizeBytes { get; }
        public KenneyKeyStatus this[string key] { get; }      // case-insensitive
        public override string ToString();  // "72 key(s) checked - Audio 36, ... - 0 missing, 9.1 MB"
    }

    public sealed record KenneyKeyStatus(string Key, bool Found, GameAssetKind Kind,
        long SizeBytes, int AtlasFrameCount);

KenneyAssetProperties (namespace …KenneyAssets.Sources)
------------------------------------------------------
The names of the entries this provider puts in GameAssetDescriptor.Properties.
USE THE CONSTANTS, not string literals; the dictionary is case-insensitive.

    public static class KenneyAssetProperties
    {
        public const string PackName = "packName";
        public const string PackVersion = "packVersion";
        public const string License = "license";
        public const string Extension = "extension";
        public const string Materializable = "materializable";
        public const string AtlasFrameCount = "atlasFrameCount";
        public const string AtlasImagePath = "atlasImagePath";
        public const string MapWidth = "mapWidth";
        public const string MapHeight = "mapHeight";
        public const string TileWidth = "tileWidth";
        public const string TileHeight = "tileHeight";
        public const string TileLayerCount = "tileLayerCount";
        public const string TilesetCount = "tilesetCount";
        public const string TiledMapError = "tiledMapError";
    }

  * Materializable is ALWAYS present, as "true" or "false". Everything else is
    present when it applies: the atlas entries on a sprite atlas, the map
    entries on a tile map that parsed, TiledMapError on one that did not.
  * This is how a browser or a loading screen can show a map's size, or an
    atlas's frame count, without opening anything.

TiledMapParseException (namespace …KenneyAssets.Parsing)
-------------------------------------------------------
    public sealed class TiledMapParseException : FormatException
    {
        public TiledMapParseException(string message);
        public TiledMapParseException(string message, string? documentPath);
        public TiledMapParseException(string message, string? documentPath,
            Exception innerException);
        public string? DocumentPath { get; }
    }

It is public because importing a map can throw it and a game catches it. The
message names the feature that could not be read, and DocumentPath names the
document inside the pack.

THE ENGINE-SIDE CALLS YOU ACTUALLY USE
--------------------------------------
These come from the engine package, namespace
CodeBrix.Platform.GameEngine.Assets.Providers. They are listed here because they
are how a game reaches a Kenney asset.

    Engine.Managers.AssetProviders          // GameAssetProviderRegistry
        Tilesheet LoadTilesheet(string key, TilesheetMaterializeOptions? options = null)
        AudioResource LoadAudio(string key, float volume = 1.0f, float pan = 0.0f)
        SKTypeface LoadFont(string key)
        TiledMapImport ImportTiledMap(string key, Scene scene,
                                     TiledMapImportOptions? options = null)
        GameModel LoadModel(string key, ModelMaterializeOptions? options = null)
        GameModelAnimationClip LoadModelAnimation(string key, string animationName,
                                                 int framesPerSecond = 24)
        IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null)
        bool TryDescribe(string key, out GameAssetDescriptor? descriptor)
        bool TryFind(string key, out IGameAssetProvider? provider)
        bool Unregister(string providerId, bool dispose = true);  void Clear()

  * A key no provider owns raises KeyNotFoundException. An asset whose kind the
    route cannot take raises UnsupportedGameAssetException, whose Kind and Key
    say what was asked for.
  * Describe on the registry is the UNION over every registered provider, so a
    game with two providers lists both in one call.

TilesheetMaterializeOptions — the picture and model-sprite knobs:

    Size? TileSize                 // adds a uniform grid region to a loose image
    Spacing Padding                // per-tile padding of that grid (Spacing.None)
    Spacing Margin                 // margin removed before the grid (Spacing.None)
    TileCollisionType CollisionType // applied to every region built (None)
    CollisionAdjust? CollisionAdjust // ditto
    bool StripFrameExtension       // true: atlas frame "ballBlue.png" -> "ballBlue"
    Size? VectorRasterSize         // SVG raster size
    float? VectorScale             // SVG scale of the intrinsic size
    ModelRenderOptions? ModelRender // the model sprite route
    string? RegisterAs             // register under this key instead

TiledMapImportOptions — the map knobs:

    int ZOrderBase                 // 0; each layer takes ZOrderBase + its
                                   //   position among ALL the map's layers
    float Parallax                 // 1.0; used for a layer that declares none
    Func<TiledTileInfo, TileCollisionType>? CollisionSelector
    string? CollisionProfileName   // applied to every imported layer
    Func<string, bool>? LayerFilter // by layer name
    bool ImportObjectLayers        // true; object layers come back as DATA

ModelMaterializeOptions — the model-data knobs:

    IReadOnlyList<string>? AnimationNames  // null = bake nothing
    bool BakeAllAnimations                 // false; wins over AnimationNames
    int AnimationFramesPerSecond           // 24

ModelRenderOptions — the model-sprite knobs (defaults in brackets): FrameSize
[128x128], Directions [8], StartYawDegrees [0], PitchDegrees [30], Projection
[Orthographic], FieldOfViewDegrees [35], AnimationNames [none], IncludeRestPose
[true], AnimationFramesPerSecond [12], Supersample [2], LightDirection,
AmbientLight [0.45], FitPadding [0.06], and the constant RestPoseRegionName
("rest"). The engine's own documentation on that record carries the OUTPUT
LAYOUT CONTRACT, which this provider implements to the letter; it is repeated
under 3D MODELS below.

PER-KIND RULES
==============

IMAGES (.png, .jpg, .jpeg, .gif, .bmp, .webp)
---------------------------------------------
One tilesheet per image. Its `default` region holds THE WHOLE IMAGE AS ONE TILE,
so the picture is sheet[0, 0] — the frame a sprite is created from.

    Tilesheet ball = providers.LoadTilesheet("kenney:puzzle-pack/PNG/Double/ballBlue");
    Sprite sprite = SpriteManager.Instance.CreateSprite(layer, ball[0, 0]);

Ask for TileSize and a SECOND region named "grid" is added over the same image,
honouring Padding and Margin, so both addressings work on one sheet:

    Tilesheet tiles = providers.LoadTilesheet(
        "kenney:pixel-platformer/Tilemap/tilemap-backgrounds_packed",
        new TilesheetMaterializeOptions { TileSize = new Size(24, 24) });
    Frame sky = tiles["grid", 3, 0];

A non-positive TileSize, or one that leaves no whole tile, is a WARNING (the
sheet is still produced, without a usable grid region) rather than an exception.

SPRITE ATLASES (a TextureAtlas .xml beside its sheet image)
-----------------------------------------------------------
One tilesheet holding the sheet image, plus ONE SINGLE-TILE REGION PER FRAME,
named after the frame with a known image extension stripped
(StripFrameExtension, default true). A frame is addressed BY NAME:

    Tilesheet sheet = providers.LoadTilesheet(
        "kenney:puzzle-pack/Spritesheet/spritesheet_default");
    Frame blue = sheet["ballBlue", 0, 0];          // NOT sheet[0, 0]

  * The column and row are always 0, 0: each region is exactly one frame.
  * Only a KNOWN image extension is stripped, so hud_1.5x.png becomes hud_1.5x
    rather than hud_1, and a name that is nothing but an extension keeps its
    name. Pass StripFrameExtension = false to keep the names verbatim.
  * THE ATLAS SHEET'S `default` REGION DELIBERATELY HOLDS NO TILES. Giving it
    the whole image as one tile would slice a second full copy of a sheet
    bitmap — tens of megabytes on a large sheet — for a picture nobody draws.
    So sheet[0, 0] on an atlas sheet yields a frame with no bitmap; address
    frames by name.
  * Two frame names that collide after stripping, and a frame rectangle that
    falls outside the sheet image, are WARNINGS: the first name wins, the stray
    frame is left out, and the sheet is still usable.
  * Region names are matched case-insensitively, and Tilesheet.GetRegion is a
    dictionary lookup, so a sheet with hundreds of frames costs nothing per
    lookup.

AUDIO (.ogg, .wav, .mp3, .flac)
-------------------------------
One AudioResource per asset, registered in the engine's AudioResourceManager
under the asset's key.

    AudioResource jump = providers.LoadAudio(
        "kenney:sci-fi-sounds/Audio/laserSmall_000", volume: 0.8f);
    jump.Play();

  * Kenney's audio packs are .ogg. .opus is NOT built into the engine: register
    CodeBrix.Audio.Opus.BsdLicenseForever and the format simply works here too,
    because the engine's reader registry is what decides.
  * A format the engine has no reader for raises NotSupportedException, whose
    message lists the formats it does have and names the package to add.
  * Bytes that will not decode raise InvalidDataException naming the key, the
    asset's path inside the pack and the pack's source path.
  * VOLUME AND PAN ARE ONLY APPLIED BY THE CALL THAT CREATES THE RESOURCE. A
    second LoadAudio for the same key hands back the resource already
    registered and IGNORES this call's volume and pan — set them on the resource
    itself, or unload the key first.

FONTS (.ttf, .otf)
------------------
One SKTypeface per asset, registered in the engine's FontManager under the
asset's key, and returned so it can be handed straight to a text element.

    SKTypeface font = providers.LoadFont("kenney:simulated-bundle/Fonts/Kenney Space");
    hud.SetFont(font, 28f);

  * IDENTIFY A FONT BY ITS KEY OR BY FontManager.GetFamilyName(key) — NEVER BY
    SKTypeface.FamilyName. FamilyName is whatever the platform's native font
    back end reports, and it differs for the same file: Kenney Future Narrow
    is "Kenney Future Narrow" on Linux but "Kenney Future" on Windows
    (DirectWrite moves the width word "Narrow" out of the family name). The
    font manager reads the family name from the font file itself, so it is the
    same everywhere:
        providers.LoadFont(key);
        string family = FontManager.Instance.GetFamilyName(key);
            // "Kenney Future Narrow" on Windows, Linux and macOS
        FontManager.Instance.TryGetByFamilyName("Kenney Future Narrow", out var face);
  * KENNEY'S INPUT-PROMPT FONTS ARE ICON FONTS. A font such as Kenney Input
    Touch carries keyboard, mouse, gamepad and touch glyphs and NO BASIC LATIN
    at all: every letter maps to glyph 0. It loads fine and is the right way to
    draw a "press this button" prompt, but it can never draw text. Use a text
    face — Kenney Space, Kenney Future Narrow — for words.
  * A nested "Webfonts *.zip" inside a bundle is an Archive, not a font: it is
    listed and never opened. Extract it yourself if you want the web formats.
  * FontManager is not internally synchronized. This package serializes its own
    loads, so concurrent materializing of one key is safe; a game that ALSO
    registers fonts from its own threads should serialize those calls.

SVG VECTORS (.svg)
------------------
Rasterized once, at load time, into a tilesheet that behaves exactly like a
loose image: the `default` region is the whole bitmap as one tile.

    Tilesheet icon = providers.LoadTilesheet(
        "kenney:puzzle-pack/Vector/puzzleAssets_vector",
        new TilesheetMaterializeOptions { VectorRasterSize = new Size(256, 256) });

  * Size = VectorRasterSize, else the intrinsic size times VectorScale, else the
    intrinsic size. A non-positive size or scale raises
    ArgumentOutOfRangeException.
  * EITHER DIMENSION IS CAPPED AT 4096 PIXELS, keeping the aspect ratio of what
    was asked for. Nothing is thrown; the raster is simply no larger than that.
  * The document is rasterized from ITS OWN top-left corner, so vector art an
    exporter left away from the origin — or at negative coordinates, which
    happens in this corpus — comes out whole rather than cropped or empty.
  * A vector wanted at several sizes is materialized once per size, each under
    its own key with RegisterAs (see LAZINESS, CACHING AND IDEMPOTENCE).
  * Kenney ships a .swf beside many an .svg. It is a Flash export: listed, never
    materialized.

TILED MAPS (.tmx, with its .tsx tile sets)
------------------------------------------
A map is imported INTO A SCENE THE CALLER OWNS: one SceneLayer per tile layer,
one registered Tilesheet per tile set, and the map's object layers handed back as
data.

    Scene scene = new Scene();
    TiledMapImport import = providers.ImportTiledMap(
        "kenney:simulated-bundle/Tiled/tilemap-example-a", scene,
        new TiledMapImportOptions { ZOrderBase = 10 });

    foreach (SceneLayer layer in import.Layers) { /* bottom-most layer first */ }
    foreach (string warning in import.Warnings) { Engine.Logger.LogWarning(warning); }

THE RESULT (TiledMapImport) carries Scene (the one passed in), Layers in Tiled
order with the bottom-most first, Tilesheets (one per tile set the map
REFERENCES, so the list does not change with a LayerFilter), ObjectGroups,
MapSizePx, TileSize and Warnings.

TILESHEET KEYS AND REGIONS:
  * Each tile set becomes a tilesheet keyed <map key>#<tile set name>, the name
    being the tile set's own name attribute, else the .tsx file stem, else
    tileset-<index>; a name used twice in one map gets a -2, -3 suffix.
  * The tile grid is in a region called "tiles". FLIP BITS ARE PRE-BAKED: an
    engine frame has no flip of its own, so for every flip combination the map
    actually uses the whole grid is baked again into the same bitmap as another
    region — "tiles-fh", "tiles-fv", "tiles-fd", "tiles-fhv", "tiles-fvd", and
    so on, h then v then d. Flipped cells are assigned from those regions.
  * THE `default` REGION OF A TILE SET SHEET IS MEANINGLESS: the public registry
    loader always adds one, it spans the whole bitmap including the baked
    variant blocks, and it has no tile size. Address tiles through "tiles" and
    its variants, never sheet[0, 0].
  * Tile set spacing and margin are honoured; a tile set larger than the map's
    grid cell — Kenney's 24 pixel characters on an 18 pixel map — keeps Tiled's
    bottom-left anchoring through its region's overhang, shifted by
    <tileoffset>.

COLLISION. With no CollisionSelector, a tile collides when it, or the tile set
it belongs to, carries a `collision` custom property: "blocking" or "true" means
Blocking, "trigger" means Trigger, anything else means None (matched ignoring
case and surrounding space). A property ON THE TILE answers for that tile even
when its value is none of those, so a tile can opt out of a tile set that opts
in. The type is recorded on the FRAME, so every cell using that tile inherits
it; a selector that answers differently for two cells sharing one tile is still
honoured, the second cell getting an explicit tile-level type.
CollisionProfileName is applied through each layer's
DefaultTileCollisionProfile and is validated against the scene up front, so a
bad profile name fails before any tile is assigned.

    TiledMapImportOptions options = new()
    {
        CollisionSelector = tile =>
            tile.TilesetName.Contains("water", StringComparison.OrdinalIgnoreCase)
                ? TileCollisionType.Trigger
                : TileCollisionType.None,
        CollisionProfileName = CollisionProfileNames.World,
        LayerFilter = name => !name.Equals("notes", StringComparison.OrdinalIgnoreCase),
    };

OBJECT LAYERS ARE DATA, NEVER SCENE OBJECTS. Each becomes a TiledObjectGroup
carrying Name, Objects, Offset, Visible, Opacity, DocumentIndex and Properties;
each TiledObject carries Id, Name, Type, Bounds (the UNROTATED rectangle as the
map wrote it), Rotation in degrees clockwise, Visible, Properties and — for a
TILE OBJECT — Tile, a TiledTileInfo naming its tile set, its global and local
tile id, its tile and tile-set properties and its flip flags. The group's Offset
is NOT folded into Bounds; apply it yourself if the map uses one. Spawning
anything from that data is the game's job, which is the point: a map says where
the player starts, and only the game knows what a player is.

WHAT IS IMPORTED, AND WHAT IS REFUSED:
  * Imported: ORTHOGONAL, FINITE maps whose layer data is CSV or base64
    (uncompressed, zlib or gzip); external and inline tile sets that cut their
    tiles from one grid image.
  * UnsupportedGameAssetException, with the feature named: an isometric,
    hexagonal or staggered map (the orientation is in the message), an infinite
    map, a tile set holding ONE IMAGE PER TILE, a tile set whose tiles are
    SMALLER than the map's grid cell, and a tile set with no grid image or no
    column count.
  * TiledMapParseException: the map document could not be read at all — and
    note that a map REFUSED WHILE THE PACK WAS CATALOGUED surfaces this way too,
    because the reason was recorded then (it is also in
    Properties["tiledMapError"], which is the cheap way to ask before trying).
    A map holding only object layers and no tile layer is refused this way as
    well.
  * FileNotFoundException for a .tsx or a tile set image that cannot be resolved
    inside the pack — strictly, against the referencing document's folder and
    the folders above it, NEVER by bare file name, because one pack can hold
    three different files called colormap.png.
  * InvalidDataException for a tile set image that will not decode.
  * WARNINGS, not failures: a layer opacity below 1 (the engine has no
    per-layer opacity, so the layer is imported fully opaque, or hidden when the
    opacity is 0), differing horizontal and vertical parallax factors, an image
    layer, a layer group, a stray global tile id no tile set owns, a layer
    larger than the map's grid, per-tile collision SHAPES (the engine's tile
    collision is a rectangle), and an object layer offset.
  * HALF IDEMPOTENT, BY DESIGN: a tilesheet already registered under its key is
    adopted as it is, but LAYERS ARE ALWAYS ADDED to the scene passed in, so
    importing one map into one scene twice gives that scene two sets of layers.
    Import into a fresh scene, or dispose the old one.

3D MODELS (.glb and .gltf materialize; .fbx, .obj, .mtl, .dae, .stl are listed)
------------------------------------------------------------------------------
Kenney's 3D kits ship the same model in several formats. ONLY THE glTF FORMS ARE
READ (.glb, .gltf); the others are catalogued with materializable = "false",
keep their extension in their key, and raise UnsupportedGameAssetException if
asked for. There are TWO ROUTES, and a game can use either or both.

ROUTE 1 — MODEL DATA, for a game that renders 3D itself (or for a future
engine-side 3D feature):

    GameModel model = providers.LoadModel(
        "kenney:blocky-characters/Models/GLB format/character-a");
    GameModelAnimationClip walk = providers.LoadModelAnimation(
        "kenney:blocky-characters/Models/GLB format/character-a", "walk",
        framesPerSecond: 24);

  * GameModel is plain data on System.Numerics: Meshes (flat float arrays for
    positions, normals and texture coordinates, uint indices, a material index),
    Materials (alpha mode and cutoff, base colour factor, the base colour
    texture as RGBA8888 bytes with its width and height, metallic and roughness
    factors, double-sidedness), BoundsMin / BoundsMax / BoundsCenter /
    BoundsRadius, Pivot, TriangleCount, VertexCount, AnimationNames and
    Animations. No disposal, no GPU handles, nothing glTF-shaped.
  * NOTHING IS BAKED UNLESS YOU ASK. AnimationNames is ALWAYS filled, so a
    caller can come back for the clips it wants; Animations holds only what
    ModelMaterializeOptions asked for. Baking is cheap for typical Kenney
    content but it is memory: positions and normals for every vertex of every
    frame.
  * THE THREE GUARANTEES that make a clip playable, and that a renderer may
    rely on: (1) a frame's meshes ALIGN with the model's meshes, one for one,
    with the same vertex count — GameModelAnimationClip.IsCompatibleWith(model)
    checks it; (2) the payloads are GPU-upload friendly — flat float arrays,
    uint indices, RGBA8888 texture bytes; (3) a clip carries Duration and
    FrameRate, so THE CALLER OWNS TIMING (GetFrameIndex(timeSeconds, loop) maps
    a time onto a frame).
  * MaterializeModelAnimation / LoadModelAnimation is the cheap way to add a
    clip later: the parsed document of an animated model is kept, so nothing is
    read again, and the clip is aligned with the model the same asset returns.
    An unknown animation name raises ArgumentException LISTING the names the
    model does offer; fewer than one frame per second raises
    ArgumentOutOfRangeException.
  * MODEL DATA IS REGISTERED NOWHERE. It belongs to the caller. The engine draws
    no 3D — that is what route 2 is for.

ROUTE 2 — PRE-RENDERED SPRITE FRAMES, for a 2D game that wants 3D art:

    Tilesheet hero = providers.LoadTilesheet(
        "kenney:blocky-characters/Models/GLB format/character-a",
        new TilesheetMaterializeOptions
        {
            ModelRender = new ModelRenderOptions
            {
                FrameSize = new Size(96, 96),
                Directions = 8,
                AnimationNames = ["idle", "walk"],
                AnimationFramesPerSecond = 12,
            },
        });

    // Columns are frames, rows are directions: this is row 0's walk cycle.
    FrameSequence walkFacingViewer = new FrameSequence();
    TilesheetRegion walk = hero.GetRegion("walk")!;
    for (int column = 0; column < walk.Columns; column++)
    {
        walkFacingViewer.AddFrame(hero, "walk", column, 0);
    }
    walkFacingViewer.SequenceCycleType = CycleType.Repeating;
    Cycle cycle = new Cycle(walkFacingViewer, 1.0 / 12.0, "hero-walk-0");

THE OUTPUT LAYOUT CONTRACT, which this provider implements exactly:
  * ONE uniform-grid region per rendered animation, NAMED AFTER THAT ANIMATION,
    plus a region named ModelRenderOptions.RestPoseRegionName ("rest") when
    IncludeRestPose is true (it is by default).
  * COLUMNS are animation frames in playback order —
    AnimationFramesPerSecond of them per second of the clip. The rest-pose
    region has exactly ONE column.
  * ROWS are camera directions: Directions of them, row d at camera yaw
    StartYawDegrees + d * 360 / Directions. Yaw is the camera's azimuth about
    the model's up axis; at yaw 0 the camera sits on +Z and a model authored
    facing +Z faces the viewer, and increasing yaw makes the model appear to
    turn to the viewer's left — with the default eight directions, row 2 is its
    left flank and row 4 its back.
  * IN SCREEN TERMS, which is what a game picks a row by: with the default eight
    directions and StartYawDegrees 0 the model FACES toward the viewer
    (down-screen, "south") in row 0, south-west in row 1, screen-left ("west") in
    row 2, north-west in row 3, away from the viewer ("north") in row 4,
    north-east in row 5, screen-right ("east") in row 6 and south-east in row 7.
    A character walking toward the right of the screen is drawn from row 6.
  * EVERY CELL IS EXACTLY FrameSize, so a frame is sheet["walk", frame,
    direction] and the rest pose is
    sheet[ModelRenderOptions.RestPoseRegionName, 0, direction].
  * ONE COMMON FIT — scale and centring — is shared by every direction and every
    frame of the whole sheet, so the model never appears to breathe or slide
    about as it turns. FitPadding is the margin that fit leaves empty.
  * The sheet ALSO carries the registry's automatic `default` region, spanning
    the whole bitmap with no tile size. It is meaningless on a model sheet and
    costs nothing; it is not a defect.
  * A SHEET LARGER THAN 8192 PIXELS in either dimension is refused with
    ArgumentException, whose message says what to reduce: fewer animations, a
    lower AnimationFramesPerSecond, a smaller FrameSize, or several sheets under
    separate RegisterAs keys.
  * Rendering is a pure software rasterizer — z-buffered, textured from the base
    colour map, lambert plus ambient, supersampled and then downscaled by Skia —
    so it needs no GPU and runs headless. Textures are sampled NEAREST and
    wrapped, which is right for Kenney's flat colour-patch atlases;
    antialiasing comes from Supersample.
  * Option values that cannot be met are clamped where clamping is obviously
    right (Supersample to 1..8, AmbientLight to 0..1, FitPadding to 0..0.45,
    PitchDegrees to ±89.5, FieldOfViewDegrees to 1..120) and refused where it is
    not (a FrameSize below one pixel, Directions below one).
  * A model whose texture Skia cannot decode still loads: the material falls
    back to its base colour factor. A model with no triangle geometry at all
    raises InvalidDataException.

DOCUMENTS, ARCHIVES AND EVERYTHING ELSE
---------------------------------------
Listed, never materialized, always openable with OpenRaw. The licence file of a
pack is the interesting one: it is a Document, and KenneyPackSummary.LicenseTitle
already carries its first line, which is the line to credit.

COMPLETE EXAMPLES
=================

REGISTERING, AND SEEING WHAT ARRIVED
------------------------------------
    using CodeBrix.Platform.GameEngine;
    using CodeBrix.Platform.GameEngine.Assets.Providers;
    using CodeBrix.Platform.GameEngine.KenneyAssets;

    string assets = Path.Combine(AppContext.BaseDirectory, "assets");

    KenneyGameAssetProvider kenney = Engine.Instance.UseKenneyAssets(
        Path.Combine(assets, "kenney_pixel-platformer.zip"),
        Path.Combine(assets, "kenney_sci-fi-sounds.zip"));

    foreach (KenneyPackSummary pack in kenney.Packs)
    {
        Engine.Logger.LogInformation(
            "{Pack}: {Assets} asset(s), credit as {Credit}",
            pack.Slug, pack.AssetCount, pack.CreditLine);
    }

    foreach (string warning in kenney.Warnings)
    {
        Engine.Logger.LogWarning("Kenney assets: {Warning}", warning);
    }

FINDING AN ASSET WITHOUT HARD-CODING ITS KEY
--------------------------------------------
    GameAssetProviderRegistry providers = Engine.Instance.Managers.AssetProviders;

    IReadOnlyList<GameAssetDescriptor> jumps = providers.Describe(new GameAssetQuery
    {
        Pack = "sci-fi-sounds",
        Kind = GameAssetKind.Audio,
        NameContains = "laserSmall",
    });

    foreach (GameAssetDescriptor sound in jumps)
    {
        Engine.Logger.LogInformation("{Key} ({Bytes} bytes)", sound.Key, sound.SizeBytes);
    }

    AudioResource laser = providers.LoadAudio(jumps[0].Key);

A KEY THAT MIGHT NOT BE THERE — ask first, so a missing asset is a decision
rather than an exception:

    if (providers.TryDescribe("kenney:pixel-platformer/Tiled/tilemap-example-a",
            out GameAssetDescriptor? map)
        && map.Properties.TryGetValue(KenneyAssetProperties.Materializable, out string? usable)
        && usable == "true")
    {
        TiledMapImport import = providers.ImportTiledMap(map.Key, scene);
    }

A WHOLE LEVEL FROM A TILED MAP
------------------------------
    using CodeBrix.Platform.GameEngine.Physics.Collisions;
    using CodeBrix.Platform.GameEngine.Scenes;

    Scene scene = new Scene();

    TiledMapImport import = Engine.Instance.Managers.AssetProviders.ImportTiledMap(
        "kenney:simulated-bundle/Tiled/tilemap-example-a",
        scene,
        new TiledMapImportOptions
        {
            ZOrderBase = 0,
            CollisionProfileName = CollisionProfileNames.World,
        });

    // The map says where things belong; the game decides what they are.
    foreach (TiledObjectGroup group in import.ObjectGroups)
    {
        foreach (TiledObject spawn in group.Objects)
        {
            if (spawn.Type == "player")
            {
                PlacePlayer(spawn.Bounds.X + group.Offset.X,
                            spawn.Bounds.Y + group.Offset.Y);
            }
        }
    }

    host.Bind(scene);

AN ATLAS SPRITE, A SOUND AND A HUD IN A KENNEY FONT
---------------------------------------------------
    GameAssetProviderRegistry providers = Engine.Instance.Managers.AssetProviders;

    Tilesheet atlas = providers.LoadTilesheet(
        "kenney:puzzle-pack/Spritesheet/spritesheet_default");
    Sprite ball = SpriteManager.Instance.CreateSprite(layer, atlas["ballBlue", 0, 0]);

    AudioResource pickUp = providers.LoadAudio(
        "kenney:sci-fi-sounds/Audio/laserSmall_000", volume: 0.7f);

    SKTypeface face = providers.LoadFont("kenney:simulated-bundle/Fonts/Kenney Space");
    TextBlock score = new TextBlock(host, view, new Rectangle(8, 8, 240, 32), "score")
        .SetFont(face, 24f);

TWO SIZES OF ONE PICTURE
------------------------
The first materialization of a key wins, so a second variant needs a key of its
own:

    Tilesheet small = providers.LoadTilesheet(
        "kenney:puzzle-pack/Vector/puzzleAssets_vector",
        new TilesheetMaterializeOptions { VectorRasterSize = new Size(64, 64) });

    Tilesheet large = providers.LoadTilesheet(
        "kenney:puzzle-pack/Vector/puzzleAssets_vector",
        new TilesheetMaterializeOptions
        {
            VectorRasterSize = new Size(512, 512),
            RegisterAs = "ui/icon-large",
        });

    // large.Name == "ui/icon-large"; small.Name is the asset key.

ADDING A MOD FOLDER LATER
-------------------------
    // Same identifier: the bundles join the catalog the game is already using.
    Engine.Instance.UseKenneyAssets(downloadedPackPath);

    // Its own identifier: a separate catalog, separate keys, separate lifetime.
    KenneyGameAssetProvider mods = Engine.Instance.UseKenneyAssets(
        new KenneyAssetsOptions
        {
            ProviderId = "mods",
            Sources = [modFolder],
            RecursiveFolders = true,          // the folder HOLDS pack folders
        });

    Tilesheet modArt = providers.LoadTilesheet("mods:extra-tiles/PNG/tile_0001");

REGISTERING MORE THAN ONCE
--------------------------
UseKenneyAssets ADDS every path it is given, so calling it twice with the same
zip gives that pack a second slug ("planets-2") and a second set of keys. When
registration can run more than once - a loading screen that can be re-entered, a
game and its tests sharing one engine - use RegisterKenneyAssets, which skips a
path already registered and says per path what happened:

    string folder = Path.Combine(AppContext.BaseDirectory, "assets", "kenney");
    string[] zips = ["kenney_planets.zip", "kenney_sci-fi-sounds.zip"];

    KenneyAssetsRegistration registration = Engine.Instance.RegisterKenneyAssets(
        [.. zips.Select(zip => Path.Combine(folder, zip))]);

    if (registration.Packs.Count == 0)
    {
        throw new InvalidOperationException($"No Kenney pack could be read from '{folder}'.");
    }

    foreach (KenneySourceResult source in registration.Sources)
    {
        Engine.Logger.LogInformation("Kenney: {Source}", source);   // Read / AlreadyRegistered / ...
    }

    foreach (KenneySourceResult missing in registration.Unavailable)
    {
        Engine.Logger.LogWarning("Kenney: {File} is missing; its assets are too.", missing.SourcePath);
    }

    KenneyGameAssetProvider kenney = registration.Provider;

CHECKING KEYS
-------------
A game that writes its keys down as constants wants a test proving every one
still resolves - the registry compares keys case-insensitively but is otherwise
spelling-exact. CheckKeys answers from the catalog without loading anything:

    KenneyKeyCheck check = kenney.CheckKeys(MyAssetKeys.All);

    check.MissingKeys.Should().BeEmpty();
    check[MyAssetKeys.MainAtlas].Kind.Should().Be(GameAssetKind.SpriteAtlas);
    Engine.Logger.LogInformation("Kenney keys: {Summary}", check);  // one-line summary

  * One KenneyKeyStatus per key, in the order given: Found, Kind (Unknown when
    not found), SizeBytes, and AtlasFrameCount for a sprite atlas.
  * CountsByKind counts FOUND keys only; a kind with none is absent.
  * A key with another provider's prefix is not found - this provider does not
    hold it. The bare "pack/path" form is accepted, as TryDescribe accepts it.
  * A key that fails to LOAD through the registry says "Did you mean: ..." with
    the closest real keys (engine core); CheckKeys is the up-front version.

THE KEY CATALOG
---------------
Describe() gives descriptors; it does not give the frame names inside an atlas.
GetKeyCatalog (or WriteKeyCatalog to any TextWriter) writes a readable listing
of every key grouped by pack, with its kind, and under each sprite atlas the
frame names the materialized sheet answers to - for a developer choosing assets
and copying exact spellings:

    Console.Write(kenney.GetKeyCatalog());
    kenney.WriteKeyCatalog(writer, new GameAssetQuery { Pack = "puzzle-pack" });

    Asset keys of provider 'kenney': 1 pack(s), <n> key(s) matching the query.

    puzzle-pack - Puzzle Pack (1.1) - <n> key(s)
      kenney:puzzle-pack/License.txt  [Document, listed only]
      kenney:puzzle-pack/PNG/Default/ballBlue  [Image]
      kenney:puzzle-pack/Spritesheet/spritesheet_default  [SpriteAtlas, <n> frame(s)]
          ballBlue
          ballGrey
          ...

  * The frame names are the REGION names with the default options (image
    extension removed, blanks left out, the first of two equal names kept), so
    sheet["ballBlue", 0, 0] works as written. With StripFrameExtension = false
    the regions keep the extension the atlas writes.
  * The query filters KEYS (Pack, Kind, NameContains, PathPrefix); an atlas that
    is listed always shows all its frames. A pack with no listed key is left
    out.
  * Plain text for reading, not a format to parse; nothing is materialized. It
    is not a code generator - a game writes its own constants from it.

LAZINESS, CACHING AND IDEMPOTENCE
=================================
WHAT REGISTERING COSTS. The archive's file listing, plus the few small documents
that decide what an asset IS — sprite atlas, tile set and tile map XML. No
image, audio, font or model file is opened. Pointing the provider at a folder of
thousands of models costs a directory walk, which is why a catalog of a whole
Kenney collection is built in well under a second.

THE ENGINE'S REGISTRIES ARE THE CACHE. This package keeps no cache of its own:
a materialized tilesheet lives in TilesheetRegistry, an audio resource in
AudioResourceManager, a typeface in FontManager. Three consequences worth
knowing:

  * FIRST MATERIALIZATION OF A KEY WINS, on every route. A second call for a key
    the engine already holds returns that object and IGNORES this call's
    options — volume and pan on audio, the grid, raster and model-render
    options on a tilesheet. Re-registering would DISPOSE the object the game is
    using, and every Frame handed out from a tilesheet with it.
  * RegisterAs IS THE WAY TO GET A VARIANT: a second tile size, a second raster
    size, a smaller model sheet. One asset, two keys, two objects.
  * An asset a game UNLOADS from the engine's registry — AudioResourceManager's
    Unload, FontManager.Remove, TilesheetRegistry.Remove — is materialized again
    on the next call. There is no stale second cache to go out of step.

MODEL DATA IS NOT CACHED BY THE ENGINE at all, because it is registered nowhere.
What IS kept is the parsed document of an ANIMATED model, so a clip baked later
lines up with the model already handed out; a static model's document is not
kept, since it has no clip to bake.

THREAD SAFETY
=============
Everything public here is safe to call from more than one thread, which the
engine's provider contract requires (the registry's bookkeeping is locked but
provider calls happen outside that lock).

  * The catalog is swapped rather than edited in place, so adding sources while
    another thread is looking up a key is safe.
  * Each materializing route serializes its own check-then-load, so two threads
    asking for one key get ONE object rather than two and a disposed one.
  * A map import runs one at a time, because two maps sharing a tile set key
    would otherwise both build a sheet and the second registration would
    dispose the first sheet under the first map's tiles.
  * Materializing is load-time work. None of it belongs in a per-frame path.

DISPOSAL — WHO OWNS WHAT
========================
    Engine.Dispose()  ->  clears the provider registry  ->  disposes the provider

That is the whole story for a normal game: registering a provider hands it to
the engine, and the engine disposes it on Unregister, on Clear, or when it shuts
down. A game does not dispose the provider itself, and must not dispose one it
has registered.

WHAT DISPOSAL DOES, AND DOES NOT DO:
  * It closes the archives and releases the parsed model documents. Nothing can
    be read or materialized afterwards (ObjectDisposedException).
  * THE CATALOG STILL ANSWERS: Describe, TryDescribe, Packs, AssetCount and
    Warnings are pure data, so a credits or diagnostics screen still works.
  * OBJECTS ALREADY HANDED TO THE ENGINE ARE NOT WITHDRAWN. The tilesheets,
    audio resources and typefaces a game is drawing and playing with stay
    registered and keep working — they are the engine's now. A game that wants
    them gone unloads them from the registry that holds them.
  * Models and animation clips are plain data and stay valid.
  * A stream from OpenRaw belongs to the CALLER: dispose it.

MINIMUM VIABLE PROJECT
======================
This package adds ONE PackageReference, one call and the bundle files to an
ordinary CodeBrix.Platform game. Everything else below is the normal engine
layout, which the engine's own AGENT-README.txt (repository root) covers in
full. Version attributes are omitted — use the latest of each package.

MyGame.Core/MyGame.Core.csproj

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>MyGame</RootNamespace>
        <Nullable>enable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.ApacheLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.Fonts.OpenSans.ApacheLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.GameEngine.MitLicenseForever" />
        <PackageReference Include="CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever" />
      </ItemGroup>
      <ItemGroup>
        <!-- The bundles the game ships, beside the executable. -->
        <None Include="assets\*.zip" CopyToOutputDirectory="PreserveNewest" />
      </ItemGroup>
    </Project>

MyGame.Core/MyGameHost.cs

    using CodeBrix.Platform.GameEngine;
    using CodeBrix.Platform.GameEngine.Assets.Providers;
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
    using CodeBrix.Platform.GameEngine.Host.Hosting;
    using CodeBrix.Platform.GameEngine.Host.Rendering;
    using CodeBrix.Platform.GameEngine.KenneyAssets;
    using CodeBrix.Platform.GameEngine.Scenes;
    using System;
    using System.Drawing;
    using System.IO;

    namespace MyGame;

    public sealed class MyGameHost : CodeBrixGameHost
    {
        private const string TilesKey =
            "kenney:pixel-platformer/Tilemap/tilemap-backgrounds_packed";

        public MyGameHost(GameSurfaceCanvas renderSurface) : base(renderSurface) { }

        protected override void LoadAssets()
        {
            Engine.Instance.UseKenneyAssets(
                Path.Combine(AppContext.BaseDirectory, "assets",
                    "kenney_pixel-platformer.zip"));
        }

        protected override void LoadTilesheets()
        {
            // The asset key IS the tilesheet's registry key from here on.
            Tilesheet tiles = Engine.Instance.Managers.AssetProviders.LoadTilesheet(
                TilesKey,
                new TilesheetMaterializeOptions { TileSize = new Size(24, 24) });
        }

        protected override Scene CreateInitialScene()
        {
            Scene scene = new Scene();
            Tilesheet tiles = TilesheetRegistry.Instance[TilesKey];
            SceneLayer layer = scene.AddLayer(30, 17, 24, 24);
            layer[0, 16]!.CurrentFrame = tiles["grid", 0, 0];
            return scene;
        }
    }

PERFORMANCE TIPS
================
  * CATALOGING IS CHEAP, MATERIALIZING IS NOT. Register every bundle at
    start-up; materialize when a level needs an asset. Nothing is read from a
    bundle until it is asked for.
  * PRE-RENDERING A MODEL SHEET IS THE EXPENSIVE CALL in this package, and its
    cost is cells x cell area x Supersample squared. Supersample = 1 roughly
    quarters it, at the price of jagged silhouettes; fewer Directions, a smaller
    FrameSize and a lower AnimationFramesPerSecond all cut it proportionally.
    Render the sheets a level needs while a loading screen is up.
  * A TILESHEET REGION BUILDS ITS TILE CACHE EAGERLY — one bitmap and one image
    per cell. A many-direction sheet at a large FrameSize therefore holds a good
    deal of memory beyond the sheet bitmap itself. Several smaller sheets,
    materialized when needed, beat one enormous one.
  * BAKE ONLY THE CLIPS YOU PLAY. Baking is opt-in for exactly this reason;
    LoadModelAnimation adds one later without re-reading the model.
  * ASK FOR A VECTOR AT THE SIZE YOU WILL DRAW IT. It is rasterized once, at
    load time; scaling the result at draw time is what costs.
  * DESCRIPTORS ARE FREE TO KEEP. The descriptor for an asset is the same
    instance every time, so caching keys or descriptors in level data costs
    nothing and Describe over a large catalog is a filter over data already in
    memory.
  * DO NOT MATERIALIZE IN A PER-FRAME PATH. Every route takes a lock and hits
    the engine's registries; the second call is cheap, but it is not free, and
    the first will decode a file.
  * ONE PROVIDER PER SET OF ASSETS, not one per bundle. A provider holds every
    bundle a game registers with it, and its keys stay distinct because each
    pack has its own slug.

COMMON PITFALLS TO AVOID
========================
  * AN ATLAS FRAME IS NOT sheet[0, 0]. On a sprite-atlas sheet the `default`
    region deliberately holds no tiles; address frames by name,
    sheet["ballBlue", 0, 0]. On a LOOSE IMAGE, sheet[0, 0] IS the picture. The
    two look alike and behave differently.
  * THE `default` REGION OF A TILED TILE SET SHEET AND OF A MODEL SHEET IS
    MEANINGLESS. Use "tiles" (and its flip variants) for a map, and the
    animation's own name — or "rest" — for a model.
  * A MATERIALIZABLE KEY HAS NO EXTENSION; A LISTED-ONLY KEY DOES. Asking for
    "…/ballBlue.png" fails with KeyNotFoundException, and asking for
    "…/character-a" when you meant the .fbx silently gives you the glTF model.
    Let Describe tell you the key.
  * OPTIONS ON THE SECOND CALL ARE IGNORED. Volume, pan, tile size, raster size
    and model-render options apply only to the call that first materializes a
    key. Use RegisterAs for a variant; do not assume a re-load applies new
    options.
  * PASS descriptor.Key, NEVER A HAND-CASED KEY. Key lookup is
    case-insensitive; the tilesheet registry is ordinal, so two spellings would
    register two sheets for one asset.
  * IMPORTING A MAP TWICE INTO ONE SCENE ADDS ITS LAYERS TWICE. Sheets are
    reused; layers are not. Use a fresh scene.
  * NEVER DRAW TEXT WITH A KENNEY INPUT-PROMPT FONT. Those faces are icon fonts
    with no basic Latin at all, so the text comes out blank with no error
    anywhere. Use Kenney Space or Kenney Future Narrow for words.
  * NEVER BRANCH ON SKTypeface.FamilyName. It is platform-specific (Windows
    reports Kenney Future Narrow as "Kenney Future"); code that works on one
    OS breaks on another. Use the key or FontManager.GetFamilyName(key).
  * A PACK SLUG COMES FROM THE LICENCE TITLE, NOT THE FILE NAME. The bundle
    kenney_puzzle-pack-1.zip registers as puzzle-pack, because its licence says
    "Puzzle Pack (1.1)". Read the slug from Packs or from a descriptor rather
    than guessing it from the download's file name.
  * TWO BUNDLES OF ONE PACK GET A -2 SUFFIX. Registering the same pack twice —
    a zip and a folder extracted from it, say — gives the second one slug-2 and
    a second set of keys. Register one of them. The SAME PATH registered twice
    through UseKenneyAssets does this too; RegisterKenneyAssets skips it.
  * AN UNREADABLE BUNDLE IS SILENT BY DEFAULT. It is in Warnings and the log,
    not in an exception. Read Warnings after registering, or set
    IgnoreUnreadableSources = false while developing.
  * MODEL DATA IS REGISTERED NOWHERE. LoadModel hands back an object the caller
    owns; calling it twice reads the cached document but builds a new model. Hold
    the one you asked for.
  * A SHEET CAN EXCEED THE 8192 PIXEL LIMIT SOONER THAN IT LOOKS. Eight
    directions of a two-second clip at 24 frames per second and 128 pixel cells
    is already 6144 x 1024. The exception message says which knob to turn.
  * AN SVG THAT DECLARES ONLY A viewBox — no width and no height — can rasterize
    to a fully transparent bitmap, because the vector library gives such a
    document a picture with no drawn content. That is a property of the document,
    not a failure of the load; add width and height to the art, or check the
    result before shipping it.
  * AN OBJECT LAYER IS DATA. Nothing is spawned, nothing is positioned, and the
    group's Offset is not folded into an object's Bounds. The game reads the
    data and builds its own objects.
  * A MAP THAT WILL NOT IMPORT SAYS SO BEFORE YOU TRY. The reason is in
    Properties["tiledMapError"] from cataloging time; three of the twenty maps
    in Kenney's own collection are isometric and simply cannot be imported by
    this version.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * IT SHIPS NO ASSETS. No art, no audio, no fonts, no models: a game brings
    the bundles.
  * IT DOES NOT WRITE. Bundles are opened read-only; nothing is extracted to
    disk, nothing is cached on disk, and no file beside a bundle is created.
  * NO ISOMETRIC, HEXAGONAL OR STAGGERED TILED MAPS, and no infinite maps. The
    engine has the coordinate systems; mapping Tiled's staggered geometry onto
    them is not in this version. The refusal names the orientation.
  * NO IMAGE-COLLECTION TILE SETS (one image per tile). Such a map is refused;
    the individual pictures are addressable as ordinary images instead, which is
    the useful route for that art.
  * NO PER-TILE COLLISION SHAPES. A tile's collision box is a rectangle, so the
    <objectgroup> inside a <tile> is reported in Warnings, not honoured.
  * NO PER-LAYER OPACITY. A partly transparent Tiled layer is imported fully
    opaque with a warning; a zero-opacity layer is imported hidden.
  * IT SPAWNS NOTHING FROM AN OBJECT LAYER, and it never creates sprites: it
    hands back layers, sheets and data.
  * NO 3D RENDERING IN THE ENGINE. This package reads model DATA and can
    PRE-RENDER a model into sprite frames with its own software rasterizer;
    there is no runtime 3D pipeline, no camera, no scene graph for models.
  * ONLY glTF MODELS ARE READ. .fbx, .obj, .mtl, .dae and .stl are listed and
    refused. Skeletal and rigid-part animation both work, baked into vertex
    frames; morph targets, cameras, lights and material extensions beyond the
    base colour set are not carried.
  * NO NESTED ARCHIVE EXPANSION. A zip inside a bundle — Kenney's
    "Webfonts *.zip" — is listed as an Archive and never opened.
  * NO ASSET CONVERSION OR AUTHORING. It does not write .gts tilesheet
    definitions, does not repack anything into an engine AssetsFile, and does
    not resize or recolour art.
  * IT IS NOT A DOWNLOADER. It never reaches the network; it reads files the
    game already has.
  * IT DOES NOT REGISTER ITSELF. No module initializer, nothing on start-up
    until a game calls UseKenneyAssets.

LICENSING OF KENNEY CONTENT
===========================
Kenney's asset bundles are released under the CREATIVE COMMONS ZERO (CC0 1.0
UNIVERSAL) public domain dedication:

    http://creativecommons.org/publicdomain/zero/1.0/

CC0 content may be used in personal, educational and commercial projects and
requires NO ATTRIBUTION. Crediting the source is nonetheless good practice and
is the house style of this repository, and the provider makes it easy: every
pack's licence title line is in KenneyPackSummary.LicenseTitle and in each
descriptor's Properties["license"], so a credits screen can be generated from
what is actually loaded.

    credits.AddRange(kenney.CreditLines);   // "<licence title> - Kenney (CC0)" per pack

Each bundle also carries Kenney's own License.txt, which is listed as an asset
and can be shown verbatim through OpenRaw. A few Kenney packs are sold rather
than given away; those are CC0 too, but the download itself is the purchaser's.
This package neither checks nor enforces any of that — it reads what it is
pointed at.

The MIT licence of this package covers its code only, and
THIRD-PARTY-NOTICES.txt (shipped inside the package) records what it
incorporates.

WORKING EXAMPLES ON GITHUB
==========================
Repository root: https://github.com/ellisnet/CodeBrix.Platform.GameEngine

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/KenneyAssetsDemo
      KenneyAssetsDemo — the worked example of this package, end to end: real
      CC0 Kenney bundles shipped beside the executable and registered with one
      UseKenneyAssets call, a Tiled map imported into scene layers, a
      PRE-RENDERED 3D character driven by the keyboard that faces its direction
      of travel and animates through the engine's Cycle / FrameSequence, atlas
      sprites as collectibles, a pick-up sound, HUD text in a Kenney text font,
      one rasterized SVG icon and an on-screen note listing the registered
      packs. It logs one line per loading step, which is the shape a game's own
      asset loading wants. Its README covers the controls and how to build each
      head.

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests
      EngineKenneyAssetsExtensionsTests.cs — the whole way round, the way game
          code does it: registering bundles and then loading one asset of every
          kind through the engine's own provider registry, registering twice,
          giving a second set of assets its own identifier, and what the
          registry refuses.
      KenneyGameAssetProviderTests.cs — the provider's own surface: Describe
          and its query, the descriptor cache, Packs and their counts, Warnings,
          OpenRaw on a non-materializable asset, disposal semantics,
          AddNewSources and its per-source report, CreditLines, CheckKeys and
          the key catalog.
      KenneyAssetsOptionsTests.cs — the options record's defaults.
      KenneyPackSummaryTests.cs / KenneySourceResultTests.cs /
          KenneyKeyCheckTests.cs — the credit line, the per-source report and
          the key check's statuses, counts and summary line.
      KenneyZipArchiveTests.cs / KenneyFolderArchiveTests.cs — a zip bundle and
          an extracted folder answering identically, including strict
          dependency resolution and a damaged zip.
      KenneyAssetSourceTests.cs / KenneyPackIndexTests.cs — pack detection, the
          collection-folder shape, slug collisions and THE KEY SCHEME, which is
          fenced case by case here.
      SpriteAtlasParserTests.cs / TiledMapParserTests.cs / TiledGidTests.cs —
          the document parsers, including every feature that is refused and the
          flip bits.
      TilesheetMaterializerTests.cs / SvgRasterizerTests.cs — the image, atlas
          and vector routes: regions, frame naming, the raster cap, the
          cull-origin translate, and the warnings that are not failures.
      AudioMaterializerTests.cs / FontMaterializerTests.cs — registration,
          idempotence across materializer instances, decode failures, and the
          icon-font fact recorded as a test.
      TiledMapImporterTests.cs — the map import: cell-to-frame mapping, baked
          flip variants, spacing and margin, mixed tile sizes and overhang,
          collision by property and by selector, object-layer data, and every
          refusal message.
      GltfModelReaderTests.cs / ModelSpriteRendererTests.cs /
          ModelSheetLayoutTests.cs / ModelTilesheetMaterializerTests.cs — the
          model routes: reading, on-demand clip baking and the alignment
          guarantee, the software rasterizer's coverage and symmetry, and the
          sheet layout contract.
      KenneyAllInOneCorpusScan.cs — an OPT-IN scan over a whole Kenney
          collection, skipped unless the environment variable KENNEY_ALLIN1_DIR
          points at one. It is the check that the catalog survives real-world
          content at scale.

QUICK REFERENCE CARD
====================
INSTALL
    dotnet add package CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever

REGISTER (once, after the engine is initialized; call again to ADD sources)
    using CodeBrix.Platform.GameEngine.KenneyAssets;
    KenneyGameAssetProvider kenney = Engine.Instance.UseKenneyAssets(
        "assets/kenney_pixel-platformer.zip", "assets/kenney_sci-fi-sounds.zip");
    // options form: new KenneyAssetsOptions { Sources = [...], ProviderId = "mods",
    //     RecursiveFolders = true, IgnoreUnreadableSources = false }
    KenneyAssetsRegistration registration = Engine.Instance.RegisterKenneyAssets(...);
    // same paths or options; skips a path already registered and reports each one:
    // Provider, Sources (KenneySourceResult: SourcePath, Status Read /
    // AlreadyRegistered / Missing / Unreadable, Packs, Message), Packs, Warnings,
    // Unavailable

KEYS   <providerId>:<pack-slug>/<path>   case-insensitive; extension DROPPED for
       a materializable asset, KEPT for a listed-only one; the key is also the
       engine registry key of the result

LOAD (Engine.Instance.Managers.AssetProviders)
    Tilesheet LoadTilesheet(key, TilesheetMaterializeOptions?)
    AudioResource LoadAudio(key, volume, pan)
    SKTypeface LoadFont(key)
    TiledMapImport ImportTiledMap(key, Scene, TiledMapImportOptions?)
    GameModel LoadModel(key, ModelMaterializeOptions?)
    GameModelAnimationClip LoadModelAnimation(key, animationName, framesPerSecond)
    Describe(GameAssetQuery?) / TryDescribe(key, out descriptor)

ADDRESSING THE RESULT
    loose image / vector      sheet[0, 0]
    sprite atlas             sheet["ballBlue", 0, 0]      (default region: empty)
    Tiled tile set           sheet["tiles", column, row]  + "tiles-fh" / "-fv" /
                             "-fd" / "-fhv" …             (default region: empty)
    model sprites            sheet["walk", frame, direction] and
                             sheet[ModelRenderOptions.RestPoseRegionName, 0, direction]
    tile set sheet key       "<map key>#<tile set name>"

PROVIDER  (KenneyGameAssetProvider)
    const DefaultProviderId = "kenney"
    IReadOnlyList<KenneyPackSummary> Packs      int AssetCount
    IReadOnlyList<string> Warnings              IReadOnlySet<GameAssetKind> SupportedKinds
    AddSources(...)    AddNewSources(...) -> KenneyAssetsRegistration
    Describe / TryDescribe / OpenRaw
    IReadOnlyList<string> CreditLines           one line per registered pack
    KenneyKeyCheck CheckKeys(keys)              Keys, MissingKeys, AllFound,
                                                CountsByKind, TotalSizeBytes, [key]
    GetKeyCatalog(GameAssetQuery?) / WriteKeyCatalog(TextWriter, GameAssetQuery?)
    Materialize Tilesheet / Audio / Font / TiledMap / Model / ModelAnimation
    Dispose()   // the ENGINE calls this; the catalog still answers afterwards

PACK  (KenneyPackSummary)
    Slug  DisplayName  Version  LicenseTitle  SourcePath
    AssetCount  MaterializableAssetCount  CountsByKind
    CreditLine   "<licence title> - Kenney (CC0)"

DESCRIPTOR PROPERTIES  (KenneyAssetProperties — use the constants)
    packName  packVersion  license  extension  materializable
    atlasFrameCount  atlasImagePath
    mapWidth  mapHeight  tileWidth  tileHeight  tileLayerCount  tilesetCount
    tiledMapError

EXCEPTIONS
    KeyNotFoundException          no provider or no asset holds that key
    UnsupportedGameAssetException listed for discovery only, or wrong route
    TiledMapParseException        the map document cannot be read
    FileNotFoundException         a dependency is missing inside the pack
    InvalidDataException          the bytes will not decode
    NotSupportedException         no audio reader for that format
    ObjectDisposedException       after the provider was disposed

TOP FIVE MISTAKES
    1. sheet[0, 0] on an atlas or a tile set sheet (address regions by name).
    2. Expecting options on the SECOND materialization of a key to apply.
    3. Guessing a key: extensions, pack slugs and casing all come from Describe.
    4. Importing one map twice into one scene (layers are added, not replaced).
    5. Drawing text with a Kenney input-prompt font (icon font, no letters).

================================================================================
END OF AGENT-README
================================================================================
