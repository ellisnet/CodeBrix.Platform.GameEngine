using System;
using System.Drawing;
using System.Numerics;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Host;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Logging;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Windows.System;

namespace CoordinateTest.Game;

/// <summary>
/// A coordinate-system / camera test bed: two orthogonal tile layers, animated rooster sprites, two
/// camera views, WASD camera-pan and arrow-key sprite movement, a screen&#8596;world&#8596;grid HUD,
/// scroll-wheel zoom, and click particle bursts. Ported from the upstream CoordinateTest demo onto a
/// CodeBrix.Platform <see cref="GameSurfaceCanvas"/> using the CpuRendering (CPU) render surface.
/// </summary>
public class CoordinateTestGame : IDisposable
{
    /// <summary>Gets the render surface control the game renders into.</summary>
    public GameSurfaceCanvas RenderSurface { get; private set; }

    /// <summary>Gets the active scene.</summary>
    public Scene Scene { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CoordinateTestGame"/> class.
    /// </summary>
    /// <param name="renderSurface">The render surface to draw into.</param>
    public CoordinateTestGame(GameSurfaceCanvas renderSurface)
    {
        RenderSurface = renderSurface;
    }

    /// <summary>
    /// Initializes the engine, loads content, builds the scene, wires input, and starts the loop.
    /// Must be called on the UI thread once the surface has a non-zero size.
    /// </summary>
    /// <param name="configPath">Optional engine configuration file path.</param>
    /// <param name="autoSaveConfig">Optional flag controlling automatic configuration save.</param>
    public void InitializeGame(string configPath = null, bool? autoSaveConfig = null)
    {
        EngineLogger.SetLogLevel(LogLevel.Warning);

        // initialize engine, platform-specific adapters, etc.
        Engine.Instance.Initialize(configPath, autoSaveConfig);

        // load game content here
        LoadTilesheets();

        // create initial scene here and bind to render surface
        Scene = CreateInitialScene();
        RenderSurface.Host.Bind(Scene, false);
        RenderSurface.Host.Backbuffer.FogPaint.Color = new SKColor(220, 230, 255, 120);

        RenderSurface.Host.ViewManager.AddView(new Rectangle(800, 0, 800, 300), 1f, 10);
        RenderSurface.Host.ViewManager.Views[0].Camera.SnapTo(new PointF(-800, -100));
        RenderSurface.Host.ViewManager.Views[1].Camera.SnapTo(new PointF(100, 100));
        RenderSurface.Host.RedrawDirtyRectangleOnly = true;

        RenderSurface.Host.Scene[0].OriginPx = new Point(-100, -100);

        InitSprites();
        InitDirectDrawings();

        RenderSurface.Host.ViewManager.Views[0].Camera.FollowCentered(SpriteManager.Instance.GetSpriteByID("rooster_1"));

        // configure input handling here
        ConfigureKeyboardInput();
        ConfigureMouseInput();

        // start the engine main loop
        Engine.Instance.Start(SynchronizationContext.Current);
    }

    #region load and init game content

    private void LoadTilesheets()
    {
        var tilesheet = TilesheetRegistry.Instance.LoadFromImageFile("rooster", AssetPath("rooster.bmp"));
        tilesheet.DefaultRegion.TileSize = new Size(50, 50);
        tilesheet.ApplyMask(SKColors.Black, 60);

        var tilesheet2 = TilesheetRegistry.Instance.LoadFromImageFile("tiles", AssetPath("original.bmp"));
        tilesheet2.DefaultRegion.TileSize = new Size(64, 32);
        tilesheet2.DefaultRegion.Area = new Rectangle(1, 1, tilesheet2.SkBitmap.Width - 2, tilesheet2.SkBitmap.Height - 2);
    }

    private static string AssetPath(string fileName)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "assets", fileName);

