================================================================================
AGENT-README: CodeBrix.Platform.GameEngine
A Comprehensive Guide for AI Coding Agents (and human developers)
================================================================================

OVERVIEW
--------------------------------------------------------------------------------
CodeBrix.Platform.GameEngine is a fully managed, cross-platform 2D / 2.5D game
engine for .NET, built on SkiaSharp. It provides tile maps, tilesheets, sprites,
layered scenes, camera/view systems, animation, physics/collision, input, audio,
a save/load system, and a global pause system.

The repository contains TWO libraries that mirror the classic core/host split:

  * CodeBrix.Platform.GameEngine        -- the platform-agnostic engine CORE.
        No UI-framework dependency; headless-testable. Its rendering seam is a
        SkiaSharp SKImage plus the RenderSurfaceAdapterBase abstraction.

  * CodeBrix.Platform.GameEngine.Host   -- the HOST layer that runs the engine
        on CodeBrix.Platform (all six heads: Win32-Skia, WPF-Skia, X11, Wayland,
        Frame Buffer, macOS). Contains the CPU (Tier A) and GPU (Tier B) render-
        surface adapters, pointer/keyboard input adapters, a UI dispatcher, and
        the game-host base classes games derive from.

The engine core is a vendored port of the open-source Gondwana game engine
version 2.5.0 (MIT, (c) 2025 Michael Adkins). See THIRD-PARTY-NOTICES.txt for
more info.

INSTALLATION
--------------------------------------------------------------------------------
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.MitLicenseForever

This single package bundles BOTH assemblies -- the engine core
(CodeBrix.Platform.GameEngine.dll) and the host layer
(CodeBrix.Platform.GameEngine.Host.dll) -- so one reference gives you
everything. There is no separate .Host package.

The namespaces are CodeBrix.Platform.GameEngine[.*] and
CodeBrix.Platform.GameEngine.Host[.*] (WITHOUT the license suffix).

Target framework: .NET 10.0 or higher.

KEY NAMESPACES
--------------------------------------------------------------------------------
    using CodeBrix.Platform.GameEngine;                 // Engine, EngineState, dispatchers
    using CodeBrix.Platform.GameEngine.Assets;          // AssetsFile
    using CodeBrix.Platform.GameEngine.Audio;           // AudioSystem, SoundChannel, streams
    using CodeBrix.Platform.GameEngine.Configuration;   // EngineConfiguration[File]
    using CodeBrix.Platform.GameEngine.Drawing;         // Tile, ImageFilterQuality, SvgResource
    using CodeBrix.Platform.GameEngine.Drawing.Sprites; // Sprite, CompositeSprite
    using CodeBrix.Platform.GameEngine.Drawing.Direct;  // DirectImage, TextBlock, particles...
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets; // Tilesheet, TilesheetRegistry
    using CodeBrix.Platform.GameEngine.Drawing.Animation;  // Cycle, FrameSequence
    using CodeBrix.Platform.GameEngine.Rendering;       // render-surface hosts, backbuffers,
                                                        //   PixelFramePresenter
    using CodeBrix.Platform.GameEngine.Rendering.Views; // ViewManager, View, Camera, Viewport
    using CodeBrix.Platform.GameEngine.Scenes;          // Scene, SceneLayer, SceneLayerTile
    using CodeBrix.Platform.GameEngine.Physics;         // movement, easing, collisions
    using CodeBrix.Platform.GameEngine.Input;           // pollers, InputPump
    using CodeBrix.Platform.GameEngine.Timers;          // Timer, HighResTimer, FixedRateGameLoop
    using CodeBrix.Platform.GameEngine.Extensibility;   // IEnginePlugin
    using CodeBrix.Platform.GameEngine.Host;            // EngineExtensions (adapter wiring)
    using CodeBrix.Platform.GameEngine.Host.Hosting;    // CodeBrixGameHost,
                                                        //   SoftwareRenderedGameHostBase
    using CodeBrix.Platform.GameEngine.Host.Rendering;  // GameSurfaceCanvas
    using CodeBrix.Platform.GameEngine.Host.Input.*;    // CodeBrix input adapters

THE TWO HOSTING MODES (choose one — they are mutually exclusive)
--------------------------------------------------------------------------------
Every game runs in exactly ONE of two modes, per GameSurfaceCanvas:

MODE A: THE ENGINE-CYCLE (SCENE PIPELINE) MODE
  The engine owns the loop. Engine.Start() spins a dedicated background thread
  that repeatedly runs one "cycle": input polling, timers, animation, sprite
  movement, collision resolution, camera updates, then (throttled to TargetFPS)
  rendering every registered render surface and presenting it. The game
  describes WHAT exists (scenes, layers, tiles, sprites, direct drawings) and
  reacts to events; the engine decides WHEN everything runs.
  Choose this for tile/sprite games: it gives you the scene graph, cameras,
  collision, animation cycles, and save/load for free.

MODE B: THE SOFTWARE-RENDERED (FRAMEBUFFER POLLING) MODE
  The game owns the loop. A FixedRateGameLoop thread ticks at a fixed rate
  (35 Hz, 70 Hz, ...); each tic the game polls input (InputPump.PollNow),
  advances its own game state, renders a whole CPU frame into a byte buffer,
  and hands it to a PixelFramePresenter (latest-frame-wins presentation).
  The engine cycle NEVER runs; the scene/sprite pipeline is never created.
  Choose this for retro-style games that render whole frames themselves
  (DOS-era ports, demoscene-style effects, emulators).

The mode split is enforced per canvas: GameSurfaceCanvas.Host (scene pipeline)
and GameSurfaceCanvas.UsePixelFramePresenter() are mutually exclusive — touching
one after the other throws. Likewise InputPump.PollNow() throws while the
engine loop is running (double-pumping would corrupt poller state).

ENGINE LIFECYCLE AND THE CYCLE, IN DETAIL (Mode A)
--------------------------------------------------------------------------------
The Engine is a thread-safe singleton: Engine.Instance. Lifecycle:

    Engine.Instance.Initialize(...);   // optional; Start() calls it if needed
    Engine.Instance.Start(syncContext); // spins the cycle thread
    Engine.Instance.Pause();            // global pause (see PAUSE section)
    Engine.Instance.Resume();
    Engine.Instance.Stop();             // halts the loop; engine reusable
    Engine.Instance.Dispose();          // full teardown; engine NOT reusable

Start() must receive the UI thread's SynchronizationContext (the parameterless
overload captures SynchronizationContext.Current, so call it ON the UI thread).
For single-threaded runtimes (WASM), StartTimerDriven(uiContext) + a platform
timer calling Engine.Instance.Tick() replaces the background thread.

One cycle executes, in order (engine plugins get InvokePreCycle /
InvokePreFrameRender / InvokePostFrameRender / InvokePostCycle hooks around
the same points — see Extensibility/IEnginePlugin):
   1. EngineDispatcher.Drain()          -- runs actions posted to the engine thread
   2. BeforeBackgroundTasksExecute event
   3. PreCycle Timer events             -- Timer.RaiseTimerEvents(TimerType.PreCycle)
   4. Input pollers                     -- keyboard, mouse, touch, gamepad events fire HERE
   5. Animator frame advancement        -- for every Tile in Tile.TilesAnimating
   6. Sprite movement                   -- SpriteManager.MoveSprites (paths, easing)
   7. Collision resolution              -- per scene layer
   8. Camera updates                    -- per render surface ViewManager
   9. AfterBackgroundTasksExecute event
  10. THROTTLE CHECK: if TargetFPS interval has not elapsed, skip 11-15
  11. BeforeFrameRender event
  12. DirectDrawingManager.UpdateAll    -- immediate-mode drawable state updates
  13. Render + present each non-GPU render surface
  14. Gamepad state update; AfterFrameRender event
  15. PostCycle Timer events
  16. CPS/FPS sampling (CPSCalculated event, posted to the UI thread)

Steps 1-9 run EVERY cycle (unthrottled — cycles run as fast as the thread
spins, yielding between cycles); steps 11-15 run at most TargetFPS times per
second. TargetFPS <= 0 renders unbounded. This split is why input feels
responsive even at low frame rates — and why per-cycle event handlers must be
CHEAP (they run thousands of times per second).

