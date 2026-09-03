using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using Platformer.Brix.Game.Art;
using SkiaSharp;
using Windows.System;

namespace Platformer.Brix.Game;

/// <summary>
/// Game host for the Platformer.Brix sample: a single-screen side-view platform game built
/// directly on the engine's public APIs. Uses <see cref="CodeBrixGameHost"/> with the
/// <see cref="GameSurfaceCanvas"/> CPU render surface, pinned to
/// <see cref="RenderWidth"/> x <see cref="RenderHeight"/>.
/// </summary>
/// <remarks>
/// The player runs with A/D or the left/right arrows and jumps with W, the up arrow or the space
/// bar. Collect every sun relic, then reach the red flag. R restarts, Esc raises
/// <see cref="ExitRequested"/>.
/// </remarks>
public sealed class PlatformerGameHost : CodeBrixGameHost
{
    /// <summary>The fixed engine render width, in pixels, this game is laid out for.</summary>
    public const int RenderWidth = 960;

    /// <summary>The fixed engine render height, in pixels, this game is laid out for.</summary>
    public const int RenderHeight = 576;

    private const int WorldColumns = 72;
    private const int WorldRows = 18;
    private const float RunSpeed = 7.5f;
    private const float Gravity = 30f;
    private const float JumpSpeed = 14f;
    private const float MaxFallSpeed = 18f;

    // The player's own collision profile, matching the group and interaction mask the demo this
    // sample is ported from assigned directly to the collider: the "Actors" group, colliding with
    // the fixed world tiles only.
    private const string PlayerCollisionProfile = "Player";

    private static readonly Vector2 SpawnPosition = new(2f, 15f);

    private readonly HashSet<VirtualKey> _keysDown = [];
    private readonly List<SceneLayerTile> _hazards = [];
    private readonly List<SceneLayerTile> _relics = [];
    private readonly List<ICollider> _groundProbeResults = [];

    private Tilesheet _tilesheet = null!;
    private SceneLayer _backgroundLayer = null!;
    private SceneLayer _worldLayer = null!;
    private SceneLayerTile _goal = null!;
    private Sprite _player = null!;
    private TextBlock _hudText = null!;
    private TextBlock _messageText = null!;

    private bool _jumpQueued;
    private bool _grounded;
    private bool _facingLeft;
    private bool _loggedFirstKey;
    private int _relicsCollected;
    private PlayState _gameState = PlayState.Playing;
    private string _lastHudText = string.Empty;
    private string _statusMessage = string.Empty;
    private DateTime _statusMessageExpiresUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformerGameHost"/> class.
    /// </summary>
    /// <param name="renderSurface">The render surface to draw into.</param>
    public PlatformerGameHost(GameSurfaceCanvas renderSurface)
        : base(renderSurface)
    {
    }

    /// <summary>
    /// Raised on the engine thread when the player presses Esc. The application closes its
    /// window in response; the game itself does nothing else.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>Gets the number of sun relics the player has collected in the current run.</summary>
    public int RelicsCollected => _relicsCollected;

    /// <summary>Gets the total number of sun relics in the level.</summary>
    public int RelicCount => _relics.Count;

    #region CodeBrixGameHost overrides

    /// <inheritdoc />
    protected override void LoadTilesheets()
    {
        _tilesheet = Engine.Managers.Tilesheets.LoadFromBitmap(
            "platformer",
            PlatformerArt.CreateTilesheetBitmap());

        _tilesheet.DefaultRegion.TileSize = new Size(
            PlatformerArt.TileSize,
            PlatformerArt.TileSize);
    }