    private void InitSprites()
    {
        var tilesheet = TilesheetRegistry.Instance.GetAll()["rooster"];

        var sprite1 = SpriteManager.Instance.CreateSprite(Scene[0], tilesheet[0, 0], "rooster_1");
        sprite1.Visible = true;
        sprite1.CollisionsEnabled = true;

        var sprite2 = SpriteManager.Instance.CreateSprite(Scene[0], tilesheet[0, 0], "rooster_2");
        sprite2.Visible = true;
        sprite2.SetPosition(new Vector2(5, 0));
        sprite2.CollisionsEnabled = true;

        FrameSequence frameSequence = new FrameSequence();
        frameSequence.AddFrame(tilesheet, 0, 0);
        frameSequence.AddFrame(tilesheet, 1, 0);
        frameSequence.AddFrame(tilesheet, 2, 0);
        frameSequence.AddFrame(tilesheet, 3, 0);
        frameSequence.SequenceCycleType = CycleType.PingPong;
        sprite1.TileAnimator.CurrentCycle = new Cycle(frameSequence, 0.5f, "ani");
        sprite1.TileAnimator.StartAnimation();
    }

    private DirectRectangle _directRectangle;
    private TextBlock _textBlockCPS;
    private TextBlock _textBlockMouse;
    private ParticleSurface _particleSurface;
    private ParticleEmitter _clickEmitter;

    private TextBlock _spriteNameTag;

    private void InitDirectDrawings()
    {
        int surfaceWidth = RenderSurface.Host.RenderSurfaceAdapter.Width;

        var bounds1 = new Rectangle(surfaceWidth - 250, 0, 250, 150);
        var bounds2 = new Rectangle(surfaceWidth - 250, 200, 250, 150);

        _directRectangle = new DirectRectangle(Color.Wheat,
                                               RenderSurface.Host,
                                               RenderSurface.Host.ViewManager.Views[0],
                                               bounds1,
                                               null);
        _directRectangle.SetFilled(true).SetAlpha(128);

        _textBlockCPS = new TextBlock(RenderSurface.Host,
                                      RenderSurface.Host.ViewManager.Views[0],
                                      bounds1,
                                      null);
        _textBlockCPS.SetColors(Color.Black, Color.Transparent).ZOrder = 10;

        Engine.Instance.CPSCalculated += (e) =>
        {
            _textBlockCPS.SetText(e.ToString());
        };

        _textBlockMouse = new TextBlock(RenderSurface.Host,
                                        RenderSurface.Host.ViewManager.Views[0],
                                        bounds2,
                                        null);
        _textBlockMouse.SetColors(Color.Black, Color.Wheat).ZOrder = 10;

        InitializeParticles();

        _spriteNameTag = new TextBlock(RenderSurface.Host,
                                       Scene[0],
                                       null,
                                       new Rectangle(0, 0, 150, 30));
        _spriteNameTag.SetColors(Color.Blue, Color.White).SetText("Mister Rooster").ZOrder = 20;
        _spriteNameTag.Movement.FollowTileSoft(SpriteManager.Instance.GetSpriteByID("rooster_1"), 0.75f, 0.1f, new Vector2(0, 0.75f));
    }

    private void InitializeParticles()
    {
        var bounds = new Rectangle(
            0,
            0,
            RenderSurface.Host.RenderSurfaceAdapter.Width,
            RenderSurface.Host.RenderSurfaceAdapter.Height);

        _particleSurface = new ParticleSurface(RenderSurface.Host,
                                               RenderSurface.Host.ViewManager.Views[0],
                                               bounds,
                                               null);

        _particleSurface.GravityY = 0f;

        _clickEmitter = new ParticleEmitter
        {
            EmitRate = 0f, // we only use Burst(), no continuous emission

            LifeRange = (0.35f, 0.7f),
            VelocityRangeX = (-280f, 280f),
            VelocityRangeY = (-280f, 280f),
            SizeRange = (2f, 5f),

            Color = SKColors.OrangeRed,
            MaxVelocity = 400f,

            JitterX = 40,
            JitterY = 40,

            SpawnDistribution = ParticleSpawnDistribution.Gaussian,
            GaussianStdDev01 = 0.45f
        };
    }