THE THREADING MODEL — WHAT RUNS WHERE
--------------------------------------------------------------------------------
There are exactly three thread contexts a Mode-A game touches:

  ENGINE THREAD (the cycle thread)
    Runs: every step of the cycle above, including ALL engine events
    (Before/AfterBackgroundTasksExecute, Before/AfterFrameRender, Timer.Tick,
    input poller events, sprite/collision callbacks) and the Paused event.
    Game-state mutation belongs here. To get onto it from elsewhere:
        Engine.Instance.EngineDispatcher.Post(() => { ...game state... });
    Posted actions run at the top of the next cycle (step 1). Posting FROM the
    engine thread executes inline.

  UI THREAD
    Runs: XAML layout/input, GameSurfaceCanvas painting, CPSCalculated,
    PreInitialization/PostInitialization/InitializationComplete, Disposing/
    Disposed (all posted via the UiDispatcher captured at Start). To get onto
    it from the engine thread:
        Engine.Instance.UiDispatcher?.Post(() => { ...UI... });
    NEVER touch XAML elements from the engine thread directly.

  AUDIO CALLBACK THREAD (only if the game uses streaming audio)
    Runs: StreamingAudioSource fill callbacks. Must be fast, allocation-free,
    and never block. Do not touch game state or UI from it; hand it data
    through lock-free fields.

Mode B is simpler: the GAME-LOOP THREAD (FixedRateGameLoop's dedicated thread)
replaces the engine thread — OnTic/OnRenderFrame and all poller events raised
by InputPump.PollNow run there — and the same UI-thread and audio-thread rules
apply. IKeyboardAdapter.IsDown(keyCode) is the one deliberate exception: it is
lock-free and valid from ANY thread at any time.

THE GLOBAL PAUSE SYSTEM
--------------------------------------------------------------------------------
One call pauses everything, in both modes; a matching call resumes:

    Engine.Instance.Pause();     // idempotent, thread-safe
    Engine.Instance.Resume();
    Engine.Instance.IsPaused     // logical pause state
    Engine.Instance.Paused       // event: the "do this when paused" hook
    Engine.Instance.Resumed      // event: the "do this when resumed" hook

What Pause() guarantees:
  * The cycle (Mode A) or tic (Mode B) in progress completes — never torn.
  * At most one further frame is rendered (Mode A renders one final frame
    AFTER the Paused event, ignoring the TargetFPS throttle, so a pause
    overlay added by the handler reaches the screen).
  * Then the loop PARKS at ~zero CPU: no input polling, no timers, no
    movement, no collisions, no rendering. A minimized game costs nothing.
  * Pause() blocks until the engine is quiescent (bounded by ~one cycle/tic),
    EXCEPT when called from the engine/game-loop thread itself — then it
    returns immediately and the park happens as the current cycle/tic ends.
  * Playing audio is suspended (Configuration.PauseSuspendsAudio, default
    true), EXCEPT short fire-and-forget sound effects — a playing voice whose
    clip is no longer than Configuration.PauseShortSoundEffectSeconds
    (default 1.0 s) rings out naturally and is not resumed later. Endless and
    looping material always suspends. Per-voice override:
    SuspendOnEnginePause on SoundChannel / StreamingAudioSource /
    AudioResource (true = always suspend, false = never, null = automatic).
  * Voices the game paused itself stay paused across Resume() — the engine
    resumes exactly the set it suspended.

The last-frame-before-pause snapshot:
  Pause() captures what the player was seeing BEFORE raising Paused:
    Engine.Instance.LastFrameBeforePause            (SKImage, global)
    RenderSurfaceHostBase.LastFrameBeforePause      (per scene surface)
    PixelFramePresenter.LastFrameBeforePause        (per presenter)
  Both the hosting application and the game can read it while paused — e.g. a
  Paused handler can build a dimmed "GAME PAUSED" screen from it. The image is
  owned by the engine/surface and stays valid through the resume, until the
  NEXT Pause() replaces it; copy it to keep it longer. GPU (GL-thread, Tier B)
  surfaces are captured too, from the adapter's copy of the most recently
  presented frame (null only if the surface never presented a frame).

  Skia-free access — LastFrameBeforePauseAsRgba(out width, out height) on all
  three (Engine, RenderSurfaceHostBase, PixelFramePresenter) returns the
  snapshot as a raw RGBA8888 byte[] (4 bytes/pixel in R,G,B,A memory order,
  row-major, unpremultiplied, width*height*4 bytes), or null when nothing was
  captured. That layout loads straight into imaging libraries — saving the
  screenshot as a PNG with CodeBrix.Imaging (NuGet package ID:
  CodeBrix.Imaging.ApacheLicenseForever) takes no translation code:

      var rgba = Engine.Instance.LastFrameBeforePauseAsRgba(out var w, out var h);
      if (rgba is not null)
          Image.LoadPixelData<Rgba32>(rgba, w, h).SaveAsPng("pause.png");

  Each call converts and copies afresh — hold the result, don't re-call per
  frame.

The Paused event contract (the "do-this-if-Pause()-is-issued" hook):
  * Raised once per pause episode, AFTER game execution is quiescent (loops
    parked / between cycles on the engine thread) and AFTER the snapshot and
    audio suspend. Safe place for a save-game routine — nothing races you.
  * Mode A: mutate the scene here (add a "PAUSED" TextBlock, a dimmed
    DirectImage of LastFrameBeforePause, hide sprites, ...) and the final
    frame renders it. Tear the overlay down in Resumed.
  * Mode B: present a pause frame directly — Presenter.PresentFrame stays
    available while the loop is parked (presentation is passive).
  * Both host base classes surface the events as overridables instead:
    OnEnginePaused() / OnEngineResumed() on GameHostBase and
    SoftwareRenderedGameHostBase.

Resume() and time:
  Every time baseline in the engine (repeating Timers, sprite movement,
  animators, direct drawings, particles, the cycle clocks) is shifted past the
  paused interval BEFORE the loops wake — the pause is invisible to game time.
  No sprite teleports, no timer bursts, no animation churn.
  TotalTicksEngineRunning / TotalSecondsEngineRunning EXCLUDE paused time (the
  value holds still while paused). FixedRateGameLoop re-baselines its schedule
  on resume: no catch-up burst, nothing counted in DroppedTics.

The one rule games must respect:
  Engine input pollers DO NOT RUN while paused, so a game cannot un-pause
  itself through engine input. Wire the resume trigger at the hosting
  application's UI layer — the window's restore/visibility event, a UI-level
  KeyDown, or a UI-level PointerPressed (see the ParticleTest campfire toggle
  for a worked example: samples/ParticleTest/src/libs/ParticleTest.Game/
  ParticleTestGame.cs, OnCanvasPointerPressed). The obvious application wiring:
  minimize -> Engine.Instance.Pause(), restore -> Engine.Instance.Resume().

Also of note:
  * Pausing BEFORE Start() is valid: the loop starts parked (minimized-at-
    launch). FixedRateGameLoop with PauseWithEngine likewise starts parked.
  * Pause is orthogonal to Stop: a paused engine still reports IsRunning.
    Stop()/Dispose() while paused work (the park is woken to observe the stop).
  * A game-owned FixedRateGameLoop opts into the global pause with
    loop.PauseWithEngine = true (SoftwareRenderedGameHostBase does this for
    its loop automatically). loop.Pause()/Resume() remain independent: a loop
    the game paused itself stays paused across a global Resume().

GETTING ON SCREEN: GameSurfaceCanvas (both modes)
--------------------------------------------------------------------------------
The one control games render into is GameSurfaceCanvas (Host library), placed
in a XAML page:

    <game:GameSurfaceCanvas x:Name="GameCanvas" />

Key members:
    FirstStarted            -- fires ONCE, at the first non-zero layout size.
                               START YOUR GAME FROM THIS EVENT; before it, the
                               surface has no real size.
    SetRenderResolution(w,h)-- pins the engine render resolution; frames are
                               aspect-fit letterboxed into the control. Call
                               BEFORE first access to Host. Non-positive values
                               track the control size instead.
    UseGpuRendering         -- opt-in to Tier B (GPU) rendering; set BEFORE
                               first access to Host, like SetRenderResolution.
                               Default false = Tier A (CPU). See RENDER TIERS.
    Host                    -- the RenderSurfaceHost the engine renders into
                               (Mode A): a RenderSurfaceHost<BackbufferBase>
                               whose backbuffer is a BitmapBackbuffer (Tier A)
                               or GpuBackbuffer (Tier B). Bind(scene) connects
                               a scene.
    UsePixelFramePresenter()-- switches the canvas to presenter mode (Mode B).
    EnsureFocus()           -- makes the canvas reliably keyboard-focusable:
                               tab stop + focus-on-load + refocus-on-press.
    WindowToBuffer/BufferToWindow -- pointer coordinate mapping across the
                               letterbox (presenter mode).
    SetPointerCursorHidden(b)-- hide/restore the cursor over the canvas.

During a live window resize the canvas suppresses engine presents and re-blits
the last frame at the new size; live presenting resumes ~500 ms after the size
settles. Do not fight this by forcing refreshes from resize handlers.

RENDER TIERS: TIER A (CPU, default) vs TIER B (GPU, opt-in) — Mode A only
--------------------------------------------------------------------------------
    GameCanvas.UseGpuRendering = true;      // BEFORE first access to Host
    GameCanvas.SetRenderResolution(1280, 720);   // optional, works on both tiers

  TIER A (default): the engine rasterises the scene on the CPU into a
  BitmapBackbuffer on the engine thread; the adapter
  (CodeBrixPlatformBitmapRenderSurfaceAdapter) blits it to the canvas.
  Dirty-rectangle present optimisation applies. Right for most 2D tile games.

  TIER B (opt-in): the scene is rasterised BY THE GPU into a GpuBackbuffer
  through an offscreen OpenGL/GLES context (CodeBrix.Platform.Graphics3DGL),
  then read back to CPU pixels once per frame and presented through the same
  canvas path — letterboxing, resize behaviour, SetRenderResolution, and input
  mapping are identical across tiers. The engine loop never touches GL-thread
  surfaces; the adapter (CodeBrixPlatformGpuRenderSurfaceAdapter) drives one GL
  frame on the UI thread per engine frame notification (TargetFPS cadence,
  coalesced latest-wins). The full surface is re-rendered every frame (no
  dirty-rectangle path on GPU).
  * Worth it when GPU raster beats CPU raster for the scene: heavy blending,
    scaling/rotation, full-surface shader effects (SKRuntimeEffect/SkSL runs
    ON the GPU — the GpuRender sample's plasma runs ~60 FPS on Tier B vs
    single-digit FPS on Tier A at 1024x640). A plain tile blit may not benefit.
  * EngineConfiguration.MsaaSampleCount applies (surface re-init on change);
    VSync has no effect on this adapter (no swap chain — pacing comes from
    TargetFPS); CPSCalculated reports the actual rendered GPU FPS (GpuFps).
  * RenderBackbufferPostScene and custom DirectDrawingBase.OnDraw run on the
    GL (UI) thread with the GRContext current on Tier B — never marshal that
    canvas elsewhere; keep OnDraw a pure function of engine time/game state.
  * Pause: fully supported — rendering parks, LastFrameBeforePause is captured
    (from the adapter's latest presented frame), and one paused-overlay frame
    is rendered after the Paused handlers run, same as Tier A.
  * On a head without OpenGL the adapter logs one warning and falls back to
    CPU-rendering the GpuBackbuffer's fallback surface (the game still runs);
    IsGpuInitialized on the adapter reports the outcome.
  * Tier is fixed once Host is created; presenter mode (Mode B) is CPU-only.

MODE A WALKTHROUGH 1: DERIVING FROM CodeBrixGameHost (recommended)
--------------------------------------------------------------------------------
CodeBrixGameHost (Host library) wires the canvas, input adapters, scene
binding, and engine start; the game overrides content hooks. Initialize() runs
this fixed sequence — override what you need, in the order it fires:

    OnInitializing
    ConfigurePlatform  -> OnConfigurePlatform
    ConfigureInput     -> keyboard/mouse/touch adapters wired to the canvas,
                          OnKeyboardAdapterInitialized / OnMouseAdapter... /
                          OnConfigureGamepads / OnTouchAdapterInitialized
    LoadAssets                       } "load content"
    LoadTilesheets                   }
    LoadAnimationCycles              }
    CreateInitialScene (return your Scene)
    CreateInitialViews
    OnSceneGraphCreated
    BindScene (canvas.Host.Bind(Scene)) -> OnSceneBound
    CreateSprites
    CreateDirectDrawings
    InitializeEngine   -> OnEngineInitialized   (engine initialized, not started)
    StartEngine        -> OnEngineStarted       (cycle thread now running)
    OnInitialized

