using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Windows.System;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.SkiaSharp;
using CodeBrix.Platform.GameEngine.Timers;
using Spot.Brix.Game;

using EngineTimer = CodeBrix.Platform.GameEngine.Timers.Timer;

namespace Spot.Brix;

/// <summary>
/// Game host for the Spot.Brix sample: a 2&#8211;4 player Ataxx-style board game. Uses
/// <see cref="CodeBrixGameHost"/> with the <see cref="GameSurfaceCanvas"/> CpuRendering (CPU) render surface.
/// </summary>
public sealed class SpotBrixGameHost : CodeBrixGameHost
{
    private bool _initialGameStarted = false;
    private bool _handleHumanInput = false;
    private bool _showScores = true;
    private bool _startupPresentationShown = false;

    private ParticleSurface? _particleSurface;

    // The AI turn schedules two one-shot timers (select, then move). They are held in fields so a
    // new game can cancel a turn that is still pending: with local timers a move scheduled in the
    // previous game fired 0.6 s into the new one and acted on cells that no longer exist.
    private EngineTimer? _pendingComputerSelectTimer;
    private EngineTimer? _pendingComputerMoveTimer;

    private SplashOverlay? _splash;

    internal TextBlock? _player1Text;
    internal DirectRectangle? _player1Rectangle;
    internal TextBlock? _player2Text;
    internal DirectRectangle? _player2Rectangle;
    internal TextBlock? _player3Text;
    internal DirectRectangle? _player3Rectangle;
    internal TextBlock? _player4Text;
    internal DirectRectangle? _player4Rectangle;
    internal TextBlock? _gameMessageText;
    internal DirectRectangle? _gameMessageRectangle;

    private AudioResource _music = null!;

    private AudioResource? _spotSelected;
    private AudioResource? _spotDeselected;
    private AudioResource _velcro = null!;
    private AudioResource _drop = null!;
    private AudioResource _gameWin = null!;
    private AudioResource _gameLose = null!;
    private AudioResource _bump = null!;
    private AudioResource? _knock;

    private Tilesheet _spotSheetDefault = null!;
    private Tilesheet _spotSheetSelected = null!;

    internal Tilesheet _clouds = null!;

    internal SKTypeface _font = null!;

    internal SpotGame SpotGame { get; private set; } = null!;

    private static readonly Random _rng = new();

    /// <summary>The image the start-up splash is built around.</summary>
    private const string SplashIconFileName = "icon-codebrix-128.png";

    /// <summary>The edge length, in pixels, of the square splash title card.</summary>
    private const int SplashSizePx = 768;

    /// <summary>The wording drawn under the icon on the splash title card.</summary>
    private const string SplashTitle = "Spot.Brix";

    /// <summary>The file the finished board is written to when a game ends.</summary>
    private const string SaveGameFileName = "savegame.json";

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotBrixGameHost"/> class.
    /// </summary>
    /// <param name="renderSurface">The render surface to draw into.</param>
    public SpotBrixGameHost(GameSurfaceCanvas renderSurface)
        : base(renderSurface)
    {
    }