    /// <inheritdoc />
    protected override Scene CreateInitialScene()
    {
        var scene = new Scene();

        // The fixed world tiles keep the standard "World" profile (the WorldStatic group,
        // colliding with actors); the player gets a profile of its own so it collides with the
        // world only.
        scene.CollisionProfiles.Define(
            PlayerCollisionProfile,
            collisionGroup: "Actors",
            collidesWith: ["WorldStatic"]);

        _backgroundLayer = scene.AddLayer(
            WorldColumns,
            WorldRows,
            PlatformerArt.TileSize,
            PlatformerArt.TileSize,
            zOrder: 0,
            parallax: 0.35f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        _worldLayer = scene.AddLayer(
            WorldColumns,
            WorldRows,
            PlatformerArt.TileSize,
            PlatformerArt.TileSize,
            zOrder: 10,
            parallax: 1f,
            coordinateSystem: CoordinateSystemTypes.Orthogonal);

        BuildBackground();
        BuildLevel();

        return scene;
    }

    /// <inheritdoc />
    protected override void OnSceneBound()
    {
        var backbuffer = RenderSurface.Host.Backbuffer;
        backbuffer.ClearColor = new SKColor(110, 190, 235);

        // Pixel art: never smooth the scaled-up tiles.
        if (backbuffer is BitmapBackbuffer bitmapBackbuffer)
            bitmapBackbuffer.FilterQuality = ImageFilterQuality.None;

        var view = RenderSurface.Host.ViewManager.Views[0];
        view.Camera.WorldBoundsPx = Scene!.GetWorldBoundsPx();
        view.Camera.SnapTo(PointF.Empty);
    }

    /// <inheritdoc />
    protected override void CreateSprites()
    {
        _player = Engine.Managers.Sprites.CreateSprite(
            _worldLayer,
            _tilesheet[PlatformerArt.PlayerRightFrame, 0],
            "player",
            PlayerCollisionProfile);

        _player.SetPosition(SpawnPosition);
        _player.Visible = true;
        _player.ZOrder = 20;
        _player.AdjustCollisionArea = new CollisionAdjust(
            top: 4,
            bottom: 0,
            left: 7,
            right: 7);

        // Blocking is the engine's "solid, collisions on" state: it gives the collider the Solid
        // response type and registers it with the layer's collider registry.
        _player.CollisionType = TileCollisionType.Blocking;

        _player.Movement.SetAcceleration(new Vector2(0f, Gravity));

        var camera = RenderSurface.Host.ViewManager.Views[0].Camera;
        camera.DeadZonePx = new Rectangle(360, 0, 240, RenderHeight);
        camera.FollowCenteredX(_player, speed: 9f);
    }

    /// <inheritdoc />
    protected override void CreateDirectDrawings()
    {
        var view = RenderSurface.Host.ViewManager.Views[0];

        var panel = new DirectRectangle(
                Color.FromArgb(210, 28, 39, 51),
                RenderSurface.Host,
                view,
                new Rectangle(12, 12, 474, 68),
                "hud-panel")
            .SetFilled(true)
            .SetBorderColor(Color.FromArgb(235, 236, 223, 186))
            .SetStrokeWidth(2f)
            .SetCornerRadius(8f);
        panel.ZOrder = 1000;

        _hudText = new TextBlock(
                RenderSurface.Host,
                view,
                new Rectangle(26, 22, 446, 48),
                "hud-text")
            .SetFont(SKTypeface.Default, 17f)
            .SetColors(SKColors.White, SKColors.Transparent)
            .SetAlignment(SKTextAlign.Left, TextBlock.VerticalAlign.Center)
            .EnableWrapping(false)
            .UseShadow();
        _hudText.ZOrder = 1001;

        _messageText = new TextBlock(
                RenderSurface.Host,
                view,
                new Rectangle(150, 220, 660, 136),
                "status-text")
            .SetFont(SKTypeface.Default, 30f, minSize: 20f)
            .SetColors(SKColors.White, new SKColor(28, 39, 51, 210))
            .SetAlignment(SKTextAlign.Center, TextBlock.VerticalAlign.Center)
            .SetMaxLines(3)
            .UseShadow()
            .UseOutline();
        _messageText.HorizontalPadding = 18f;
        _messageText.VerticalPadding = 12f;
        _messageText.ZOrder = 1100;
        _messageText.Visible = false;

        UpdateHud(force: true);
        ShowTemporaryMessage("Collect every sun relic, then reach the red flag.", 4d);
    }

    /// <inheritdoc />
    protected override void OnKeyboardAdapterInitialized()
    {
        var keyboard = Engine.Input.KeyboardEventPoller;

        if (keyboard is null)
            return;

        keyboard.KeyDown += OnKeyDown;

        foreach (var key in MonitoredKeys)
            keyboard.StartMonitoringKey((int)key, key.ToString());
    }

    /// <inheritdoc />
    protected override void OnEngineInitialized()
    {
        Engine.Configuration.TargetFPS = 60;
        Engine.BeforeBackgroundTasksExecute += BeforeBackgroundTasksExecute;
        Engine.AfterBackgroundTasksExecute += AfterBackgroundTasksExecute;
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Console.WriteLine(
            $"[Platformer.Brix] {RenderWidth}x{RenderHeight}, {WorldColumns}x{WorldRows} tiles, " +
            $"{_relics.Count} relics — A/D or arrows move, W/Up/Space jump, R restart, Esc quit.");
    }

    /// <inheritdoc />
    protected override void UnhookEvents()
    {
        if (Engine.Input.KeyboardEventPoller is not null)
            Engine.Input.KeyboardEventPoller.KeyDown -= OnKeyDown;

        Engine.BeforeBackgroundTasksExecute -= BeforeBackgroundTasksExecute;
        Engine.AfterBackgroundTasksExecute -= AfterBackgroundTasksExecute;
    }

    #endregion CodeBrixGameHost overrides

    #region private methods

    private static VirtualKey[] MonitoredKeys =>
    [
        VirtualKey.A,
        VirtualKey.D,
        VirtualKey.Left,
        VirtualKey.Right,
        VirtualKey.W,
        VirtualKey.Up,
        VirtualKey.Space,
        VirtualKey.R,
        VirtualKey.Escape
    ];

    private void BuildBackground()
    {
        foreach (var (x, y) in new[]
                 {
                     (3, 3), (11, 5), (21, 2), (32, 4),
                     (43, 2), (54, 5), (63, 3), (70, 1)
                 })
        {
            _backgroundLayer[x, y]!.CurrentFrame = _tilesheet[PlatformerArt.CloudFrame, 0];
        }
    }

    private void BuildLevel()
    {
        var pitColumns = new HashSet<int>
        {
            14, 15, 16,
            36, 37, 38,
            55, 56, 57
        };

        for (var x = 0; x < WorldColumns; x++)
        {
            if (pitColumns.Contains(x))
                continue;

            SetSolidTile(x, 16, PlatformerArt.GrassFrame);
            SetSolidTile(x, 17, PlatformerArt.GrassFrame);
        }

        AddPlatform(5, 9, 13);
        AddPlatform(11, 13, 11);
        AddPlatform(18, 24, 14);
        AddPlatform(26, 31, 12);
        AddPlatform(33, 35, 13);
        AddPlatform(40, 47, 14);
        AddPlatform(49, 54, 12);
        AddPlatform(59, 64, 13);
        AddPlatform(66, 71, 10);

        AddHazard(22, 13);
        AddHazard(29, 11);
        AddHazard(44, 13);
        AddHazard(62, 12);

        AddRelic(7, 12);
        AddRelic(12, 10);
        AddRelic(28, 11);
        AddRelic(51, 11);
        AddRelic(68, 9);

        _goal = _worldLayer[70, 9]!;
        _goal.CurrentFrame = _tilesheet[PlatformerArt.GoalFrame, 0];
    }

    private void AddPlatform(int fromX, int toX, int y)
    {
        for (var x = fromX; x <= toX; x++)
            SetSolidTile(x, y, PlatformerArt.StoneFrame);
    }

    private void SetSolidTile(int x, int y, int frame)
    {
        var tile = _worldLayer[x, y]!;
        tile.CurrentFrame = _tilesheet[frame, 0];

        // The layer already applies the "World" profile to every fixed tile; naming it here keeps
        // the tile's group and interaction mask explicit at the point the tile becomes solid.
        tile.SetCollisionProfile(CollisionProfileNames.World);
        tile.CollisionType = TileCollisionType.Blocking;
    }

    private void AddHazard(int x, int y)
    {
        var tile = _worldLayer[x, y]!;
        tile.CurrentFrame = _tilesheet[PlatformerArt.SpikeFrame, 0];
        tile.AdjustCollisionArea = new CollisionAdjust(
            top: 14,
            bottom: 1,
            left: 3,
            right: 3);
        _hazards.Add(tile);
    }

    private void AddRelic(int x, int y)
    {
        var tile = _worldLayer[x, y]!;
        tile.CurrentFrame = _tilesheet[PlatformerArt.RelicFrame, 0];
        tile.AdjustCollisionArea = new CollisionAdjust(
            top: 5,
            bottom: 5,
            left: 5,
            right: 5);
        _relics.Add(tile);
    }

    private void OnKeyDown(KeyDownEventArgs args)
    {
        var key = (VirtualKey)args.KeyCode;

        // Keys only reach the engine's poller while the game surface holds keyboard focus, so say
        // once — on the console — that the input path is live.
        if (!_loggedFirstKey)
        {
            _loggedFirstKey = true;
            Console.WriteLine($"[Platformer.Brix] keyboard input reached the game (first key: {key}).");
        }

        switch (args.KeyAction)
        {
            case KeyAction.Pressed:
                _keysDown.Add(key);

                if (key is VirtualKey.Space or VirtualKey.W or VirtualKey.Up)
                    _jumpQueued = true;

                if (key == VirtualKey.R)
                    RestartGame();

                if (key == VirtualKey.Escape)
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                break;

            case KeyAction.Released:
                _keysDown.Remove(key);
                break;
        }
    }

    private void BeforeBackgroundTasksExecute()
    {
        if (_gameState != PlayState.Playing)
            return;

        var velocity = _player.Movement.MovementState.Velocity;
        var moveLeft = _keysDown.Contains(VirtualKey.A) || _keysDown.Contains(VirtualKey.Left);
        var moveRight = _keysDown.Contains(VirtualKey.D) || _keysDown.Contains(VirtualKey.Right);

        var horizontal = moveLeft == moveRight
            ? 0f
            : moveLeft ? -RunSpeed : RunSpeed;

        if (horizontal < 0f && !_facingLeft)
            SetPlayerFacing(left: true);
        else if (horizontal > 0f && _facingLeft)
            SetPlayerFacing(left: false);

        var vertical = Math.Min(velocity.Y, MaxFallSpeed);

        if (_jumpQueued && _grounded)
        {
            vertical = -JumpSpeed;
            _grounded = false;
        }

        _jumpQueued = false;
        _player.Movement.SetVelocity(new Vector2(horizontal, vertical));
        _player.Movement.SetAcceleration(new Vector2(0f, Gravity));
    }

    private void AfterBackgroundTasksExecute()
    {
        if (_gameState != PlayState.Playing)
            return;

        _grounded = IsStandingOnSolid();
        CollectRelics();

        if (_hazards.Any(hazard =>
                hazard.Visible &&
                _player.CollisionArea.IntersectsWith(hazard.CollisionArea)))
        {
            Respawn("Ouch. Spikes remain undefeated.");
            return;
        }

        if (_player.GetPosition().Y > WorldRows + 2)
        {
            Respawn("Mind the gap.");
            return;
        }

        if (_player.CollisionArea.IntersectsWith(_goal.CollisionArea))
        {
            if (_relicsCollected == _relics.Count)
                WinGame();
            else
                ShowTemporaryMessage(
                    $"The flag is locked: {_relics.Count - _relicsCollected} relic(s) remain.",
                    2d);
        }

        UpdateMessageVisibility();
        UpdateHud();
    }

    private bool IsStandingOnSolid()
    {
        var area = _player.CollisionArea;
        var playerCollider = _player.Collider!;
        var footProbe = new Aabb(
            area.Left + 3,
            area.Bottom,
            area.Right - 3,
            area.Bottom + 2);

        _worldLayer.ColliderRegistry.QueryAabb(
            footProbe,
            playerCollider.CollisionGroup,
            playerCollider.CollidesWith,
            _groundProbeResults,
            ignore: playerCollider);

        return _groundProbeResults.Any(collider =>
            collider.IsStatic &&
            collider.ResponseType == CollisionResponseType.Solid &&
            collider.BoundsWorldPx.MinY >= area.Bottom - 1);
    }

    private void CollectRelics()
    {
        foreach (var relic in _relics)
        {
            if (!relic.Visible || !_player.CollisionArea.IntersectsWith(relic.CollisionArea))
                continue;

            relic.Visible = false;
            _relicsCollected++;
            ShowTemporaryMessage("Sun relic recovered.", 1.25d);
        }
    }

    private void SetPlayerFacing(bool left)
    {
        _facingLeft = left;
        _player.CurrentFrame = _tilesheet[
            left ? PlatformerArt.PlayerLeftFrame : PlatformerArt.PlayerRightFrame,
            0];
    }

    private void Respawn(string message)
    {
        _player.SetPosition(SpawnPosition);
        _player.Movement.SetVelocity(Vector2.Zero);
        _player.Movement.SetAcceleration(new Vector2(0f, Gravity));
        _grounded = false;
        ShowTemporaryMessage(message, 2d);
    }

    private void WinGame()
    {
        _gameState = PlayState.Won;
        _keysDown.Clear();
        _player.Movement.StopAllMovement();
        _messageText.SetText("YOU FOUND THE OLD ROAD\nPress R to play again");
        _messageText.Visible = true;
        UpdateHud(force: true);

        Console.WriteLine("[Platformer.Brix] the old road is found — press R to play again.");
    }

    private void RestartGame()
    {
        _gameState = PlayState.Playing;
        _relicsCollected = 0;

        foreach (var relic in _relics)
            relic.Visible = true;

        Respawn("The road begins again.");
        UpdateHud(force: true);
    }

    private void ShowTemporaryMessage(string message, double seconds)
    {
        if (_gameState != PlayState.Playing)
            return;

        _statusMessage = message;
        _statusMessageExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
        _messageText.SetText(message);
        _messageText.Visible = true;

        Console.WriteLine($"[Platformer.Brix] {message}");
    }

    private void UpdateMessageVisibility()
    {
        if (string.IsNullOrEmpty(_statusMessage) || DateTime.UtcNow < _statusMessageExpiresUtc)
            return;

        _statusMessage = string.Empty;
        _messageText.Visible = false;
    }

    private void UpdateHud(bool force = false)
    {
        var state = _gameState == PlayState.Won ? "Road found" : "Find the old road";
        var hud =
            $"Relics {_relicsCollected}/{_relics.Count}   {state}\n" +
            "A/D or ←/→ move   W/↑/Space jump   R restart   Esc quit";

        if (!force && string.Equals(hud, _lastHudText, StringComparison.Ordinal))
            return;

        _lastHudText = hud;
        _hudText.SetText(hud);
    }

    #endregion private methods

    private enum PlayState
    {
        Playing,
        Won
    }
}