Minimal game skeleton (the Spot.Brix sample is the full worked example):

    public sealed class MyGameHost : CodeBrixGameHost
    {
        public MyGameHost(GameSurfaceCanvas canvas) : base(canvas) { }

        protected override void LoadAssets() { /* AudioResourceManager, AssetsFile... */ }
        protected override void LoadTilesheets() { /* TilesheetFactory... */ }
        protected override void LoadAnimationCycles() { /* Cycle definitions */ }
        protected override Scene CreateInitialScene() { /* build + return scene */ }
        protected override void CreateInitialViews()
            => RenderSurface.Host.ViewManager.ConfigureSingleFullView();
        protected override void CreateSprites() { /* Sprite instances */ }
        protected override void OnEngineStarted()
            => Engine.Configuration.TargetFPS = 60;
        protected override void OnEnginePaused() { /* save game / pause screen */ }
        protected override void OnEngineResumed() { /* tear down pause screen */ }
        protected override void OnRenderSurfaceResized(int w, int h)
            { /* reposition HUD overlays pinned to edges */ }
    }

Page wiring (identical in every sample):

    GameCanvas.FirstStarted += (_, _) =>
    {
        GameCanvas.SetRenderResolution(1280, 720);   // optional
        _host = new MyGameHost(GameCanvas);
        _host.Initialize(logLevel: LogLevel.Warning);
    };

Dispose() the host when the page closes: it unhooks events, stops the engine,
and disposes it (after Dispose the engine singleton cannot be restarted —
one game host per process lifetime).

MODE A WALKTHROUGH 2: DRIVING Engine DIRECTLY (no host base)
--------------------------------------------------------------------------------
For demos/tools it is equally valid to skip the host base (ParticleTest,
Slider, and CoordinateTest do this). The essential order, on the UI thread,
from FirstStarted:

    var host = canvas.Host;                          // creates the render host
    host.ViewManager.ConfigureSingleFullView();      // views BEFORE Start
    Engine.Instance.Start(SynchronizationContext.Current);
    Engine.Instance.Configuration.TargetFPS = 90;

    // input adapters are opt-in on this path:
    Engine.Instance.InitializeCodeBrixMouseAdapter(canvas);
    Engine.Instance.Input.MouseEventPoller.MouseEvent += OnMouseEvent;
    Engine.Instance.Input.MouseEventPoller.StartMonitoringMouse();

    // then build scene content (or DirectDrawings, which need no scene)

Slider shows the important pattern for building content AFTER the engine is
already cycling: post the scene mutation to the engine thread —

    Engine.Instance.EngineDispatcher.Post(() => RebuildPuzzle(...));