    private static string GetAssetPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "assets", fileName);

    #region CodeBrixGameHost overrides

    protected override void LoadAssets()
    {
        // Pin the device format BEFORE loading any audio. These assets are a mix of rates:
        // the music and the bubble-pop are 48 kHz, the victory/knock/lose clips 44.1 kHz (the
        // lose clip mono), and the bump/velcro/water-drip effects 24 kHz. CodeBrix.Audio has no
        // playback-time resampler, so without a pinned format the shared output silently adopted
        // the rate of whichever sound played first (the 48 kHz music) and every 24 kHz and
        // 44.1 kHz effect then threw on Play - the bump on any invalid selection, the water-drip
        // on EVERY completed move. Pinning makes the short-effect preload rate-convert each clip
        // once at load time instead. Channel count needs no help: the output matches mono to
        // stereo itself. 48 kHz is the right target because the long music track streams rather
        // than preloads, so it is the one clip that cannot be converted up front.
        AudioSystem.Initialize(48000, 2);

        // load standalone audio files
        _music = Engine.Managers.AudioResources.LoadFromFile("music", GetAssetPath("sounovamusic-puzzle-amp-casual-game-music-460543.mp3"));
        _music.IsLooping = true;

        _spotSelected = Engine.Managers.AudioResources.LoadFromFile("spotSelected", GetAssetPath("universfield-bubble-pop-293342.mp3"));
        _spotSelected.Volume = 0.4f;

        _spotDeselected = Engine.Managers.AudioResources.LoadFromFile("spotDeselected", GetAssetPath("universfield-bubble-pop-293342.mp3"));
        _spotDeselected.Volume = 0.15f;

        _velcro = Engine.Managers.AudioResources.LoadFromFile("velcro", GetAssetPath("freesound_community-velcro_fast-91558.mp3"));
        _drop = Engine.Managers.AudioResources.LoadFromFile("drop", GetAssetPath("freesound_community-water-drip-45622.mp3"));
        _gameWin = Engine.Managers.AudioResources.LoadFromFile("gameWin", GetAssetPath("peekaboolabcreative-11l-victory_sound_with_t-1749487402950-357606.mp3"));
        _gameLose = Engine.Managers.AudioResources.LoadFromFile("gameLose", GetAssetPath("freesound_community-080047_lose_funny_retro_video-game-80925.mp3"));
        _bump = Engine.Managers.AudioResources.LoadFromFile("bump", GetAssetPath("freesound_community-bump-7-92964.mp3"));
        _knock = Engine.Managers.AudioResources.LoadFromFile("knock", GetAssetPath("rohhsadotcom-knock-on-wood-02-421991.mp3"));

        // load standalone video files

        // load standalone font files
        _font = Engine.Managers.Fonts.LoadFromFile("main", GetAssetPath("ArchitectsDaughter-Regular.ttf"));

        // load standalone cursor files
    }

    protected override void LoadTilesheets()
    {
        // splash logo
        var splash = TilesheetRegistry.Instance.LoadFromImageFile("splash", GetAssetPath("spot.png"));
        splash.ApplyMask(Color.Black.ToSKColor());

        _spotSheetDefault = TilesheetRegistry.Instance.LoadFromImageFile("spots", GetAssetPath("spot_defaults.png"));
        _spotSheetDefault.DefaultRegion.TileSize = new Size(93, 96);

        _spotSheetSelected = TilesheetRegistry.Instance.LoadFromImageFile("selected", GetAssetPath("spot_selected.png"));
        _spotSheetSelected.DefaultRegion.TileSize = new Size(64, 64);

        _clouds = TilesheetRegistry.Instance.LoadFromImageFile("clouds", GetAssetPath("clouds.png"));
    }

    protected override Scene CreateInitialScene()
    {
        var scene = new Scene();

        var sceneLayer1 = scene.AddLayer(
            columnCount: 1,
            rowCount: 1,
            width: 768,
            height: 768,
            zOrder: 10,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        sceneLayer1.ShowGridLines = false;

        return scene;
    }

    protected override void OnSceneBound()
    {
        RenderSurface.Host.Backbuffer.ClearColor = Color.CornflowerBlue.ToSKColor();
        SpotGame = new SpotGame();
        HookSpotGameEvents();
    }

    protected override void CreateDirectDrawings()
    {
        // The opening screen (title art + drifting spots) and the music belong to the post-splash
        // phase, so that the splash overlay is the only thing on screen while it plays. See
        // BeginPostSplashStartup, which the splash raises once it has faded out.
    }

    protected override void OnEngineStarted()
    {
        ShowSplash();
    }

    protected override void OnMouseAdapterInitialized()
    {
        if (Engine.Input.MouseEventPoller is null)
            return;

        Engine.Input.MouseEventPoller.MouseEvent += MouseEventPoller_MouseEvent;
        Engine.Input.MouseEventPoller.StartMonitoringMouse();
    }

    protected override void OnKeyboardAdapterInitialized()
    {
        if (Engine.Input.KeyboardEventPoller is null)
            return;

        Engine.Input.KeyboardEventPoller.KeyDown += KeyboardEventPoller_KeyDown;
        Engine.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.S);
    }

    protected override void UnhookEvents()
    {
        if (Engine.Input.MouseEventPoller is not null)
            Engine.Input.MouseEventPoller.MouseEvent -= MouseEventPoller_MouseEvent;

        if (Engine.Input.KeyboardEventPoller is not null)
            Engine.Input.KeyboardEventPoller.KeyDown -= KeyboardEventPoller_KeyDown;

        CancelPendingComputerTurn();

        _splash?.Dispose();
        _splash = null;

        NewGameRequested = null;

        UnhookSpotGameEvents();
    }

    #endregion CodeBrixGameHost overrides

    #region splash and opening screen

    /// <summary>
    /// Raised when the player asks for a new game from inside the game surface — that is, on the very
    /// first left click while the opening screen is showing. The UI layer handles this by opening its
    /// New Game dialog. The event is raised on the UI thread, so a handler may touch XAML directly.
    /// </summary>
    public event Action? NewGameRequested;

    /// <summary>
    /// Creates the start-up splash overlay. When no splash image can be produced the opening screen is
    /// shown straight away instead, so the game never gets stuck behind a missing asset.
    /// </summary>
    private void ShowSplash()
    {
        if (RenderSurface.Host.ViewManager.Views.Count == 0)
        {
            BeginPostSplashStartup();
            return;
        }

        using var imageStream = CreateSplashImageStream();

        if (imageStream is null)
        {
            BeginPostSplashStartup();
            return;
        }

        _splash = SplashOverlay.TryCreate(imageStream,
                                          RenderSurface.Host,
                                          RenderSurface.Host.ViewManager.Views[0],
                                          holdSeconds: 1.5f,
                                          onSplashCompleted: OnSplashCompleted,
                                          nickname: "spot-splash");

        if (_splash is null)
            BeginPostSplashStartup();
        else
            Engine.Logger.LogInformation("Spot.Brix splash overlay started.");
    }

    private void OnSplashCompleted()
    {
        // The overlay disposes itself before raising this, so only the reference has to be dropped.
        _splash = null;

        BeginPostSplashStartup();
    }

    /// <summary>
    /// Paints the splash title card: the CodeBrix icon over the game's name, drawn at the board's
    /// design size so it stays crisp instead of being stretched from a small icon file.
    /// </summary>
    /// <returns>A PNG stream the caller owns, or <see langword="null"/> when the icon file is missing.</returns>
    private Stream? CreateSplashImageStream()
    {
        var iconPath = GetAssetPath(SplashIconFileName);

        if (!File.Exists(iconPath))
            return null;

        using var icon = SKBitmap.Decode(iconPath);

        if (icon is null)
            return null;

        var info = new SKImageInfo(SplashSizePx, SplashSizePx, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float iconLeft = (SplashSizePx - icon.Width) / 2f;
        float iconTop = (SplashSizePx / 2f) - icon.Height;
        var iconRect = new SKRect(iconLeft, iconTop, iconLeft + icon.Width, iconTop + icon.Height);

        using (var iconPaint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawBitmap(icon, iconRect, SKSamplingOptions.Default, iconPaint);
        }

        using var titleFont = new SKFont(_font, 64f);
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 140), IsAntialias = true };
        using var title = new SKPaint { Color = SKColors.White, IsAntialias = true };

        float baseline = (SplashSizePx / 2f) + 72f;

        canvas.DrawText(SplashTitle, (SplashSizePx / 2f) + 3f, baseline + 3f, SKTextAlign.Center, titleFont, shadow);
        canvas.DrawText(SplashTitle, SplashSizePx / 2f, baseline, SKTextAlign.Center, titleFont, title);

        canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(encoded.ToArray());
    }

    /// <summary>
    /// Builds the opening screen (title art plus the drifting spots) and starts the music. Called once,
    /// after the splash has finished; a second call is ignored.
    /// </summary>
    public void BeginPostSplashStartup()
    {
        if (_startupPresentationShown)
            return;

        _startupPresentationShown = true;

        if (Scene is null || Scene.SceneLayers.Count == 0)
            return;

        if (TilesheetRegistry.Instance.TryGet("splash", out Tilesheet tilesheet))
        {
            var directImage = new DirectImage(
                tilesheet.SkBitmap,
                RenderSurface.Host,
                Scene[0],
                new Rectangle(0, 0, 769, 769));

            directImage.ZOrder = 100;
            directImage.SetScaleMode(DirectImage.ScaleMode.Fit);
        }

        var particleSurface = new ParticleSurface(
            RenderSurface.Host,
            Scene[0],
            new Rectangle(0, 0, 769, 769));

        particleSurface.CullingMarginX = 1300f;
        particleSurface.ZOrder = 50;
        particleSurface.Emitters.Add(GetSpots(769, 769));

        if (MusicEnabled && _music is not null)
        {
            _music.Volume = 0.2f;

            if (!_music.IsPlaying)
                _music.Play();
        }

        Engine.Logger.LogInformation("Spot.Brix opening screen shown; click the board or the New Game button to start.");
    }

    #endregion splash and opening screen

    #region game settings

    internal bool MusicEnabled { get; private set; } = true;

    public void SetMusicEnabled(bool enabled)
    {
        MusicEnabled = enabled;

        if (enabled)
        {
            _music?.Play();
        }
        else
        {
            _music?.Stop();
        }
    }

    internal bool SoundEffectsEnabled { get; private set; } = true;

    public void SetSoundEffectsEnabled(bool enabled)
    {
        SoundEffectsEnabled = enabled;
    }

    internal bool JiggleEnabled { get; private set; } = true;

    public void SetJiggleEnabled(bool enabled)
    {
        JiggleEnabled = enabled;

        // The toggles are applied to the host before Initialize() runs, so the game may not exist yet.
        if (!enabled && SpotGame is not null)
        {
            foreach (var player in SpotGame.Players)
            {
                StopPlayerJiggle(player);
            }
        }
    }

    internal bool CloudsEnabled { get; private set; } = true;

    public void SetCloudsEnabled(bool enabled)
    {
        CloudsEnabled = enabled;

        if (enabled)
        {
            AddClouds();
        }
        else
        {
            DisposeParticleSurface();
        }
    }

    /// <summary>
    /// Starts a new game with the supplied options, clearing the current scene first. Any computer turn
    /// still pending from the previous game is cancelled before the board is rebuilt.
    /// </summary>
    /// <param name="options">The board size and player line-up to start with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public void StartNewGame(NewGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CancelPendingComputerTurn();

        _initialGameStarted = true;

        // Pre-null the reference: ClearAll() disposes the particle surface, and a later
        // DisposeParticleSurface() would otherwise dispose the same object a second time.
        _particleSurface = null;

        Engine.Managers.DirectDrawings.ClearAll();
        Engine.Managers.Sprites.Clear();
        Scene.RemoveAllLayers();

        // ClearAll() disposed the score/banner overlays. Drop the stale references as well: a game with
        // fewer players does not recreate them all, and OnRenderSurfaceResized / SetScoreVisible would
        // otherwise reach into disposed drawings.
        ClearOverlayFields();

        SetPlayerFrames(options.Players);

        var newGameResult = SpotGame.NewGame(options.BoardWidth, options.BoardHeight, options.Players.ToArray());

        Scene.AddLayer(newGameResult.Field);
        Scene.AddLayer(newGameResult.BackgroundField);

        if (_music != null)
            _music.Volume = 0.1f;

        CreateTextBlockFields();
    }

    /// <summary>
    /// Queues <see cref="StartNewGame"/> onto the engine thread. This is what a UI event handler should
    /// call: the scene may only be rebuilt from the thread that runs the engine cycle.
    /// </summary>
    /// <param name="options">The board size and player line-up to start with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public void StartNewGameOnEngineThread(NewGameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Engine.EngineDispatcher.Post(() => StartNewGame(options));
    }

    /// <summary>
    /// Starts a default 4-player game (1 human, 3 AI) on an 8×8 board.
    /// Useful as a quick-start shortcut; production UI should call
    /// <see cref="StartNewGame"/> with user-configured <see cref="NewGameOptions"/> instead.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread: the work is queued onto the engine thread, and runs inline when
    /// the caller is already on it.
    /// </remarks>
    public void StartDefaultGame()
    {
        var players = new List<Player>
        {
            new Player { Name = "Player 1", Type = PlayerType.Human,    ColorItem = new ColorItem("Blue",   SKColors.Blue,   SKColors.White) },
            new Player { Name = "Player 2", Type = PlayerType.Computer, ColorItem = new ColorItem("Red",    SKColors.Red,    SKColors.White) },
            new Player { Name = "Player 3", Type = PlayerType.Computer, ColorItem = new ColorItem("Green",  SKColors.Green,  SKColors.Black) },
            new Player { Name = "Player 4", Type = PlayerType.Computer, ColorItem = new ColorItem("Yellow", SKColors.Yellow, SKColors.Blue)  },
        };

        var options = new NewGameOptions
        {
            BoardWidth  = 8,
            BoardHeight = 8,
            Players     = players,
        };

        StartNewGameOnEngineThread(options);
    }

    #endregion game settings

    #region private methods

    /// <summary>
    /// Cancels the computer turn that is waiting to select or to move, if there is one.
    /// </summary>
    private void CancelPendingComputerTurn()
    {
        _pendingComputerSelectTimer?.Dispose();
        _pendingComputerSelectTimer = null;

        _pendingComputerMoveTimer?.Dispose();
        _pendingComputerMoveTimer = null;
    }

    /// <summary>
    /// Forgets the score and game-over overlays without disposing them; the caller has already cleared
    /// the direct drawings that owned them.
    /// </summary>
    private void ClearOverlayFields()
    {
        _player1Text = null;
        _player1Rectangle = null;
        _player2Text = null;
        _player2Rectangle = null;
        _player3Text = null;
        _player3Rectangle = null;
        _player4Text = null;
        _player4Rectangle = null;
        _gameMessageText = null;
        _gameMessageRectangle = null;
    }

    private void KeyboardEventPoller_KeyDown(KeyDownEventArgs args)
    {
        if (args.KeyAction != KeyAction.Pressed)
            return;

        // args.KeyConfig.Key holds the integer key code registered via StartMonitoringKey.
        if (int.TryParse(args.KeyConfig.Key, out int code) && code == (int)VirtualKey.S)
            SetScoreVisible(!_showScores);
    }

    private void MouseEventPoller_MouseEvent(CodeBrix.Platform.GameEngine.Input.Mouse.MouseEventArgs args)
    {
        // While the opening screen is showing, the first click is the invitation to set a game up.
        if (!_initialGameStarted && args.LeftButtonJustPressed)
        {
            RaiseNewGameRequested();
            return;
        }

        if (!_handleHumanInput)
            return;

        if (Scene is null || Scene.SceneLayers.Count == 0)
            return;

        if (RenderSurface.Host.ViewManager.Views.Count == 0)
            return;

        var view = RenderSurface.Host.ViewManager.Views[0];
        var layer = Scene.SceneLayers[0];

        var screenPos = args.CurrentPosition;

        if (args.LeftButtonJustPressed)
        {
            var selectedCoord = view.ScreenPxToGrid(layer, screenPos);

            if (selectedCoord.X >= 0 && selectedCoord.X < layer.GridColumnCount &&
                selectedCoord.Y >= 0 && selectedCoord.Y < layer.GridRowCount)
            {
                var cell = SpotGame.SpotGameField.GetCell((int)selectedCoord.X, (int)selectedCoord.Y);

                if (SpotGame.AttemptSelectCell(cell, out var playerMovement) && playerMovement != null)
                    SpotGame.ExecuteMove(playerMovement.Value);
            }
        }
    }

    /// <summary>
    /// Raises <see cref="NewGameRequested"/> on the UI thread. The mouse poller runs on the engine
    /// thread, and the handler puts a dialog on screen.
    /// </summary>
    private void RaiseNewGameRequested()
    {
        var handler = NewGameRequested;

        if (handler is null)
            return;

        Engine.Logger.LogInformation("Spot.Brix new game requested from the game surface.");

        if (Engine.UiDispatcher is not null && !Engine.UiDispatcher.IsOnUIThread)
            Engine.UiDispatcher.Post(() => handler());
        else
            handler();
    }

    private void SetPlayerFrames(List<Player> players)
    {
        foreach (var player in players)
        {
            switch (player.ColorItem.Name)
            {
                case "Blue":
                    player.DefaultFrame = new Frame(_spotSheetDefault, 0, 0);
                    player.ActiveFrame = new Frame(_spotSheetSelected, 0, 0);
                    break;
                case "Green":
                    player.DefaultFrame = new Frame(_spotSheetDefault, 0, 1);
                    player.ActiveFrame = new Frame(_spotSheetSelected, 1, 0);
                    break;
                case "Violet":
                    player.DefaultFrame = new Frame(_spotSheetDefault, 0, 2);
                    player.ActiveFrame = new Frame(_spotSheetSelected, 2, 0);
                    break;
                case "Red":
                    player.DefaultFrame = new Frame(_spotSheetDefault, 0, 3);
                    player.ActiveFrame = new Frame(_spotSheetSelected, 3, 0);
                    break;
                case "Yellow":
                    player.DefaultFrame = new Frame(_spotSheetDefault, 0, 4);
                    player.ActiveFrame = new Frame(_spotSheetSelected, 4, 0);
                    break;
                default:
                    break;
            }
        }
    }
    private void StartPlayerJiggle(Player player)
    {
        if (JiggleEnabled)
        {
            foreach (var cell in SpotGame.SpotGameField.GetAllCellsForPlayer(player))
            {
                cell.Sprite?.StartJiggle(loop: true);
            }
        }
    }

    private void StopPlayerJiggle(Player player)
    {
        foreach (var cell in SpotGame.SpotGameField.GetAllCellsForPlayer(player))
        {
            cell.Sprite?.StopJiggle();
        }
    }

    private void JiggleAllPlayers()
    {
        foreach (var player in SpotGame.Players)
        {
            StartPlayerJiggle(player);
        }
    }

    #endregion private methods

    #region particle emitters

    private ParticleEmitter GetSpots(float width, float height)
    {
        SKColor[] colors =
        {
            SKColors.Red,
            SKColors.Blue,
            SKColors.Yellow,
            SKColors.Green,
            SKColors.Violet
        };

        return new ParticleEmitter
        {
            Position = new PointF(width * 1.1f, height * 0.5f),
            JitterY = height * 0.5f,

            EmitRate = 0.65f,
            LifeRange = (1000f, 2000f),

            VelocityRangeX = (-100f, -50f),
            VelocityRangeY = (-1f, 1f),

            SizeRange = (40f, 80f),

            GravityY = 0f,
            BlendMode = SKBlendMode.SrcOver,

            OnSpawn = (ref Particle p) =>
            {
                var baseColor = colors[_rng.Next(colors.Length)];
                p.Color = baseColor.WithAlpha(255);
            }
        };
    }

    /// <summary>
    /// Disposes the cloud particle surface, if one exists. This is the only place the surface is
    /// disposed, so the reference can never outlive the object it names.
    /// </summary>
    private void DisposeParticleSurface()
    {
        _particleSurface?.Dispose();
        _particleSurface = null;
    }

    private void AddClouds()
    {
        // Clouds live on the background game field, which only exists once a game has been started.
        if (SpotGame is null || SpotGame.Players.Length == 0)
            return;

        DisposeParticleSurface();

        _particleSurface = new ParticleSurface(
            RenderSurface.Host,
            SpotGame.BackgroundGameField,
            new Rectangle(0, 0, 769, 769),
            "cloudSurface",
            4);

        _particleSurface.CullingMarginX = 1300f;
        _particleSurface.ZOrder = 50;
        _particleSurface.Emitters.Add(GetClouds(769, 769));
    }

    private ParticleEmitter GetClouds(float width, float height)
    {
        return new ParticleEmitter
        {
            Position = new PointF(width * 1.4f, height * 0.5f),
            JitterY = height * 0.5f,

            EmitRate = 0.075f,
            LifeRange = (2000f, 2000f),

            VelocityRangeX = (-50f, -25f),
            VelocityRangeY = (-1f, 1f),

            SizeRange = (200f, 500f),

            GravityY = 0f,
            BlendMode = SKBlendMode.SrcOver,

            ParticleSprite = _clouds.SkBitmap,

            OnSpawn = (ref Particle p) =>
            {
                p.AngularVel = 0;
                p.Rotation = 0;

                byte alpha = (byte)_rng.Next(100, 180);
                p.Tint = new SKColor(255, 255, 255, alpha);
            }
        };
    }

    #endregion particle emitters

    #region score display

    private void CreateTextBlockFields()
    {
        // upper left
        _player1Text = new TextBlock(RenderSurface.Host,
                                     RenderSurface.Host.ViewManager.Views[0],
                                     new Rectangle(10, 10, 200, 50));
        _player1Text.SetFont(_font, 24, 12)
                    .SetColors(SpotGame.Players[0].ColorItem.TextColor, SKColors.Transparent)
                    .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
                    .SetText(SpotGame.Players[0].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[0]))
                    .SetMaxLines(1)
                    .UseShadow()
                    .SetShadow(3, 3, 200, 3.0f);
        _player1Text.ZOrder = 20;

        _player1Rectangle = new DirectRectangle(SpotGame.Players[0].ColorItem.Color.ToColor(),
                                                RenderSurface.Host,
                                                RenderSurface.Host.ViewManager.Views[0],
                                                _player1Text.ScreenBounds);
        _player1Rectangle.SetCornerRadius(30)
                         .SetFilled(true);

        // bottom right
        _player2Text = new TextBlock(RenderSurface.Host,
                                     RenderSurface.Host.ViewManager.Views[0],
                                     new Rectangle(RenderSurface.Host.Backbuffer.Width - 210, RenderSurface.Host.Backbuffer.Height - 60, 200, 50));
        _player2Text.SetFont(_font, 24, 12)
                    .SetColors(SpotGame.Players[1].ColorItem.TextColor, SKColors.Transparent)
                    .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
                    .SetText(SpotGame.Players[1].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[1]))
                    .SetMaxLines(1)
                    .UseShadow()
                    .SetShadow(3, 3, 200, 3.0f);
        _player2Text.ZOrder = 20;

        _player2Rectangle = new DirectRectangle(SpotGame.Players[1].ColorItem.Color.ToColor(),
                                                RenderSurface.Host,
                                                RenderSurface.Host.ViewManager.Views[0],
                                                _player2Text.ScreenBounds);
        _player2Rectangle.SetCornerRadius(30)
                         .SetFilled(true);

        if (SpotGame.Players.Length >= 3)
        {
            // upper right
            _player3Text = new TextBlock(RenderSurface.Host,
                                         RenderSurface.Host.ViewManager.Views[0],
                                         new Rectangle(RenderSurface.Host.Backbuffer.Width - 210, 10, 200, 50));
            _player3Text.SetFont(_font, 24, 12)
                        .SetColors(SpotGame.Players[2].ColorItem.TextColor, SKColors.Transparent)
                        .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
                        .SetText(SpotGame.Players[2].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[2]))
                        .SetMaxLines(1)
                        .UseShadow()
                        .SetShadow(3, 3, 200, 3.0f);
            _player3Text.ZOrder = 20;

            _player3Rectangle = new DirectRectangle(SpotGame.Players[2].ColorItem.Color.ToColor(),
                                                    RenderSurface.Host,
                                                    RenderSurface.Host.ViewManager.Views[0],
                                                    _player3Text.ScreenBounds);
            _player3Rectangle.SetCornerRadius(30)
                             .SetFilled(true);
        }

        if (SpotGame.Players.Length >= 4)
        {
            // bottom left
            _player4Text = new TextBlock(RenderSurface.Host,
                                         RenderSurface.Host.ViewManager.Views[0],
                                         new Rectangle(10, RenderSurface.Host.Backbuffer.Height - 60, 200, 50));
            _player4Text.SetFont(_font, 24, 12)
                        .SetColors(SpotGame.Players[3].ColorItem.TextColor, SKColors.Transparent)
                        .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
                        .SetText(SpotGame.Players[3].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[3]))
                        .SetMaxLines(1)
                        .UseShadow()
                        .SetShadow(3, 3, 200, 3.0f);
            _player4Text.ZOrder = 20;

            _player4Rectangle = new DirectRectangle(SpotGame.Players[3].ColorItem.Color.ToColor(),
                                                    RenderSurface.Host,
                                                    RenderSurface.Host.ViewManager.Views[0],
                                                    _player4Text.ScreenBounds);
            _player4Rectangle.SetCornerRadius(30)
                             .SetFilled(true);
        }

        if (SpotGame.SpotGameField.GridColumnCount > 10 || SpotGame.SpotGameField.GridRowCount > 10)
        {
            SetScoreVisible(false);
        }
    }

    /// <summary>
    /// Re-anchors the corner score overlays (and any game-over banner) when the window is resized.
    /// The overlays are created once in <see cref="CreateTextBlockFields"/> using the backbuffer size
    /// at that moment; without this they drift out of position on resize — most visibly the
    /// width/height-anchored Player 2 (bottom-right) and Player 3 (top-right) labels.
    /// </summary>
    protected override void OnRenderSurfaceResized(int width, int height)
    {
        const int boxWidth = 200;
        const int boxHeight = 50;

        RepositionField(_player1Text, _player1Rectangle, new Rectangle(10, 10, boxWidth, boxHeight));                                          // upper left
        RepositionField(_player2Text, _player2Rectangle, new Rectangle(width - (boxWidth + 10), height - (boxHeight + 10), boxWidth, boxHeight)); // bottom right
        RepositionField(_player3Text, _player3Rectangle, new Rectangle(width - (boxWidth + 10), 10, boxWidth, boxHeight));                     // upper right
        RepositionField(_player4Text, _player4Rectangle, new Rectangle(10, height - (boxHeight + 10), boxWidth, boxHeight));                   // bottom left

        if (_gameMessageText is not null && _gameMessageRectangle is not null)
        {
            var msgBounds = new Rectangle(width / 2 - 180, height / 2 - 40, 360, 80);
            _gameMessageText.ScreenBounds = msgBounds;
            _gameMessageRectangle.ScreenBounds = msgBounds;
        }
    }

    private static void RepositionField(TextBlock? text, DirectRectangle? rectangle, Rectangle bounds)
    {
        if (text is null || rectangle is null)
            return;

        text.ScreenBounds = bounds;
        rectangle.ScreenBounds = bounds;
    }

    private void SetScoreVisible(bool visible)
    {
        _showScores = visible;

        if (_player1Text is not null)
        {
            _player1Text.Visible = visible;
            _player1Rectangle!.Visible = visible;
        }

        if (_player2Text is not null)
        {
            _player2Text.Visible = visible;
            _player2Rectangle!.Visible = visible;
        }

        if (_player3Text is not null)
        {
            _player3Text.Visible = visible;
            _player3Rectangle!.Visible = visible;
        }

        if (_player4Text is not null)
        {
            _player4Text.Visible = visible;
            _player4Rectangle!.Visible = visible;
        }

        if (visible)
            SetPlayerScores();
    }

    private void SetPlayerScores()
    {
        if (_player1Text is not null)
            _player1Text.SetText(SpotGame.Players[0].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[0]));

        if (_player2Text is not null)
            _player2Text.SetText(SpotGame.Players[1].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[1]));

        if (SpotGame.Players.Length >= 3)
            _player3Text?.SetText(SpotGame.Players[2].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[2]));

        if (SpotGame.Players.Length >= 4)
            _player4Text?.SetText(SpotGame.Players[3].Name + " - " + SpotGame.GetPlayerScore(SpotGame.Players[3]));
    }

    private void CreateGameOverText(List<Player> winningPlayers)
    {
        bool multipleWinners;
        string message;

        Color primaryTextColor = winningPlayers[0].ColorItem.TextColor.ToColor();
        Color? secondaryTextColor = null;
        Color primaryFillColor = winningPlayers[0].ColorItem.Color.ToColor();
        Color? secondaryFillColor = null;

        if (winningPlayers.Count == 1)
        {
            multipleWinners = false;
            message = winningPlayers[0].Name + " wins!";
        }
        else
        {
            multipleWinners = true;

            var names = winningPlayers.Select(p => p.Name).ToList();
            var formatted = names.Count == 2
                ? string.Join(" and ", names)
                : string.Join(", ", names.Take(names.Count - 1)) + $", and {names.Last()}";

            message = $"{formatted} tie!";

            secondaryTextColor = winningPlayers[1].ColorItem.TextColor.ToColor();
            secondaryFillColor = winningPlayers[1].ColorItem.Color.ToColor();
        }

        _gameMessageText = new TextBlock(RenderSurface.Host,
                                         RenderSurface.Host.ViewManager.Views[0],
                                         new Rectangle(RenderSurface.Host.Backbuffer.Width / 2 - 180, RenderSurface.Host.Backbuffer.Height / 2 - 40, 360, 80));
        _gameMessageText.SetFont(_font, 48, 16)
                        .SetColors(primaryTextColor.ToSKColor(), SKColors.Transparent)
                        .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
                        .SetText(message)
                        .UseShadow()
                        .SetShadow(5, 5, 200, 3.0f)
                        .EnableWrapping();

        _gameMessageText.ZOrder = 20;

        _gameMessageRectangle = new DirectRectangle(primaryFillColor,
                                                    RenderSurface.Host,
                                                    RenderSurface.Host.ViewManager.Views[0],
                                                    _gameMessageText.ScreenBounds);
        _gameMessageRectangle.SetCornerRadius(40)
                             .SetFilled(true)
                             .SetColor(primaryFillColor)
                             .SetBorderColor(primaryTextColor)
                             .SetStrokeWidth(2f)
                             .SetStrokeAlign(DirectRectangle.StrokeAlign.Outside);

        if (multipleWinners)
        {
            _gameMessageText.PulseColor(primaryTextColor, secondaryTextColor!.Value, 1.75f);
            _gameMessageRectangle.PulseFill(primaryFillColor, secondaryFillColor!.Value, 1.25f);
            _gameMessageRectangle.PulseBorder(primaryTextColor, secondaryTextColor.Value, 0.75f);
        }
    }

    #endregion score display

    #region SpotGame event handlers

    private void HookSpotGameEvents()
    {
        if (SpotGame is null)
            return;

        SpotGame.GameStarted += OnGameStarted;
        SpotGame.PlayerTurnStarted += OnPlayerTurnStarted;
        SpotGame.PlayerTurnEnded += OnPlayerTurnEnded;
        SpotGame.SpotSelected += OnSpotSelected;
        SpotGame.SpotDeselected += OnSpotDeselected;
        SpotGame.InvalidSelectionAttempted += OnInvalidSelectionAttempted;
        SpotGame.InvalidMoveAttempted += OnInvalidMoveAttempted;
        SpotGame.PlayerMoveStarted += OnPlayerMoveStarted;
        SpotGame.PlayerMoveStopped += OnPlayerMoveStopped;
        SpotGame.CellsCaptured += OnCellsCaptured;
        SpotGame.NoValidMovesAvailable += OnNoValidMovesAvailable;
        SpotGame.GameOver += OnGameOver;
    }

    private void UnhookSpotGameEvents()
    {
        if (SpotGame is null)
            return;

        SpotGame.GameStarted -= OnGameStarted;
        SpotGame.PlayerTurnStarted -= OnPlayerTurnStarted;
        SpotGame.PlayerTurnEnded -= OnPlayerTurnEnded;
        SpotGame.SpotSelected -= OnSpotSelected;
        SpotGame.SpotDeselected -= OnSpotDeselected;
        SpotGame.InvalidSelectionAttempted -= OnInvalidSelectionAttempted;
        SpotGame.InvalidMoveAttempted -= OnInvalidMoveAttempted;
        SpotGame.PlayerMoveStarted -= OnPlayerMoveStarted;
        SpotGame.PlayerMoveStopped -= OnPlayerMoveStopped;
        SpotGame.CellsCaptured -= OnCellsCaptured;
        SpotGame.NoValidMovesAvailable -= OnNoValidMovesAvailable;
        SpotGame.GameOver -= OnGameOver;
    }

    private void OnGameStarted(SpotGame game)
    {
        Engine.Logger.LogInformation("Spot.Brix game started on a {0}x{1} board with players: {2}",
            game.SpotGameField.GridColumnCount,
            game.SpotGameField.GridRowCount,
            string.Join(", ", game.Players.Select(p => p.Name)));

        if (MusicEnabled)
        {
            if (_music != null && !_music.IsPlaying)
                _music.Play();
        }

        if (CloudsEnabled)
            AddClouds();
    }

    private void OnPlayerTurnStarted(Player player)
    {
        Engine.Logger.LogDebug("Player {0}'s turn started", player.Name);
        StartPlayerJiggle(player);

        if (player.Type == PlayerType.Human)
        {
            _handleHumanInput = true;
        }
        else
        {
            _handleHumanInput = false;

            // Start a short timer before the computer moves. Both timers are held in fields so that
            // StartNewGame can cancel a turn that has not fired yet.
            CancelPendingComputerTurn();

            _pendingComputerSelectTimer = EngineTimer.Add(TimerType.PostCycle, TimerCycles.Once, 0.6);
            _pendingComputerSelectTimer.Tick += () =>
            {
                _pendingComputerSelectTimer?.Dispose();
                _pendingComputerSelectTimer = null;

                var moves = SpotGame.SpotGameField.GetBestMovesForPlayer(player);

                // The board can fill up between scheduling this turn and running it.
                if (moves.Count == 0)
                    return;

                var bestMove = moves[_rng.Next(moves.Count)];

                SpotGame.AttemptSelectCell(bestMove.FromCell, out _);

                // small delay before executing move to allow for selection animation
                _pendingComputerMoveTimer = EngineTimer.Add(TimerType.PostCycle, TimerCycles.Once, 0.6);
                _pendingComputerMoveTimer.Tick += () =>
                {
                    _pendingComputerMoveTimer?.Dispose();
                    _pendingComputerMoveTimer = null;

                    SpotGame.ExecuteMove(bestMove);
                };
            };
        }
    }

    private void OnPlayerTurnEnded(Player player)
    {
        Engine.Logger.LogDebug("Player {0}'s turn ended", player.Name);
        StopPlayerJiggle(player);
    }

    private void OnSpotSelected(SpotGameField.Cell cell)
    {
        Engine.Logger.LogDebug("Cell at ({0}, {1}) selected by player {2}", cell.X, cell.Y, cell.OccupiedBy!.Name);

        // Only a human selection gets the pop; the computer's own selections would otherwise chirp
        // once per AI turn.
        if (SoundEffectsEnabled && SpotGame.CurrentPlayer.Type == PlayerType.Human)
            _spotSelected?.Play();

        var sprite = cell.Sprite!;
        sprite.StopJiggle();
        sprite.CurrentFrame = cell.OccupiedBy.ActiveFrame;
        sprite.PulseBy(1.1f, 0.4f, 0.4f, true);
    }

    private void OnSpotDeselected(SpotGameField.Cell cell)
    {
        Engine.Logger.LogDebug("Cell at ({0}, {1}) deselected", cell.X, cell.Y);

        if (SoundEffectsEnabled)
            _spotDeselected?.Play();

        var sprite = cell.Sprite!;
        sprite.StartJiggle(loop: true);
        sprite.CurrentFrame = cell.OccupiedBy!.DefaultFrame;
        sprite.StopPulse(true, 0.2f);
    }

    private void OnInvalidSelectionAttempted(SpotGameField.Cell cell)
    {
        Engine.Logger.LogDebug("Invalid selection attempted at cell ({0}, {1})", cell.X, cell.Y);

        if (SoundEffectsEnabled)
        {
            _bump?.Play();
        }
    }

    private void OnInvalidMoveAttempted(SpotGameField.Cell cell)
    {
        Engine.Logger.LogDebug("Invalid move attempted to cell ({0}, {1})", cell.X, cell.Y);

        if (SoundEffectsEnabled)
            _knock?.Play();
    }

    private void OnPlayerMoveStarted(PlayerMovement movement)
    {
        if (movement.MovementType == MovementType.Jump && SoundEffectsEnabled)
        {
            _velcro?.Play();
        }
    }

    private void OnPlayerMoveStopped(PlayerMovement movement)
    {
        Engine.Logger.LogDebug("Player {0} performed a {1} move from ({2}, {3}) to ({4}, {5})",
            movement.Player.Name,
            movement.MovementType,
            movement.FromX, movement.FromY,
            movement.DestX, movement.DestY);

        if (SoundEffectsEnabled)
        {
            _drop?.Play();
        }

        if (_showScores)
            SetPlayerScores();

        SpotGame.NextPlayer();
    }

    private void OnCellsCaptured(List<SpotGameField.Cell> cellsCaptured)
    {
        Engine.Logger.LogDebug("{0} cells captured", cellsCaptured.Count);

        foreach (var cell in cellsCaptured)
        {
            var oldSprite = cell.Sprite;
            if (oldSprite == null)
                continue;

            Action? handler = null;
            handler = () =>
            {
                oldSprite.ResizeComplete -= handler;
                oldSprite.CurrentFrame = cell.OccupiedBy!.DefaultFrame;
                oldSprite.ResizeTo(new(56, 56), 0.2f);
            };

            oldSprite.ResizeComplete += handler;
            oldSprite.ResizeTo(new(1, 1), 0.2f);
        }
    }

    private void OnNoValidMovesAvailable(Player player)
    {
        Engine.Logger.LogDebug("No valid moves available for player {0}", player.Name);

        SpotGame.NextPlayer();
    }

    private void OnGameOver()
    {
        Engine.Logger.LogInformation("Spot.Brix game over.");

        _handleHumanInput = false;

        CancelPendingComputerTurn();

        SetScoreVisible(true);
        SetPlayerScores();
        StopPlayerJiggle(SpotGame.CurrentPlayer);
        JiggleAllPlayers();

        var allScores = SpotGame.GetAllPlayerScores();
        var maxScore = allScores.Values.Max();
        var winnersWithScores = allScores
            .Where(kvp => kvp.Value == maxScore)
            .Select(kvp => kvp.Key)
            .ToList();

        CreateGameOverText(winnersWithScores);

        if (MusicEnabled)
        {
            if (_music != null)
            {
                _music.Volume = 0.05f;

                var isHumanWinner = winnersWithScores.Any(winner => winner.Type == PlayerType.Human);
                if (isHumanWinner)
                    _gameWin?.Play();
                else
                    _gameLose?.Play();
            }
        }

        SaveFinishedGame();
    }

    /// <summary>
    /// Writes the finished board out with the engine's own save API, next to the executable. This is a
    /// showcase of the save round-trip rather than a feature: nothing reloads the file, and a failure
    /// is logged and swallowed so the end-of-game presentation is never interrupted.
    /// </summary>
    private void SaveFinishedGame()
    {
        var savePath = Path.Combine(AppContext.BaseDirectory, SaveGameFileName);

        try
        {
            Engine.State.SaveToFile(savePath, false, true);
            Engine.Logger.LogInformation("Finished game saved to {0}", savePath);
        }
        catch (Exception ex)
        {
            Engine.Logger.LogWarning(ex, "The finished game could not be saved to {0}.", savePath);
        }
    }

    #endregion SpotGame event handlers
}
