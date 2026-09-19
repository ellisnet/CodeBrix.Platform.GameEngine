using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.KenneyAssets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;
using Windows.System;

namespace KenneyAssetsDemo.Game;

/// <summary>
/// Game host for the KenneyAssetsDemo sample: everything on screen comes out of two Kenney asset
/// bundles, through <c>UseKenneyAssets</c> and the engine's own
/// <c>Engine.Managers.AssetProviders</c> registry.
/// </summary>
/// <remarks>
/// <para>
/// The demo is a walking-about scene, not a game: a Tiled map imported into scene layers, a character
/// pre-rendered from an animated glTF model who walks with the arrow keys or WASD and faces the way he
/// is going, five collectibles cut out of a Kenney sprite atlas, a sound when one is picked up, a
/// heads-up display in a Kenney font and a rasterized SVG badge in the corner.
/// </para>
/// <para>
/// It follows the fixed order <see cref="GameHostBase.Initialize"/> runs its hooks in, and every hook
/// does one kind of asset work, so the file reads as "here is where each kind of Kenney asset comes
/// in": <see cref="LoadAssets"/> registers the bundles and takes the audio and the fonts,
/// <see cref="LoadTilesheets"/> takes the pictures (the pre-rendered model, the atlas, the vector),
/// <see cref="LoadAnimationCycles"/> turns the model sheet's rows into engine animation cycles,
/// <see cref="CreateInitialScene"/> imports the map, and the two hooks after it fill the scene.
/// </para>
/// <para>
/// Every step reports what it got on the console with a fixed prefix - see <see cref="DemoLog"/> - so a
/// run can be checked without looking at it.
/// </para>
/// </remarks>
public sealed class KenneyAssetsDemoGameHost : CodeBrixGameHost
{
    /// <summary>The fixed engine render width, in pixels, this demo is laid out for.</summary>
    /// <remarks>
    /// The imported map is 26 x 15 cells of 18 pixels, so 468 x 270 pixels of world. The demo renders
    /// at twice that and shows the world at <see cref="WorldZoom"/>, which gives the 18-pixel tile art
    /// a clean doubling while the heads-up display, which is drawn in screen space and so is not
    /// zoomed, still gets the full resolution to draw text into.
    /// </remarks>
    public const int RenderWidth = 936;

    /// <summary>The fixed engine render height, in pixels, this demo is laid out for.</summary>
    public const int RenderHeight = 540;

    /// <summary>The zoom the world is drawn at. See <see cref="RenderWidth"/>.</summary>
    private const float WorldZoom = 2.0f;

    //The imported map layers take z-orders from here upward, one per layer, and the demo's own layer
    //  sits above all of them with room to spare
    private const int MapZOrderBase = 0;
    private const int ActorLayerZOrder = 100;

    //The pre-rendered character: cell size in world pixels, how many directions the sheet holds, and
    //  how fast its animations play. 32 pixels reads well against 18-pixel tiles, and eight directions
    //  is what an eight-way walk needs
    private const int CharacterFramePx = 32;
    private const int CharacterDirections = 8;
    private const int CharacterAnimationFps = 12;
    private const float WalkCellsPerSecond = 4.5f;

    //One axis of a diagonal, so a diagonal walk is not faster than a straight one
    private const float DiagonalStep = 0.70710678f;

    //The atlas frames these come from are 48 pixels square and the engine scales a sprite with nearest
    //  sampling, so halving is the size that stays crisp
    private const int CollectiblePx = 24;

    private const int BadgeWidthPx = 180;
    private const int BadgeHeightPx = 108;

    //Animation cycles live in a process-global registry keyed by string, so the demo's keys carry its
    //  own name
    private const string CycleKeyPrefix = "kenney-assets-demo";

    //Cells of the imported map that no tile layer fills, spread over it, for the collectibles to sit in
    private static readonly (int Column, int Row)[] CollectibleCells =
        [(9, 1), (7, 4), (21, 5), (11, 8), (8, 9)];

    private static readonly Vector2 StartCell = new(10.0f, 10.0f);

    private readonly HashSet<VirtualKey> _keysDown = [];
    private readonly HashSet<string> _cycleKeys = [];
    private readonly List<Collectible> _collectibles = [];

    private KenneyGameAssetProvider? _provider;