MODE B WALKTHROUGH: SoftwareRenderedGameHostBase
--------------------------------------------------------------------------------
Derive, implement four members, construct with the canvas and tic rate, call
Initialize() from FirstStarted (the SoftRender sample is the full worked
example — 320x200 plasma+starfield at 70 Hz with raw-PCM audio):

    public sealed class MyRetroHost : SoftwareRenderedGameHostBase
    {
        public MyRetroHost(GameSurfaceCanvas canvas)
            : base(canvas, ticsPerSecond: 70) { }

        protected override void OnLoadContent()
        {
            // REQUIRED: configure the presenter or Initialize() throws
            Presenter.Configure(320, 200, PixelBufferFormat.Rgba8888,
                FrameOrientation.Identity, PixelFrameScaleMode.Fit,
                ImageFilterQuality.None);
        }

        protected override void ConfigureAudio()      // opt-in
            => AudioSystem.Initialize(44100, 2);

        protected override void OnTic() { /* one tic of game logic */ }

        protected override void OnRenderFrame(Span<byte> frame)
            { /* fill 320*200*4 bytes; presented when this returns */ }

        protected override void OnShutdown() { /* teardown */ }
        protected override void OnEnginePaused() { /* pause frame / save */ }
        protected override void OnEngineResumed() { }
    }

Per tic, on the dedicated game-loop thread, the base runs:
    InputPump.PollNow() -> OnTic() -> OnRenderFrame(buffer) -> present.

FixedRateGameLoop semantics the game can rely on:
  * Non-drifting fixed timestep: each tic's target advances by exactly one
    period; scheduling lag does not accumulate.
  * Bounded catch-up: at most MaxCatchUpTics (default 5) back-to-back tics;
    a longer stall re-baselines and counts DroppedTics instead of bursting.
  * Sleep+yield hybrid pacing — an idle loop does not burn a core.
  * Pause()/Resume() (and the global engine pause via PauseWithEngine, which
    the host base enables) park after the tic in progress and resume with a
    re-baselined schedule: no burst, nothing dropped.
  * ActualTicsPerSecond / TicCount / DroppedTics for health monitoring.
  * A callback exception stops the loop, lands in LastException, and raises
    UnhandledException — a Mode-B game should log it (the SoftRender host
    shows the pattern).

PixelFramePresenter details:
  * Configure(width, height, format {Rgba8888,Bgra8888}, orientation
    {Identity,Rotate90}, scaleMode {Fit,Stretch,PixelPerfect,Center},
    filterQuality). Reconfigurable at any time from the game thread (e.g.
    320x200 <-> 640x400).
  * PresentFrame(bytes | uint[] | ReadOnlyMemory<byte>): any thread, once per
    tic, exactly width*height*4 bytes; one full-frame copy, zero per-frame
    managed allocations, latest-frame-wins triple buffering.
  * Rotate90 displays column-major (transposed) buffers with NO CPU transpose
    — for column-major renderers.
  * WindowToBuffer/BufferToWindow map pointer coordinates across the
    letterbox (exposed on the canvas too).

TIMERS AND THE PER-CYCLE EVENTS (Mode A)
--------------------------------------------------------------------------------
Timers (CodeBrix.Platform.GameEngine.Timers.Timer) fire on the engine thread
inside the cycle:

    var t = Timer.Add("spawner", TimerType.PreCycle, TimerCycles.Repeating, 2.5);
    t.Tick += () => SpawnWave();          // every 2.5 s of engine time
    Timer.Remove("spawner");              // or t.Dispose()

  * TimerType.PreCycle fires at step 2 (before input/movement); PostCycle at
    step 15 (after rendering). TimerCycles.Once auto-removes after firing.
  * Repeating timers are schedule-preserving: a late cycle does not shift the
    next due time (no drift), and a missed interval fires as soon as possible.
  * Per-timer Paused property and static Timer.PausedAll pause timer events
    while the engine keeps running (distinct from the global engine pause,
    which parks everything and shifts timer schedules on resume).
  * HighResTimer wraps the Stopwatch clock: GetCurrentTick(),
    GetDuration(start, stop) in seconds, TicksPerSecond.

Choosing an event hook:
    BeforeBackgroundTasksExecute -- per-cycle pre-input logic (CHEAP ONLY)
    AfterBackgroundTasksExecute  -- per-cycle post-movement logic (CHEAP ONLY)
    BeforeFrameRender            -- per-FRAME setup (throttled to TargetFPS)
    AfterFrameRender             -- per-FRAME post-render (profiling, etc.)
    CPSCalculated                -- periodic metrics snapshot, ON THE UI THREAD
    Timer.Tick                   -- anything on a time schedule
    Paused / Resumed             -- the global pause hooks
Prefer Timers and the frame events; the per-cycle pair runs thousands of times
per second and is the easiest place to destroy performance.

INPUT
--------------------------------------------------------------------------------
Two complementary paths — EVENTS (edge-triggered) and POLLING (level):

  EVENTS: the pollers raise KeyDown/MouseEvent/Touch*/ButtonDown on the
  engine thread (Mode A, during step 4) or the game-loop thread (Mode B, from
  InputPump.PollNow). Keys must be registered first:
      var kb = Engine.Instance.Input.KeyboardEventPoller;
      kb.KeyDown += e => { if (e.KeyAction == KeyAction.Pressed
                               && e.KeyCode == (int)VirtualKey.W) ... };
      kb.StartMonitoringKey((int)VirtualKey.W, "W");
      // or StartMonitoringKeys(codes) / StartMonitoringAllKeys()
  Key codes are Windows VirtualKey values cast to int. KeyDownEventArgs
  carries KeyAction (Pressed/Released/Repeated) and modifier flags.
  MouseEventArgs has edge helpers (LeftButtonJustPressed, ...) plus polled
  properties on the poller (CurrentPosition, ButtonStates, ScrollDelta).
  Touch has Began/Moved/Ended events plus built-in tap/swipe/pinch
  recognizers. Event pacing: Configuration.TimeBetweenKeyboardEvents /
  ...Mouse/Touch/GamepadEvents (default 0.03 — SECONDS, despite a few older
  doc comments saying milliseconds) throttle repeat delivery; per-key
  overrides via the StartMonitoring* timeBetweenEvents parameter.
  Start/StopMonitoring* registrations are queued and applied at the next
  poll — not instantaneous.

  POLLING: IKeyboardAdapter.IsDown(keyCode) (reach it via
  KeyboardEventPoller.Adapter or your own adapter reference) is lock-free and
  valid from any thread at any time — the per-tic gameplay path for held keys
  (movement). Gamepads: IGamepadManager.Update() is throttled by the engine
  to the frame rate — never call it unthrottled yourself.

  Wiring adapters (Host extensions, canvas-based):
      Engine.InitializeCodeBrixKeyboardAdapter(canvas);
      Engine.InitializeCodeBrixMouseAdapter(canvas);
      Engine.InitializeCodeBrixTouchAdapter(canvas);
  CodeBrixGameHost and SoftwareRenderedGameHostBase do this for you.

  FOCUS: keyboard input reaches the canvas only while it HAS focus. Call
  canvas.EnsureFocus() (host bases apply it) — and remember that clicking any
  other control (a toolbar button) steals focus; hand it back (see the
  Spot.Brix MainPage for the toolbar refocus recipe). KeyDown bubbles from the
  FOCUSED element: a handler attached to the canvas never sees keys while a
  sibling control is focused.

  RELATIVE MOUSE (FPS mouse look): RelativeMouseSession over
  MouseDevice.MouseMoved — Begin() (hide + confine + accumulate), per-tic
  ConsumeDelta() -> (dx, dy), End(). Inactive (logged) on platform versions
  without relative mouse support.

  WHILE PAUSED: engine/game-loop input stops entirely. UI-level input
  (canvas.KeyDown, canvas.PointerPressed at the XAML layer) keeps flowing —
  that is where pause-toggle input belongs.