    #endregion load and init game content

    private Scene CreateInitialScene()
    {
        var scene = new Scene();
        var sceneLayer1 = scene.AddLayer(60, 5, 64, 64, 10, 1f, CoordinateSystemTypes.Orthogonal);
        var sceneLayer2 = scene.AddLayer(60, 5, 32, 32, 5, 0.5f, CoordinateSystemTypes.Orthogonal);

        sceneLayer1.ShowGridLines = true;
        sceneLayer1.ShowCollisionBoxes = false;
        sceneLayer2.ShowGridLines = true;

        var sourceTilesheet = TilesheetRegistry.Instance.GetAll()["tiles"];
        sceneLayer1[0, 0].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[1, 0].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[2, 0].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[0, 1].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[1, 1].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[2, 1].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[0, 2].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[1, 2].CurrentFrame = sourceTilesheet[4, 4];
        sceneLayer1[2, 2].CurrentFrame = sourceTilesheet[4, 4];

        sceneLayer2[0, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[1, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[2, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[0, 1].CurrentFrame = sourceTilesheet[4, 3];
        sceneLayer2[1, 1].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[2, 1].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[0, 2].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[1, 2].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[2, 2].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[10, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[11, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[12, 0].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[10, 1].CurrentFrame = sourceTilesheet[4, 3];
        sceneLayer2[11, 1].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[12, 1].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[10, 2].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[11, 2].CurrentFrame = sourceTilesheet[3, 3];
        sceneLayer2[12, 2].CurrentFrame = sourceTilesheet[3, 3];

        return scene;
    }

    #region input configuration

    private void ConfigureKeyboardInput()
    {
        Engine.Instance.InitializeCodeBrixKeyboardAdapter(RenderSurface);
        Engine.Instance.Input.KeyboardEventPoller.KeyDown += KeyboardEventPoller_KeyDown;
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.W, "W");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.A, "A");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.S, "S");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.D, "D");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.Left, "Left");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.Right, "Right");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.Up, "Up");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.Down, "Down");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.PageUp, "PageUp");
        Engine.Instance.Input.KeyboardEventPoller.StartMonitoringKey((int)VirtualKey.PageDown, "PageDown");
    }

    private void KeyboardEventPoller_KeyDown(KeyDownEventArgs args)
    {
        var camera = RenderSurface.Host.ViewManager.Views[0].Camera;
        var curPos = camera.PositionPx;
        var sprite = SpriteManager.Instance.GetSpriteByID("rooster_1");

        // Parse the received key string into the VirtualKey enum (case-insensitive)
        if (!Enum.TryParse<VirtualKey>(args.KeyConfig.Key, ignoreCase: true, out var key))
        {
            return;
        }

        switch (key)
        {
            case VirtualKey.W:
                camera.PanToOverDuration(new PointF(curPos.X, curPos.Y - 100), 1.5f);
                break;
            case VirtualKey.A:
                camera.PanToOverDuration(new PointF(curPos.X - 100, curPos.Y), 1.5f);
                break;
            case VirtualKey.S:
                camera.PanToOverDuration(new PointF(curPos.X, curPos.Y + 100), 1.5f);
                break;
            case VirtualKey.D:
                camera.PanToOverDuration(new PointF(curPos.X + 100, curPos.Y), 1.5f);
                break;
            case VirtualKey.Right:
                if (args.KeyAction == KeyAction.Released)
                    sprite.Movement.SetAcceleration(new Vector2(0, 0));
                else
                    sprite.Movement.SetAcceleration(new Vector2(2f, 0));

                sprite.Movement.SetLinearDamping(0.3f);
                break;
            case VirtualKey.Left:
                if (args.KeyAction == KeyAction.Released)
                    sprite.Movement.SetAcceleration(new Vector2(0, 0));
                else
                    sprite.Movement.SetAcceleration(new Vector2(-2f, 0));
                break;
            case VirtualKey.Up:
                if (args.KeyAction == KeyAction.Released)
                    sprite.Movement.SetAcceleration(new Vector2(0, 0));
                else
                    sprite.Movement.SetAcceleration(new Vector2(0, -2f));

                sprite.Movement.SetLinearDamping(0.3f);
                break;
            case VirtualKey.Down:
                if (args.KeyAction == KeyAction.Released)
                    sprite.Movement.SetAcceleration(new Vector2(0, 0));
                else
                    sprite.Movement.SetAcceleration(new Vector2(0, 2f));
                break;
            case VirtualKey.PageUp:
                sprite.ScaleBy(1.1f, 0.15f);
                break;
            case VirtualKey.PageDown:
                sprite.ScaleBy(0.9f, 0.15f);
                break;
            default:
                break;
        }
    }

    private void ConfigureMouseInput()
    {
        Engine.Instance.InitializeCodeBrixMouseAdapter(RenderSurface);
        Engine.Instance.Input.MouseEventPoller.MouseEvent += MouseEventPoller_MouseEvent;
        Engine.Instance.Input.MouseEventPoller.StartMonitoringMouse();
    }

    private void MouseEventPoller_MouseEvent(MouseEventArgs args)
    {
        var view = RenderSurface.Host.ViewManager.Views[0];
        var layer = Scene.SceneLayers[0];

        var screenPos = args.CurrentPosition;

        var worldFromScreen = view.ScreenPxToWorldPx(layer, screenPos);
        var gridFromScreen = view.ScreenPxToGrid(layer, screenPos);

        var cameraPos = view.Camera.PositionPx;
        var message =
            $"Mouse Pos (screen): {screenPos.X}, {screenPos.Y}\n" +
            $"World Pos (px): {worldFromScreen.X:F1}, {worldFromScreen.Y:F1}\n" +
            $"Grid coordinates: {gridFromScreen.X}, {gridFromScreen.Y}\n" +
            $"Camera Pos: (px): {cameraPos.X}, {cameraPos.Y}";
        _textBlockMouse?.SetText(message);

        foreach (SceneLayerTile tile in layer)
            tile.EnableFog = false;

        var pickedTile = layer[gridFromScreen];
        if (pickedTile is not null)
            pickedTile.EnableFog = true;

        ScrollWheelZoom(args, view, layer);

        if (args.ButtonStates[MouseButton.Left].JustPressed)
        {
            var pos = args.CurrentPosition;
            _clickEmitter.Position = new PointF(pos.X, pos.Y);
            _particleSurface.Burst(_clickEmitter, 80);
        }
    }

    private void ScrollWheelZoom(MouseEventArgs args, View view, SceneLayer layer)
    {
        if (args.ScrollDelta != 0)
        {
            var vp = view.Viewport;
            float currentZoom = vp.Zoom;
            float delta = args.ScrollDelta * 0.001f;

            float minZoom = 0.1f;
            float maxZoom = 8f;

            float targetZoom = Math.Clamp(currentZoom + delta, minZoom, maxZoom);

            view.ZoomAroundScreenPoint(layer, args.CurrentPosition, targetZoom, 0.75f);
        }
    }

    #endregion input configuration

    #region IDisposable support

    private bool disposedValue;

    /// <summary>
    /// Releases the unmanaged resources used by the game and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Engine.Instance.Input.KeyboardEventPoller.KeyDown -= KeyboardEventPoller_KeyDown;
                Engine.Instance.Input.MouseEventPoller.MouseEvent -= MouseEventPoller_MouseEvent;

                Engine.Instance.Stop();
                Engine.Instance.Dispose();
            }

            disposedValue = true;
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="CoordinateTestGame"/> class.
    /// </summary>
    ~CoordinateTestGame()
    {
        Dispose(disposing: false);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="CoordinateTestGame"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion IDisposable support
}