    private Tilesheet _characterSheet = null!;
    private Tilesheet _collectibleAtlas = null!;
    private Tilesheet? _vectorBadge;

    private AudioResource? _pickUpSound;
    private SKTypeface? _hudFont;
    private SKTypeface? _titleFont;
    private SKTypeface? _iconFont;

    private SceneLayer _actorLayer = null!;
    private Sprite? _character;
    private TextBlock? _statusText;

    private Size _mapSizePx;
    private Size _mapTileSizePx;

    private int _facing;
    private string _currentCycleKey = string.Empty;
    private int _collected;
    private string _lastStatusText = string.Empty;
    private bool _loggedFirstKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyAssetsDemoGameHost"/> class.
    /// </summary>
    /// <param name="renderSurface">The render surface to draw into.</param>
    public KenneyAssetsDemoGameHost(GameSurfaceCanvas renderSurface)
        : base(renderSurface)
    {
    }

    /// <summary>Gets the number of collectibles picked up so far.</summary>
    public int Collected => _collected;

    /// <summary>Gets the number of collectibles the demo placed.</summary>
    public int CollectibleCount => _collectibles.Count;

    #region CodeBrixGameHost overrides

    /// <inheritdoc />
    /// <remarks>
    /// Registers the two bundles the sample ships, then takes the sound and the fonts out of them.
    /// Without the bundles there is no demo, so that step is fatal; a missing sound or font is not.
    /// </remarks>
    protected override void LoadAssets()
    {
        DemoLog.Step("registering the Kenney bundles", RegisterKenneyBundles);

        DemoLog.TryStep("loading the pick-up sound", LoadPickUpSound);
        DemoLog.TryStep($"loading the font '{DemoAssetKeys.HudFont}'",
            () => _hudFont = LoadFont(DemoAssetKeys.HudFont));
        DemoLog.TryStep($"loading the font '{DemoAssetKeys.TitleFont}'",
            () => _titleFont = LoadFont(DemoAssetKeys.TitleFont));
        DemoLog.TryStep($"loading the font '{DemoAssetKeys.IconFont}'",
            () => _iconFont = LoadFont(DemoAssetKeys.IconFont));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Everything that becomes a picture. A tilesheet is what the engine draws from, and the provider
    /// hands one back for three quite different kinds of asset: a 3D model pre-rendered into sprite
    /// frames, a sprite atlas cut into named regions, and an SVG document rasterized into a bitmap.
    /// </remarks>
    protected override void LoadTilesheets()
    {
        DemoLog.Step("pre-rendering the character model", LoadCharacterSheet);
        DemoLog.Step("loading the collectible sprite atlas", LoadCollectibleAtlas);
        DemoLog.TryStep("rasterizing the vector badge", LoadVectorBadge);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One engine animation cycle per (animation, direction) pair of the model sheet. The sheet's
    /// layout makes this mechanical: a region per animation, its COLUMNS are the animation's frames and
    /// its ROWS are the camera directions, so row <c>d</c> of region <c>walk</c> is the whole walk
    /// cycle seen from direction <c>d</c>.
    /// </remarks>
    protected override void LoadAnimationCycles()
    {
        foreach (string animation in DemoAssetKeys.CharacterAnimations)
        {
            TilesheetRegion? region = _characterSheet.GetRegion(animation);

            if (region is null)
            {
                //The provider renders only the animations the model actually offers
                DemoLog.Write($"WARNING: animation cycles: the character sheet has no '{animation}' region.");
                continue;
            }

            for (int direction = 0; direction < region.Rows; direction++)
            {
                FrameSequence sequence = new(new List<Frame>());

                for (int frame = 0; frame < region.Columns; frame++)
                {
                    //sheet[regionName, column, row] - the frame of this animation, from this direction
                    sequence.AddFrame(_characterSheet, animation, frame, direction);
                }

                sequence.SequenceCycleType = CycleType.Repeating;

                //Constructing a Cycle registers it under its key; the animator fetches a clone by key
                string key = CycleKey(animation, direction);
                _ = new Cycle(sequence, 1.0 / CharacterAnimationFps, key);
                _cycleKeys.Add(key);
            }

            DemoLog.Write(
                $"animation cycles: '{animation}' - {region.Rows} direction(s) of " +
                $"{region.Columns} frame(s) at {CharacterAnimationFps} fps.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The map import ADDS one scene layer per tile layer of the map to the scene it is given, so the
    /// scene is created here first and handed over. A layer of the demo's own goes on top of them for
    /// the character and the collectibles, which is the whole trick to mixing imported content with
    /// content a game builds itself.
    /// </remarks>
    protected override Scene CreateInitialScene()
    {
        Scene scene = new();

        DemoLog.Step("importing the Tiled map", () => ImportMap(scene));

        int columns = Math.Max(1, _mapSizePx.Width / Math.Max(1, _mapTileSizePx.Width));
        int rows = Math.Max(1, _mapSizePx.Height / Math.Max(1, _mapTileSizePx.Height));

        _actorLayer = scene.AddLayer(
            columnCount: columns,
            rowCount: rows,
            width: _mapTileSizePx.Width,
            height: _mapTileSizePx.Height,
            zOrder: ActorLayerZOrder,
            parallax: 1.0f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        _actorLayer.ShowGridLines = false;

        return scene;
    }

    /// <inheritdoc />
    protected override void OnSceneBound()
    {
        var backbuffer = RenderSurface.Host.Backbuffer;
        backbuffer.ClearColor = new SKColor(24, 28, 38);

        //Kenney's tile art is pixel art: never smooth a tile as it is scaled up. The matching
        //  presentation setting is applied in OnEngineInitialized, because it lives on
        //  Engine.Configuration, which the engine replaces when it loads its configuration file -
        //  after this hook has run.
        if (backbuffer is BitmapBackbuffer bitmapBackbuffer)
        {
            bitmapBackbuffer.FilterQuality = ImageFilterQuality.None;
        }

        if (RenderSurface.Host.ViewManager.Views.Count == 0) { return; }

        View view = RenderSurface.Host.ViewManager.Views[0];

        //The world is exactly half the render resolution across, so showing it at 2x fills the surface
        //  with the map and leaves the screen-space heads-up display at full resolution.
        view.Viewport.SnapZoom(WorldZoom);
        view.Camera.SnapTo(PointF.Empty);
    }

    /// <inheritdoc />
    protected override void CreateSprites()
    {
        CreateCharacter();
        CreateCollectibles();
    }

    /// <inheritdoc />
    protected override void CreateDirectDrawings()
    {
        if (RenderSurface.Host.ViewManager.Views.Count == 0) { return; }

        View view = RenderSurface.Host.ViewManager.Views[0];

        CreateHudPanel(view);
        CreatePackNote(view);
        CreateVectorBadge(view);

        UpdateStatusText();
    }

    /// <inheritdoc />
    protected override void OnKeyboardAdapterInitialized()
    {
        var keyboard = Engine.Input.KeyboardEventPoller;

        if (keyboard is null) { return; }

        keyboard.KeyDown += OnKeyDown;

        foreach (VirtualKey key in MonitoredKeys)
        {
            keyboard.StartMonitoringKey((int)key, key.ToString());
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is the first hook that runs AFTER <c>Engine.Initialize</c>, and the engine replaces
    /// <c>Engine.Configuration</c> with what it loaded there - so configuration a game sets in an
    /// earlier hook is thrown away. Anything on <c>Engine.Configuration</c> belongs here.
    /// </remarks>
    protected override void OnEngineInitialized()
    {
        Engine.Configuration.TargetFPS = 60;

        //Never smooth the finished frame when the surface presents it larger than the resolution it was
        //  rendered at. Tile filtering (see OnSceneBound) and presentation filtering are separate.
        Engine.Configuration.RenderScalingFilter = RenderScalingFilter.NearestNeighbor;

        Engine.BeforeBackgroundTasksExecute += BeforeCycleWork;
        Engine.AfterBackgroundTasksExecute += AfterCycleWork;
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        DemoLog.Write(
            "controls: arrow keys or WASD walk the character; nothing else is needed - walk into a " +
            "collectible to pick it up.");
        DemoLog.Ready();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The asset provider is NOT unregistered here: the engine's provider registry disposes what it
    /// holds when the engine shuts down, which closes the bundle archives. What the provider already
    /// materialized - the tilesheets, the sound, the typefaces - belongs to the engine's own registries
    /// and goes with them.
    /// </remarks>
    protected override void UnhookEvents()
    {
        if (Engine.Input.KeyboardEventPoller is not null)
        {
            Engine.Input.KeyboardEventPoller.KeyDown -= OnKeyDown;
        }

        Engine.BeforeBackgroundTasksExecute -= BeforeCycleWork;
        Engine.AfterBackgroundTasksExecute -= AfterCycleWork;
    }

    #endregion CodeBrixGameHost overrides

    #region loading the Kenney assets

    private static VirtualKey[] MonitoredKeys =>
    [
        VirtualKey.Left,
        VirtualKey.Right,
        VirtualKey.Up,
        VirtualKey.Down,
        VirtualKey.A,
        VirtualKey.D,
        VirtualKey.W,
        VirtualKey.S,
    ];

    //The bundles ship in the application's own assets folder, exactly as they are downloaded from
    //  kenney.nl - the provider reads a bundle as it stands, so nothing has to be unpacked or
    //  converted at build time
    private static string BundlePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "kenney", fileName);

    private static string CycleKey(string animation, int direction) =>
        $"{CycleKeyPrefix}/{animation}/{direction}";

    /// <summary>
    /// Registers the bundles with the engine. This ONE call is the whole of the demo's asset setup: it
    /// reads each bundle's file listing and the small documents that decide what an asset is, and puts
    /// a provider in <c>Engine.Managers.AssetProviders</c> that answers for every key in them.
    /// </summary>
    private void RegisterKenneyBundles()
    {
        _provider = Engine.UseKenneyAssets(
            BundlePath(DemoAssetKeys.SimulatedBundleFileName),
            BundlePath(DemoAssetKeys.PuzzlePackFileName));

        if (_provider.Packs.Count == 0)
        {
            throw new InvalidOperationException(
                "No Kenney pack could be read. The bundles are copied to the 'assets/kenney' folder " +
                "beside the executable; check that they are there.");
        }

        string packs = string.Join(
            ", ",
            _provider.Packs.Select(pack =>
                $"{pack.Slug} ({pack.DisplayName}) {pack.AssetCount} asset(s), " +
                $"{pack.MaterializableAssetCount} materializable"));

        DemoLog.Write($"packs registered as '{_provider.ProviderId}': {_provider.Packs.Count} - {packs}");

        //Describe with no query lists everything the provider holds; a GameAssetQuery narrows it by
        //  pack, kind, name or path. This is what an asset browser or a mod loader would walk.
        IReadOnlyList<GameAssetDescriptor> described = _provider.Describe();

        string byKind = string.Join(
            ", ",
            described
                .GroupBy(descriptor => descriptor.Kind)
                .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                .Select(group => $"{group.Key} {group.Count()}"));

        DemoLog.Write($"assets described: {described.Count} in total - {byKind}");

        foreach (string warning in _provider.Warnings)
        {
            DemoLog.Write($"WARNING: bundle catalog: {warning}");
        }
    }

    private void LoadPickUpSound()
    {
        //volume and pan are applied by the FIRST materialization of a key; asking for the same key
        //  again returns the resource the engine already holds and ignores them
        _pickUpSound = Engine.Managers.AssetProviders.LoadAudio(DemoAssetKeys.PickUpSound, volume: 0.6f);

        DemoLog.Write(
            $"sound loaded: {DemoAssetKeys.PickUpSound} - {_pickUpSound.SourceExtension}, " +
            $"volume {_pickUpSound.Volume:0.00}.");
    }

    private SKTypeface LoadFont(string key)
    {
        SKTypeface typeface = Engine.Managers.AssetProviders.LoadFont(key);

        DemoLog.Write(
            $"font loaded: {key} - family '{FontManager.Instance.GetFamilyName(key)}', {typeface.GlyphCount} glyph(s).");

        return typeface;
    }

    /// <summary>
    /// Pre-renders the animated glTF character into a sprite sheet the 2D engine can draw.
    /// </summary>
    /// <remarks>
    /// This is the same <c>LoadTilesheet</c> call an image or an atlas goes through; what makes it a
    /// model render is <see cref="TilesheetMaterializeOptions.ModelRender"/>. The output layout is
    /// fixed: one uniform-grid region per animation, plus <c>rest</c> for the model's rest pose, with
    /// COLUMNS = frames and ROWS = directions, so a frame is <c>sheet["walk", frame, direction]</c>.
    /// The sheet also carries the <c>default</c> region every registry-loaded sheet gets, which means
    /// nothing here.
    /// </remarks>
    private void LoadCharacterSheet()
    {
        TilesheetMaterializeOptions options = new()
        {
            ModelRender = new ModelRenderOptions
            {
                FrameSize = new Size(CharacterFramePx, CharacterFramePx),
                Directions = CharacterDirections,
                AnimationNames = DemoAssetKeys.CharacterAnimations,
                AnimationFramesPerSecond = CharacterAnimationFps,
            },
        };

        //Rendering a sheet is a load-time cost worth reporting: it is software rasterizing, one cell
        //  at a time, and it grows with cells x cell area x supersampling
        long started = Stopwatch.GetTimestamp();
        _characterSheet = Engine.Managers.AssetProviders.LoadTilesheet(DemoAssetKeys.Character, options);
        double renderMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        string regions = string.Join(
            ", ",
            _characterSheet.Regions.Select(region =>
                $"{region.Name} {region.Columns}x{region.Rows}"));

        DemoLog.Write(
            $"character sheet: {DemoAssetKeys.Character} - regions (frames x directions): {regions}; " +
            $"sheet {_characterSheet.SkBitmap.Width}x{_characterSheet.SkBitmap.Height} px of " +
            $"{CharacterFramePx}x{CharacterFramePx} cells; rendered in {renderMilliseconds:F0} ms.");
    }

    /// <summary>
    /// Loads the sprite atlas the collectibles come out of.
    /// </summary>
    /// <remarks>
    /// An atlas is ONE tilesheet holding the sheet image, with one 1x1 region per frame named after the
    /// frame, the image extension the atlas document spells it with stripped off - so
    /// <c>ballBlue.png</c> in the document is <c>sheet["ballBlue", 0, 0]</c> here. The sheet's
    /// <c>default</c> region deliberately holds no tiles: slicing a second full copy of a big sheet
    /// bitmap would cost megabytes nobody asked for, and the frames are what a caller wants.
    /// </remarks>
    private void LoadCollectibleAtlas()
    {
        _collectibleAtlas = Engine.Managers.AssetProviders.LoadTilesheet(DemoAssetKeys.CollectibleAtlas);

        int found = DemoAssetKeys.CollectibleFrames.Count(
            frame => _collectibleAtlas.GetRegion(frame) is not null);

        DemoLog.Write(
            $"sprite atlas: {DemoAssetKeys.CollectibleAtlas} - " +
            $"{_collectibleAtlas.Regions.Count} region(s) over a " +
            $"{_collectibleAtlas.SkBitmap.Width}x{_collectibleAtlas.SkBitmap.Height} px sheet; " +
            $"{found} of {DemoAssetKeys.CollectibleFrames.Length} collectible frame(s) found.");
    }

    /// <summary>
    /// Rasterizes an SVG document into a bitmap and registers it as a tilesheet.
    /// </summary>
    /// <remarks>
    /// The vector route answers with a tilesheet whose <c>default</c> region is the whole picture, like
    /// a loose image, so <c>sheet[0, 0]</c> is it. This particular Kenney document is a whole sheet of
    /// puzzle art rather than one glyph, which is why the demo rasterizes it small and shows it as a
    /// badge; a single-icon document would be asked for at its intrinsic size instead.
    /// </remarks>
    private void LoadVectorBadge()
    {
        _vectorBadge = Engine.Managers.AssetProviders.LoadTilesheet(
            DemoAssetKeys.VectorBadge,
            new TilesheetMaterializeOptions
            {
                VectorRasterSize = new Size(BadgeWidthPx, BadgeHeightPx),
            });

        DemoLog.Write(
            $"vector rasterized: {DemoAssetKeys.VectorBadge} - " +
            $"{_vectorBadge.SkBitmap.Width}x{_vectorBadge.SkBitmap.Height} px.");
    }

    /// <summary>
    /// Imports the Tiled map into the scene.
    /// </summary>
    /// <param name="scene">The scene that receives one layer per tile layer of the map.</param>
    /// <remarks>
    /// The importer resolves the map's tile set documents and their images inside the bundle, registers
    /// one tilesheet per tile set it references under <c>&lt;map key&gt;#&lt;tile set name&gt;</c>,
    /// bakes a region for every flip combination the map uses, and assigns a frame to every cell. What
    /// it cannot represent it reports in <see cref="TiledMapImport.Warnings"/> rather than by failing,
    /// which is why the warnings are worth printing.
    /// </remarks>
    private void ImportMap(Scene scene)
    {
        TiledMapImport import = Engine.Managers.AssetProviders.ImportTiledMap(
            DemoAssetKeys.Map,
            scene,
            new TiledMapImportOptions { ZOrderBase = MapZOrderBase });

        _mapSizePx = import.MapSizePx;
        _mapTileSizePx = import.TileSize;

        DemoLog.Write(
            $"map imported: {DemoAssetKeys.Map} - {import.Layers.Count} scene layer(s), " +
            $"{import.Tilesheets.Count} tilesheet(s), {import.ObjectGroups.Count} object group(s), " +
            $"{import.MapSizePx.Width}x{import.MapSizePx.Height} px of " +
            $"{import.TileSize.Width}x{import.TileSize.Height} tiles, " +
            $"{import.Warnings.Count} warning(s).");

        foreach (Tilesheet sheet in import.Tilesheets)
        {
            DemoLog.Write(
                $"map tilesheet: {sheet.Name} - regions: " +
                $"{string.Join(", ", sheet.Regions.Select(region => region.Name))}.");
        }

        foreach (string warning in import.Warnings)
        {
            DemoLog.Write($"WARNING: map import: {warning}");
        }
    }

    #endregion loading the Kenney assets

    #region building the scene

    private void CreateCharacter()
    {
        //The rest pose is a real region of the sheet, and it is the frame the sprite starts on; the
        //  first engine cycle swaps it for the idle animation
        Frame restPose = _characterSheet[ModelRenderOptions.RestPoseRegionName, 0, _facing];

        _character = Engine.Managers.Sprites.CreateSprite(_actorLayer, restPose, "character");
        _character.SetPosition(StartCell);
        _character.Visible = true;
        _character.ZOrder = 20;

        //A sprite is sized to its layer's tile size by default. The model cells are bigger than a map
        //  tile on purpose, so the character is drawn at the cell size it was rendered at, standing on
        //  the middle of its grid cell.
        _character.RenderSize = new Size(CharacterFramePx, CharacterFramePx);
        _character.HorizAlign = HorizontalAlignment.Center;
        _character.VertAlign = VerticalAlignment.Bottom;

        //Most of a model cell is empty air around the character, so the box that picks things up is
        //  inset to roughly his feet
        _character.AdjustCollisionArea = new CollisionAdjust(top: 14, bottom: 2, left: 9, right: 9);
    }

    private void CreateCollectibles()
    {
        for (int index = 0; index < CollectibleCells.Length; index++)
        {
            string frameName = DemoAssetKeys.CollectibleFrames[index % DemoAssetKeys.CollectibleFrames.Length];

            if (_collectibleAtlas.GetRegion(frameName) is null)
            {
                DemoLog.Write($"WARNING: collectibles: the atlas has no '{frameName}' frame.");
                continue;
            }

            (int column, int row) = CollectibleCells[index];

            //An atlas frame is a 1x1 region, so its one tile is [frameName, 0, 0]
            Sprite sprite = Engine.Managers.Sprites.CreateSprite(
                _actorLayer, _collectibleAtlas[frameName, 0, 0], $"collectible-{index}");

            sprite.SetPosition(new Vector2(column, row));
            sprite.Visible = true;
            sprite.ZOrder = 10;
            sprite.RenderSize = new Size(CollectiblePx, CollectiblePx);
            sprite.HorizAlign = HorizontalAlignment.Center;
            sprite.VertAlign = VerticalAlignment.Middle;

            _collectibles.Add(new Collectible(sprite, frameName));
        }

        DemoLog.Write(
            $"collectibles placed: {_collectibles.Count} atlas frame(s) - " +
            $"{string.Join(", ", _collectibles.Select(collectible => collectible.FrameName))}.");
    }

    private void CreateHudPanel(View view)
    {
        var panel = new DirectRectangle(
                Color.FromArgb(205, 18, 22, 32),
                RenderSurface.Host,
                view,
                new Rectangle(14, 12, 500, 74),
                "hud-panel")
            .SetFilled(true)
            .SetBorderColor(Color.FromArgb(230, 232, 216, 160))
            .SetStrokeWidth(2.0f)
            .SetCornerRadius(8.0f);
        panel.ZOrder = 1000;

        var title = new TextBlock(RenderSurface.Host, view, new Rectangle(28, 20, 350, 22), "hud-title")
            .SetFont(_titleFont ?? _hudFont ?? SKTypeface.Default, 16.0f)
            .SetColors(new SKColor(255, 236, 170), SKColors.Transparent)
            .SetAlignment(SKTextAlign.Left, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .SetText("KENNEY ASSETS DEMO");
        title.ZOrder = 1001;

        _statusText = new TextBlock(RenderSurface.Host, view, new Rectangle(28, 44, 390, 24), "hud-status")
            .SetFont(_hudFont ?? SKTypeface.Default, 12.0f, minSize: 10.0f)
            .SetColors(SKColors.White, SKColors.Transparent)
            .SetAlignment(SKTextAlign.Left, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false);
        _statusText.ZOrder = 1001;

        if (_iconFont is null) { return; }

        //A Kenney input-prompt font is an ICON font: its glyphs are in the private use area and it
        //  carries no letters, so it draws a prompt and never a word.
        var prompt = new TextBlock(RenderSurface.Host, view, new Rectangle(430, 16, 44, 40), "hud-prompt")
            .SetFont(_iconFont, 30.0f)
            .SetColors(new SKColor(196, 226, 255), SKColors.Transparent)
            .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .SetText(DemoAssetKeys.TouchPromptGlyph);
        prompt.ZOrder = 1001;

        var promptCaption = new TextBlock(
                RenderSurface.Host, view, new Rectangle(416, 56, 72, 16), "hud-prompt-caption")
            .SetFont(_hudFont ?? SKTypeface.Default, 10.0f, minSize: 8.0f)
            .SetColors(new SKColor(160, 176, 200), SKColors.Transparent)
            .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .SetText("icon font");
        promptCaption.ZOrder = 1001;
    }

    private void CreatePackNote(View view)
    {
        string packs = _provider is null
            ? "no packs registered"
            : string.Join(
                "   |   ",
                _provider.Packs.Select(pack => $"{pack.Slug} ({pack.DisplayName}), {pack.AssetCount} assets"));

        var note = new TextBlock(
                RenderSurface.Host,
                view,
                new Rectangle(14, RenderHeight - 32, RenderWidth - 28, 20),
                "pack-note")
            .SetFont(_hudFont ?? SKTypeface.Default, 11.0f, minSize: 9.0f)
            .SetColors(new SKColor(226, 232, 244), new SKColor(18, 22, 32, 180))
            .SetAlignment(SKTextAlign.Left, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .SetPadding(8.0f, 2.0f)
            .SetText($"Kenney CC0 packs:   {packs}");
        note.ZOrder = 1000;
    }

    private void CreateVectorBadge(View view)
    {
        if (_vectorBadge is null) { return; }

        var caption = new TextBlock(
                RenderSurface.Host,
                view,
                new Rectangle(RenderWidth - 196, RenderHeight - 168, BadgeWidthPx, 16),
                "badge-caption")
            .SetFont(_hudFont ?? SKTypeface.Default, 10.0f, minSize: 8.0f)
            .SetColors(new SKColor(196, 206, 226), new SKColor(18, 22, 32, 160))
            .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .SetText("SVG, rasterized on load");
        caption.ZOrder = 1000;

        //The vector route's default region is the whole rasterized picture, and the sheet's bitmap is
        //  what a direct drawing wants
        var badge = new DirectImage(
                _vectorBadge.SkBitmap,
                RenderSurface.Host,
                view,
                new Rectangle(RenderWidth - 196, RenderHeight - 150, BadgeWidthPx, BadgeHeightPx),
                "badge")
            .SetScaleMode(DirectImage.ScaleMode.Fit)
            .SetOpacity(220);
        badge.ZOrder = 1001;
    }

    #endregion building the scene

    #region playing

    private bool Held(VirtualKey key) => _keysDown.Contains(key);

    private void OnKeyDown(KeyDownEventArgs args)
    {
        var key = (VirtualKey)args.KeyCode;

        //Keys reach the engine's poller only while the game surface holds keyboard focus, so say once
        //  that the input path is live
        if (!_loggedFirstKey)
        {
            _loggedFirstKey = true;
            DemoLog.Write($"keyboard input reached the demo (first key: {key}).");
        }

        switch (args.KeyAction)
        {
            case KeyAction.Pressed:
                _keysDown.Add(key);
                break;

            case KeyAction.Released:
                _keysDown.Remove(key);
                break;
        }
    }

    //Runs on the engine thread, at the top of every cycle, BEFORE the engine moves anything
    private void BeforeCycleWork()
    {
        if (_character is null) { return; }

        float moveX = (Held(VirtualKey.Right) || Held(VirtualKey.D) ? 1.0f : 0.0f)
            - (Held(VirtualKey.Left) || Held(VirtualKey.A) ? 1.0f : 0.0f);
        float moveY = (Held(VirtualKey.Down) || Held(VirtualKey.S) ? 1.0f : 0.0f)
            - (Held(VirtualKey.Up) || Held(VirtualKey.W) ? 1.0f : 0.0f);

        if (moveX != 0.0f && moveY != 0.0f)
        {
            moveX *= DiagonalStep;
            moveY *= DiagonalStep;
        }

        //Sprite velocity is in GRID cells per second on the sprite's own layer
        _character.Movement.SetVelocity(new Vector2(moveX * WalkCellsPerSecond, moveY * WalkCellsPerSecond));

        bool moving = moveX != 0.0f || moveY != 0.0f;

        if (moving)
        {
            //Standing still keeps the direction last walked in, which is what a player expects
            _facing = ModelSheetFacing.DirectionFromMovement(moveX, moveY, CharacterDirections);
        }

        PlayAnimation(moving ? "walk" : "idle");
    }

    //Runs on the engine thread, after the engine has moved everything and before the frame is drawn
    private void AfterCycleWork()
    {
        if (_character is null) { return; }

        KeepCharacterOnTheMap();
        CollectWhatTheCharacterTouches();
        UpdateStatusText();
    }

    private void PlayAnimation(string animation)
    {
        if (_character is null) { return; }

        string key = CycleKey(animation, _facing);

        //Re-starting the cycle that is already playing would restart it every cycle
        if (key == _currentCycleKey || !_cycleKeys.Contains(key)) { return; }

        _currentCycleKey = key;
        _character.TileAnimator.StartAnimation(key);
    }

    private void KeepCharacterOnTheMap()
    {
        if (_character is null) { return; }

        Vector2 position = _character.GetPosition();
        float lastColumn = Math.Max(0, _actorLayer.GridColumnCount - 1);
        float lastRow = Math.Max(0, _actorLayer.GridRowCount - 1);

        Vector2 clamped = new(
            Math.Clamp(position.X, 0.0f, lastColumn),
            Math.Clamp(position.Y, 0.0f, lastRow));

        if (clamped != position)
        {
            _character.SetPosition(clamped);
        }
    }

    private void CollectWhatTheCharacterTouches()
    {
        if (_character is null) { return; }

        Rectangle reach = _character.CollisionArea;

        foreach (Collectible collectible in _collectibles)
        {
            if (collectible.Collected || !reach.IntersectsWith(collectible.Sprite.CollisionArea))
            {
                continue;
            }

            collectible.Collected = true;
            collectible.Sprite.Visible = false;
            _collected++;

            _pickUpSound?.Play();

            DemoLog.Write($"collected: {collectible.FrameName} ({_collected} of {_collectibles.Count}).");
        }
    }

    private void UpdateStatusText()
    {
        if (_statusText is null) { return; }

        string text = _collectibles.Count > 0 && _collected == _collectibles.Count
            ? $"collected {_collected} of {_collectibles.Count} - all of them"
            : $"collected {_collected} of {_collectibles.Count}   |   arrows or WASD to walk";

        //A TextBlock may be re-texted every frame, but there is no reason to re-lay-out an unchanged line
        if (text == _lastStatusText) { return; }

        _lastStatusText = text;
        _statusText.SetText(text);
    }

    #endregion playing
}