AUDIO
--------------------------------------------------------------------------------
Two paths, matching the two modes (both from CodeBrix.Platform.GameEngine.Audio,
device I/O via CodeBrix.Audio; every voice mixes into ONE shared native output
device, so overlapping sounds are cheap):

  RESOURCE PATH (typical for Mode A): AudioResourceManager.Instance loads
  clips (LoadFromFile / LoadFromStream / LoadFromPcm /
  LoadFromEngineAssetsFile); each AudioResource owns a voice: Play(fromStart),
  Pause(), Resume(), Stop(), Seek(), IsLooping, Volume, Pan, Duration,
  PlaybackCompleted. Clone() gives an independent voice of the same clip.

  SHORT-EFFECT PRELOAD (automatic): container-format sounds (.wav/.mp3) no
  longer than AudioResourceManager.PreloadShortSoundEffectMaxSeconds (default
  10 s; 0 disables) are decoded ONCE to raw float PCM in memory at load time
  (AudioResource.IsPreloaded == true; the CachedSound type). Plays, Clone()s,
  SoundChannel clips, and SfxVoicePool voices over a preloaded resource share
  that single decoded buffer — no decode, file, or MP3 work ever happens on
  the real-time audio thread. When the app pinned the device format
  (AudioSystem.Initialize) the decode also rate-converts up front. Longer
  material (music, ambience) keeps its streaming reader — leave it that way;
  preloading minutes of PCM would waste memory for a single voice.

  RAPID-FIRE SFX — SfxVoicePool: route sound-effect TRIGGERS (gunshots,
  pickups, impacts) through a fixed-size voice pool instead of playing the
  AudioResource itself per shot:
      AudioResourceManager.Instance.TryPlaySfx("laser", volume, pan, priority);
  That one call plays the preloaded clip on the shared pool
  (AudioResourceManager.Instance.SfxPool, 32 pre-allocated voices) — no
  per-play player allocation (no GC stutter), and a POLYPHONY CAP: when every
  voice is busy, SfxVoicePool.CullPolicy decides —
      CullOldest         (default) steal the longest-playing voice
      CullLowestPriority steal the lowest-priority voice (oldest on a tie),
                         unless every playing voice outranks the new trigger;
                         map camera distance / gameplay importance onto the
                         priority argument (higher wins)
      RejectNew          drop the new trigger
  Culls/drops log at Debug. Games with special needs construct their own
  SfxVoicePool(size) instances (several pools, different sizes) and call
  TryPlay(CachedSound|AudioResource|key, volume, pan, priority). The pool
  refuses non-preloaded resources rather than decode on trigger. Pool voices
  participate in the global engine pause like all engine audio (pool-wide
  override: SfxVoicePool.SuspendOnEnginePause). A voice returns to the pool
  when its clip ends (~25 ms sweep lag) or on StopAll()/cull.

  PINNED-DEVICE PATH (typical for Mode B, opt-in):
      AudioSystem.Initialize(44100, 2);      // pins the device rate — REQUIRED
                                             // before SoundChannel/streams
    * SoundChannel — a fixed classic game-audio channel: SetClip(key) (swap
      constantly), Play(volume, pan, pitch), live Volume/Pan/Pitch, State.
      Odd-rate raw-PCM clips are rate-converted automatically; Pitch is a live
      multiplier (0.05-20). NOTE: Stopped-state detection lags ~25 ms (the
      shared output's sweep timer).
    * StreamingAudioSource — endless pull-model stream (synth music, emulated
      sound chips): FillAudioBuffer(Span<float>) callback or ISampleProvider,
      pulled on the AUDIO CALLBACK THREAD (fast, allocation-free, never
      block); Start/Stop + Volume.
    * AudioResourceManager.LoadFromPcm(key, data, rate, bits {8u,16s},
      channels) — headerless raw-PCM lumps, no container needed. (Raw-PCM
      resources are not preloaded — they are already uncompressed in memory.)

  PAUSE INTERACTION (both paths): the global engine pause suspends playing
  voices and resumes exactly that set; short fire-and-forget clips ring out
  (see THE GLOBAL PAUSE SYSTEM). Per-voice override: SuspendOnEnginePause.

  SHUTDOWN: Engine.Dispose() shuts the shared audio output down (stopping any
  remaining voices and releasing the native device). Mode-B games that never
  dispose the engine call AudioSystem.Shutdown() themselves on exit (the
  SoftRender sample shows the pattern). The shared output restarts
  automatically if something plays later in the process.

SCENES, LAYERS, AND TILES (Mode A)
--------------------------------------------------------------------------------
The scene graph is Scene -> SceneLayer (a 2D tile grid) -> SceneLayerTile:

    var scene = new Scene();
    var layer = scene.AddLayer(columnCount: 8, rowCount: 8,
                               width: 64, height: 64,          // tile size px
                               zOrder: 0, parallax: 1f,
                               coordinateSystem: CoordinateSystemTypes.Orthogonal);
    layer[0, 0].CurrentFrame = tilesheet[4, 4];   // place a graphic on a cell

  * Layers: ZOrder (lower renders behind), Parallax (1 = moves with camera,
    <1 background, >1 foreground), Visible, WrapHorizontally/Vertically,
    OriginPx (world origin of tile (0,0)), ShowGridLines/ShowCollisionBoxes
    (debug overlays). Prefer SetTileSize(w,h) over setting TileWidth and
    TileHeight separately (one refresh instead of two). Nearly every layer
    property setter forces a full scene refresh — batch changes.
  * Coordinate systems per layer: Orthogonal, IsometricRhombic,
    IsometricAxial, HexAxialFlatTop, HexAxialPointedTop (the CoordinateTest
    sample exercises them). Conversions: layer.GridToWorldPx / WorldPxToGrid /
    GetAdjacentTile(tile, CardinalDirections); tile indexers return null out
    of bounds (no auto-wrap — call WrapGrid first when wrapping).
  * SceneLayerTile: cells are created with the layer; assigning CurrentFrame
    places a tilesheet frame. Set EnableAnimator = true only on tiles that
    animate (it allocates an Animator per tile).
  * Scenes self-register globally (Scene.GetSceneByID / GetAllScenes) and
    must be Dispose()d (or Scene.ClearAllScenes()) or they linger there.
  * Scene.FullRefreshNeeded flags a full redraw; structural changes set it
    automatically.

VIEWS AND CAMERAS (Mode A)
--------------------------------------------------------------------------------
Each render surface has a ViewManager; each View pairs a Viewport (screen
rectangle + zoom) with a Camera (world position):

    host.ViewManager.ConfigureSingleFullView();          // the usual case
    host.ViewManager.ConfigureVerticalSplit(1f, 1f);     // split screen
    var view = host.ViewManager.AddView(targetRectPx, zoom, zOrder);  // custom

  * Camera movement (every move clamps to WorldBoundsPx unless it is Empty):
    instant — SnapTo, CenterOn, CenterOnGrid, PanBy; smooth — PanTo/
    PanCenterTo(speed), PanToOverDuration/PanCenterToOverDuration(seconds),
    PanToGridOverDuration; following — FollowCentered(movable, speed, hard),
    FollowCenteredX/Y (axis lock), Follow(func), ClearFollow().
    FollowLerpPerSecond (default 8) sets follow snappiness; DeadZonePx gives
    the target wiggle room. Camera.PositionPx is read-only — move via methods.
  * Viewport: Zoom (>1 in, <1 out), SnapZoom, ZoomTo(lerp),
    ZoomToOverDuration(seconds); View.ZoomAroundScreenPoint gives map-style
    wheel zoom (zoom + pan together). MinZoom 0.1, MaxZoom 8 per view.
  * Picking: view.ScreenPxToGrid(layer, screenPoint) turns a pointer position
    into a grid cell (Spot.Brix does exactly this for click handling); plus
    ScreenPxToWorldPx / WorldPxToScreenPx / WorldRectToScreenRect, all
    parallax-aware.
  * host.Bind(scene, limitCameraToWorldBoundPx) auto-creates one full-surface
    view if none exist; ConfigureSingleFullView throws before the adapter
    exists — bind/configure from FirstStarted onward, on the UI thread.
  * host.RedrawDirtyRectangleOnly (default true) presents only dirty regions;
    host.Backbuffer.ClearColor sets the letterbox/background color.
  * RenderBackbufferPostScene(SKCanvas) is a post-scene overlay hook — it runs
    on the engine thread for CPU surfaces but on the GL thread for GPU
    backbuffers; never marshal that canvas elsewhere.

TILESHEETS, SPRITES, AND ANIMATION (Mode A)
--------------------------------------------------------------------------------
TILESHEETS: TilesheetRegistry.Instance is the named store —
LoadFromImageFile / LoadFromBitmap / LoadFromStream / LoadFromAssetsFile /
LoadFromDefinitionFile(.gts) / LoadFromDefinitionAsset. A .gts file is a JSON
TilesheetDefinition (image source, regions, mask); relative image paths
resolve against the .gts directory.

    var sheet = TilesheetRegistry.Instance.LoadFromImageFile("spots", path);
    sheet.DefaultRegion.TileSize = new Size(93, 96);
    sheet.ApplyMask(Color.Black.ToSKColor());   // optional color-key transparency
    Frame frame = sheet[0, 0];                  // or sheet[regionName, x, y]

Sheets can carry multiple named Regions (AddRegion with area, tile size,
padding, margin, overhang); Frame is the (sheet, cell) handle everything else
consumes.

SPRITES: create ONLY via the manager (the constructor is not public):

    var sprite = SpriteManager.Instance.CreateSprite(sceneLayer, new Frame(sheet, 0, 0), "hero");
    sprite.Visible = true;
    sprite.SetPosition(new Vector2(5, 0));      // GRID cells, not pixels

  * Sprite positions are GRID coordinates on their scene layer; RenderSize,
    NudgeX/NudgeY, and CollisionArea are pixels.
  * SpriteManager.SizeNewSpritesToSceneLayer (default true) sizes new sprites
    to the layer's tile size, not the frame's native size — set RenderSize
    (or the flag) for native-size sprites.
  * CloneSprite, GetSpriteByID, GetSpritesAtViewPixel(view, point) for
    picking. Sprite.Dispose() is deferred to the next cycle — safe mid-frame.
  * Resize/pulse: ResizeTo(size, seconds), ScaleBy(factor, seconds),
    PulseTo/PulseBy(grow, shrink, loop), StopPulse(snapBack), CancelResize;
    ResizeComplete fires at the end of EVERY pulse leg and on cancel —
    unhook one-shot handlers inside the handler.
  * Jiggle (visual-only shake; never affects collision or RenderSize):
    StartJiggle(intensityX, intensityY, speed, duration, loop, ...),
    JiggleOnce, StopJiggle.

ANIMATION CYCLES:

    var seq = new FrameSequence();
    seq.AddFrame(sheet, 0, 0); seq.AddFrame(sheet, 1, 0);
    seq.AddFrame(sheet, 2, 0); seq.AddFrame(sheet, 3, 0);
    seq.SequenceCycleType = CycleType.PingPong;    // Simple | Repeating | PingPong
    sprite.TileAnimator.CurrentCycle = new Cycle(seq, 0.5, "walk"); // 0.5 s/frame
    sprite.TileAnimator.StartAnimation();

  * Cycle keys are a GLOBAL registry; constructing a Cycle with an existing
    key replaces it, and SetCurrentCycle/StartAnimation(key) fetch a CLONE.
  * Cycles can chain (NextCycle) and hide the tile at cycle end
    (hideTileOnCycleEnd). A throttle of 0 auto-stops the animation.
  * Animator events: Started, Stopped, Cycled (per frame advance). Never call
    Animator.Dispose directly — the owning Tile does.
  * Static scene tiles animate too: set tile.EnableAnimator = true first.

MOVEMENT, EASING, AND COLLISIONS (Mode A)
--------------------------------------------------------------------------------
Every Sprite (and DirectComposite / movable direct drawing) has a .Movement
MovementController. Units are the mover's space — GRID cells for sprites,
PIXELS for direct drawings; all durations are seconds. Per-frame priority:
Follow > Scripted > Integrated physics.

  SCRIPTED (tweens):
    sprite.Movement.MoveTo(target, 0.4f, EasingKind.SmootherStep);
    sprite.Movement.MoveBy(delta, 10f, EasingFunctions.EaseInOutQuad);
    sprite.Movement.MoveToward(target, speedPerSec);   // constant speed
    sprite.Movement.CancelScript(); / StopAllMovement();
    events: ScriptedMovementStarted / ScriptedMovementStopped (arrival hook)
    PITFALL: MoveBy(delta, float, ...) has two meanings — with an easing
    argument the float is a DURATION (tween); without, it is a SPEED
    (constant velocity). Pass EasingKind/Func explicitly to get the tween.
  INTEGRATED (physics): SetVelocity, SetAcceleration, SetMaxSpeed,
    SetLinearDamping. Setting velocity/acceleration cancels a script;
    starting a script zeroes velocity/acceleration.
  FOLLOW: FollowPixelSoft/Hard(getPos, speed, offset),
    FollowTileSoft/Hard(target), Unfollow().
  EASING: EasingFunctions.Linear, EaseIn/Out/InOutQuad|Cubic|Quart|Quint,
    SmoothStep, SmootherStep — or the EasingKind enum.

COLLISIONS:
  * Nothing collides until tile.CollisionsEnabled = true (default false).
    Sprites auto-create a TileCollider with all-groups masks; customize with
    TileCollider(tile, collisionGroup, collidesWith, CollisionResponseType).
  * Groups are bitmasks — allocate named bits via the scene's
    CollisionGroups registry (Define/Get; presets WorldStatic, Actors,
    Projectiles, Triggers).
  * Resolution is AUTOMATIC, once per cycle, per layer: Solid vs Solid gets
    a minimum-axis push-out with velocity canceled on the hit axis (slide);
    Trigger reports without push-out. (Overlap events are currently
    engine-internal — collision response is automatic-only; query manually
    via SceneLayer.ColliderRegistry.QueryAabb for game logic.)
  * SceneLayer.ShowCollisionBoxes = true overlays collision bounds for
    debugging.

DIRECT DRAWINGS AND PARTICLES (Mode A, immediate-mode)
--------------------------------------------------------------------------------
Direct drawings bypass the tile grid: construct with the render host and
either a SceneLayer (world-space, scrolls with the camera) or a View
(screen-fixed HUD), then chain fluent Set*() calls. They need no scene cell
and self-register with DirectDrawingManager (dispose to remove).

    DirectImage(image, host, layerOrView, bounds)   .SetScaleMode(...)
    DirectSvg(svgResource, host, layerOrView, bounds)
    DirectRectangle(color, host, layerOrView, bounds)
        .SetFilled(true).SetCornerRadius(6f).SetBorderColor(...)
        .SetStrokeWidth(6f).SetStrokeAlign(...).SetAlpha(128)
        .SetBlendMode(SKBlendMode.Screen).PulseFill(a, b, seconds)
        .PulseBorder(a, b, seconds)
    TextBlock(host, viewOrLayer, bounds)
        .SetFont(SKTypeface.FromFamilyName("..."), 16f, minSize: 14f)
        .SetColors(fore, back).SetAlignment(SKTextAlign.Center, VerticalAlign.Center)
        .EnableWrapping().SetMaxLines(6).UseShadow().SetShadow(...)
        .UseOutline().PulseColor(...).StartTypewriter(...)/.StartWordReveal(...)
        .SetText("...")   // updatable every frame (score/FPS readouts)
    DirectComposite(host, DirectDrawingMode.View)
        .Add(child1).Add(child2)      // group; has .Movement (pixel space)
        .SetOpacity/FadeTo/FadeIn/FadeOut
    ParticleSurface(host, layerOrView, bounds, nickname, maxParticles)
        .Emitters.Add(new ParticleEmitter {
            Position, EmitRate, LifeRange, VelocityRangeX/Y, SizeRange,
            GravityY, JitterX/Y, Color, BlendMode, ParticleSprite,
            OnSpawn = (ref Particle p) => { /* per-particle custom */ } });
        // plus Burst(emitter, count), ActiveParticleCount, GlobalEmitScale,
        // CullingMarginX for off-surface emitters

The ParticleTest sample is the reference for particles + composites +
TextBlock; the glowing pulsing text box it animates upward is a
DirectComposite of a DirectRectangle and a TextBlock moved with
Movement.MoveBy(new Vector2(0, -500), 10f, EasingFunctions.EaseInOutQuad).
ZOrder orders direct drawings among themselves per drawing mode.

SAVE / LOAD: EngineState (Mode A)
--------------------------------------------------------------------------------
EngineState (Engine.Instance.State) is a serializable snapshot of the engine's
live registries: AssetsFiles, Tilesheets, Cycles, Scenes, Sprites, and
SoundResources (State.ValueBag is deliberately NOT serialized).

    Engine.Instance.State.SaveToFile("save1.json", compress: true);
    EngineState.LoadFromFile("save1.json", compressed: true);
    EngineState.MergeFromFile("patch.json", overwriteExisting: true,
        parts: EngineStateParts.Scenes | EngineStateParts.Sprites);

WHAT ROUND-TRIPS: the full populated object graph — scenes with their layers
(tile grids, per-tile frames/visibility/flags, wrap flags, origin, parallax,
z-order, tile size, collision groups), sprites (position, layer reference,
frame, alignment/nudge/render-size, collision flag), animation cycles
(sequences, throttle, chained/self NextCycle references), audio specs
(source, volume/pan/looping), asset-pack references, and tilesheets
(re-registered by definition). SHARED REFERENCES are preserved as identities:
a sprite's layer reference and the scene's layer entry deserialize to the SAME
instance ($id/$ref via CodeBrix.Json.Extensions Feature A). Loaded content is
fully REHYDRATED: layer collision registries/refresh queues, tile colliders,
tile->layer back-references, sprite animators/movement/colliders, and
scene<->layer event wiring are rebuilt during the load's merge step.

NOT persisted (by design): State.ValueBag and the per-tile/per-scene ValueBags
(open-ended object data), in-flight movement scripts/jiggle/pulse state
(sprites load at rest), animation playback position, and audio playback
position. Live wiring (devices, streams, Skia objects) is never serialized —
audio specs re-load from their persisted source (loose file path or asset-pack
entry) and re-apply volume/pan/looping.

MECHANICS AND RULES:
  * Save writes a versioned envelope { "schema": 1, "state": {...} };
    compress = GZip. The compress/compressed flags must AGREE between save
    and load — the loader does not sniff. Pre-v1 (Newtonsoft) files are
    rejected by design.
  * LoadFromFile CLEARS the selected parts first (overwrite semantics);
    MergeFromFile merges (scenes matched by ID, sprites by Nickname, cycles/
    audio by key). Audio specs whose resource came from an asset pack apply
    their saved settings to the pack-loaded resource.
  * Loading is STAGED internally: asset packs and tilesheets are registered
    FIRST, then the object graph deserializes (tile/sprite Frames resolve
    tilesheets BY NAME against the live registry during that read — this is
    why a save file's tilesheets must load with it: don't load parts:Scenes
    alone into a process that hasn't loaded the tilesheets those scenes use).
  * EngineStateParts is a flags enum (AssetsFiles, Tilesheets, Cycles,
    Scenes, Sprites, Audio, All); Tilesheets/Audio automatically pull in
    AssetsFiles they depend on. separateGtsFiles:true writes tilesheets as
    sidecar .gts files next to the save.
  * A save file mounts automatically at engine init via
    Configuration.StateFiles (List<StateFileMount>): File, IsCompressed,
    OverwriteExisting, EngineStateParts.
  * The proper hook for save-on-pause: call SaveToFile from the Paused event
    (or OnEnginePaused) — game state is quiescent there by contract. Load/
    merge likewise belongs at quiescent moments (before Start, or while
    paused), never mid-cycle from another thread.
  * EngineState.SerializerOptions is the options template (public, settable).
    It carries the EngineSaveContractResolver (namespace
    CodeBrix.Platform.GameEngine.Serialization) — the piece that makes the
    engine's model types round-trip under System.Text.Json (object contracts
    for the referenceable types, non-public-member access, deserialization
    constructors) — plus leaf converters for Frame, FrameSequence, the
    SceneLayerTile[,] grid, and CollisionGroupRegistry. If a game replaces
    SerializerOptions, keep the resolver and those converters or save/load
    breaks. Do NOT add the CodeBrix.Json.Extensions polymorphism fallback
    converter factory to these options — it would take precedence over
    reference handling.
  * Custom Sprite/Tile SUBCLASSES are not round-trip-aware out of the box:
    the save contracts cover the engine's own types. A game that must persist
    a subclass should keep its persistent data in engine-visible members and
    rebuild the subclass wiring itself after load (or serialize its own data
    alongside the engine save).

ASSETS: AssetsFile
--------------------------------------------------------------------------------
AssetsFile is a zip-backed asset container (optionally AES-256 encrypted).
Contents are fully buffered into memory at load; the file handle closes
immediately.

    var pack = AssetsFile.LoadOrCreate("assets.pack");
    using Stream? img = pack.Get(AssetTypes.Image, "hero.png");
    // or pack[AssetTypes.Image, "hero"] — extension optional, case-insensitive
    pack.Add(AssetTypes.Font, "/path/SomeFont.ttf");
    pack.Save();                                    // rewrites the zip

  * AssetTypes: Image, Audio, Video, Cursor, Font, Misc, Svg,
    TilesheetDefinition.
  * Get returns a fresh read-only MemoryStream per call; exact-name match
    first, then base-name match ignoring extension.
  * AssetsFileIdentifier(pack, type, name) is a serializable pointer to one
    entry (used by audio/tilesheet loading and save files); IsValid guards a
    missing entry.
  * AudioResourceManager.LoadFromEngineAssetsFile(pack) bulk-loads every
    audio entry.

CONFIGURATION: EngineConfiguration / EngineConfigurationFile
--------------------------------------------------------------------------------
Engine.Instance.Configuration (loaded by Initialize; default file
"gameengine.json"; a missing file just yields defaults):

    TargetFPS = 60                    -- render throttle; 0 = uncapped
    VSync = true                      -- GPU (Tier B) backbuffers only
    MsaaSampleCount = 1               -- GPU only; applies at next surface init
    SamplingTimeForCPS = 1.5          -- seconds between CPSCalculated events
    TimeBetweenKeyboardEvents = 0.03  -- repeat-event throttle floors (seconds)
    TimeBetweenMouseEvents / TouchEvents / GamepadEvents = 0.03
    LoggingMode = Asynchronous        -- Synchronous = ordered but slower
    LoggingQueueCapacity = 8192       -- async overflow drops
    FlushAsyncLogsOnShutdown = true
    PauseSuspendsAudio = true         -- global pause suspends game audio
    PauseShortSoundEffectSeconds = 1.0-- fire-and-forget exemption threshold
    StateFiles                        -- state files auto-mounted at init
    ConfigurationSections             -- free-form string sections for the
                                         game's own settings, with
                                         Get/Set/Has/Remove helpers and
                                         config[section, key] indexers

EngineConfigurationFile.CreateNew/Load/Save manage the file; AutoSave writes
on Dispose.

PLAYING WELL WITH THE ENGINE — THE CHECKLIST
--------------------------------------------------------------------------------
A game "plays well" when it respects the engine's contracts instead of
fighting them:

THREADS
  [] Mutate game state on the engine thread (Mode A) or the game-loop thread
     (Mode B) only; use EngineDispatcher.Post to get there.
  [] Never touch XAML/UI from the engine or game-loop thread; use
     UiDispatcher.Post.
  [] Audio fill callbacks: fast, allocation-free, never block, never touch
     game state.
  [] Do not call blocking waits (Task.Wait, lock convoys, I/O) inside cycle
     events, timer handlers, or OnTic — one slow handler stalls the whole
     game.

THE LOOP
  [] Pick a real TargetFPS (60/90/120) for Mode A; 0 (unbounded) burns a core
     for no visible gain on most displays.
  [] Keep per-cycle handlers (Before/AfterBackgroundTasksExecute) trivial —
     they run thousands of times per second. Put periodic work on Timers.
  [] Mode B: pick the tic rate the GAME's logic wants (35/70 Hz retro rates
     are first-class); do heavy one-off work outside OnTic or accept dropped
     tics (watch DroppedTics/ActualTicsPerSecond).
  [] No per-frame allocations in hot paths (OnRenderFrame, fill callbacks,
     per-cycle handlers) — the SoftRender sample logs its per-frame allocs as
     a regression canary; zero steady-state garbage is achievable and worth it.

PAUSE CORRECTNESS
  [] Wire the hosting app: minimize -> Pause(), restore -> Resume().
  [] Register save-game / pause-screen logic on Paused (or OnEnginePaused);
     tear down on Resumed. Never poll IsPaused from game logic to "stop
     yourself" — the engine already stopped you.
  [] Put un-pause input at the UI layer, never engine input (pollers are
     parked while paused).
  [] Long-running voices the game manages specially: set SuspendOnEnginePause
     explicitly instead of fighting the automatic rule.

SOUND EFFECTS
  [] Fire rapid SFX through AudioResourceManager.TryPlaySfx (or your own
     SfxVoicePool) — never a fresh decode or player per shot. Short effects
     preload automatically; check IsPreloaded if a pool play returns false.
  [] Keep music/ambience on streaming readers (one long-lived voice each);
     don't raise PreloadShortSoundEffectMaxSeconds to cover them.
  [] Pick the cull policy deliberately; with CullLowestPriority, give the
     player's critical cues the highest priorities so they are never stolen.

MUTUAL-EXCLUSIVITY RULES (each throws if violated)
  [] One mode per canvas: Host XOR UsePixelFramePresenter().
  [] InputPump.PollNow() only when the engine loop is NOT running.
  [] AudioSystem.Initialize before SoundChannel/callback streams.
  [] Presenter.Configure before the Mode-B loop starts (OnLoadContent).

RESOURCES AND SHUTDOWN
  [] Load content in the host's load hooks (before the loop starts), not
     mid-game on the hot path; AssetsFile/tilesheets/audio all support
     up-front loading.
  [] Dispose the game host on page close; it stops the loop, unhooks events,
     and tears the engine down in the right order. After Engine.Dispose() the
     singleton is dead for the process — do not try to restart it.
  [] Unsubscribe any engine events you subscribed outside the host bases
     (the bases unhook their own).

RENDERING
  [] Pin a render resolution (SetRenderResolution) when the game's layout
     assumes fixed coordinates; letterboxing is automatic, and pointer
     mapping across the letterbox is provided (WindowToBuffer).
  [] Do not force refreshes during window resizes; the canvas already
     suppresses and resumes presenting around a resize.
  [] Size-anchored HUD content: reposition in OnRenderSurfaceResized.

CODING CONVENTIONS (CodeBrix family)
--------------------------------------------------------------------------------
  * Target net10.0 only; never multi-target.
  * File-scoped namespaces; usings at the top (System.* first), never global
    usings.
  * XML doc comments on public/protected members (GenerateDocumentationFile
    = true; fix CS1591 at the source, never suppress).
  * xUnit v3 + SilverAssertions for tests; coverlet.collector for coverage.
  * No project-wide warning suppression except the documented port exceptions
    below.

  PORT EXCEPTIONS (this repository):
  * Nullable reference types are ENABLED (<Nullable>enable</Nullable>) on both
    libraries. The upstream source relies on "?" annotations throughout;
    stripping them would change observable public signatures and reduce
    fidelity. This is the same sanctioned exception used by
    CodeBrix.Platform.OpenGL. Because NRT is on, "?" on reference types and
    the "!" null-forgiveness operator are permitted in this repository
    (unlike the family default).

ARCHITECTURE
--------------------------------------------------------------------------------
  CodeBrix.Platform.GameEngine (core)
    deps: SkiaSharp, CodeBrix.SkiaSvg, CodeBrix.Compression, CodeBrix.Audio,
          System.Text.Json + CodeBrix.Json.Extensions, Microsoft.Extensions.*
    No CodeBrix.Platform UI dependency. Rendering seam = SKImage + adapter base.

  CodeBrix.Platform.GameEngine.Host
    refs: CodeBrix.Platform.GameEngine
    deps: CodeBrix.Platform, CodeBrix.Platform.SkiaSharp.Views,
          CodeBrix.Platform.Graphics3DGL (GPU path), SkiaSharp,
          CodeBrix.Platform.Svg (platform-integrated SVG)
    Tier A = CPU BitmapBackbuffer adapter (default, all heads).
    Tier B = GPU GpuBackbuffer adapter via Graphics3DGL offscreen GL +
    one-copy readback (opt-in: GameSurfaceCanvas.UseGpuRendering — see RENDER
    TIERS). GL-thread surfaces skip the cycle's render step; the adapter
    renders them via GlRenderAndSnapshot on the UI (GL) thread at TargetFPS
    cadence. They park during the global pause, are captured by the pause
    snapshot via the adapter's latest presented frame, and get one adapter-
    driven paused-overlay frame after the Paused handlers run.
    The adapter builds its GRContext with OffscreenGLContext.CreateGrContext()
    (requires CodeBrix.Platform >= 1.0.199.897, whose X11 GL wrapper filters
    the garbage egl* stubs glvnd/Mesa returns from glXGetProcAddress — the
    cause of the earlier assembled-interface segfault).

  Source is grouped into sub-folders that mirror the sub-namespaces
  (Drawing, Rendering, Scenes, Physics, Input, Audio, Assets, Timers, ...).

THE SAMPLES — THE LIVING REFERENCE
--------------------------------------------------------------------------------
samples/ holds six complete games/demos, each with LinuxX11, Win32Skia, and
MacOS heads plus a shared .UI project; each is the reference consumer for the
subsystems it exercises:

  Spot.Brix      -- Mode A via CodeBrixGameHost: scenes, sprites, tilesheets,
                    engine mouse+keyboard input, toolbar/focus recipe.
  Slider         -- Mode A, direct Engine: sprites built on the engine thread
                    via EngineDispatcher.Post, engine mouse events, rebuild-
                    while-running.
  CoordinateTest -- Mode A, direct Engine: coordinate systems (orthogonal,
                    isometric, hex), cameras/views.
  ParticleTest   -- Mode A, direct Engine: ParticleSurface/emitters,
                    DirectComposite/TextBlock/DirectRectangle, movement
                    easing — plus the campfire click = global Pause()/Resume()
                    toggle (UI-level pointer input + letterbox mapping).
                    PARTICLETEST_USE_GPU=1 runs it on Tier B.
  SoftRender     -- Mode B end-to-end: 320x200/70 Hz plasma+starfield,
                    presenter, InputPump, raw-PCM blips (SoundChannel),
                    streamed drone (StreamingAudioSource), zero-alloc frame
                    loop, loop health stats.
  GpuRender      -- Mode A, direct Engine: the TIER B (GPU) showcase and
                    SoftRender's GPU-first counterpart — resolution-
                    independent SkSL plasma + starfield via a custom
                    DirectDrawingBase subclass (PlasmaBackdrop), stats
                    TextBlock with live GPU FPS, click-anywhere pause with a
                    pause overlay (paused-frame + snapshot demo), window-
                    tracking resolution with resize handling.
                    GPURENDER_USE_CPU=1 runs the same scene on Tier A.

TESTING
--------------------------------------------------------------------------------
  Core tests are headless unit tests (the UI-agnostic core makes this clean):
  populated-graph save/load round-trips (EngineStateRoundTripTests: scenes/
  layers/tile grids, shared sprite references, cycles, loose-file and
  asset-pack audio, compression, merge semantics), the global pause suite
  (EnginePauseTests: park/resume semantics, no-burst time shifting, audio
  suspend rules, snapshot capture), and the audio SFX suites
  (CachedSoundTests, SfxVoicePoolTests — decode-once preload and the pool's
  cull-policy selection logic; nothing in them opens the audio device). Host
  tests cover what can run without a live UI head; head-dependent behavior is
  env-gated or skipped with a reason.

  The core test assembly runs its collections SERIALLY
  (CollectionBehavior(DisableTestParallelization = true) in AssemblyInfo.cs):
  the engine under test is a process-global singleton machine (Engine.Instance
  plus the scene/sprite/cycle/tilesheet/audio registries), so tests that
  populate or clear that state cannot overlap. Keep new test classes
  compatible with that assumption — clean up global state you create.

    dotnet test CodeBrix.Platform.GameEngine.slnx

================================================================================
END OF AGENT-README
================================================================================
