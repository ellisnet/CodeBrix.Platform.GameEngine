================================================================================
AGENT-README: CodeBrix.Platform.GameEngine
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.GameEngine.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.GameEngine is a fully managed, cross-platform 2D / 2.5D game
engine for .NET, built on SkiaSharp. It provides tile maps, tilesheets, sprites,
layered scenes, camera/view systems, animation, physics/collision, input, audio,
a save/load system, and a global pause system. Target: .NET 10 or later.

The package carries TWO assemblies that mirror the classic core/host split:

  * CodeBrix.Platform.GameEngine        -- the platform-agnostic engine CORE.
        No UI-framework dependency; headless-usable. Its rendering seam is a
        SkiaSharp SKImage plus the RenderSurfaceAdapterBase abstraction.

  * CodeBrix.Platform.GameEngine.Host   -- the HOST layer that runs the engine
        on CodeBrix.Platform (all six heads: Win32-Skia, WPF-Skia, X11, Wayland,
        Frame Buffer, macOS). Contains the CpuRendering (CPU) and GpuRendering
        (GPU) render-surface adapters, keyboard/mouse/touch input adapters, a
        UI dispatcher, and the game-host base classes games derive from.

The engine core is a vendored port of an open-source, MIT-licensed game engine
(MIT, (c) 2025 Michael Adkins). Its namespaces are
CodeBrix.Platform.GameEngine[.*]; do not use the upstream namespaces. See
THIRD-PARTY-NOTICES.txt (shipped in the package) for the notices, the upstream
project name and the exact revision this port tracks.

OTHER PACKAGES FROM THE SAME REPOSITORY
---------------------------------------
  CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever — gamepads (SDL2 game
  controller support, optional add-on); see
  src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt. This engine package
  has NO gamepad backend of its own: it defines the IGamepadManager<T> /
  IGamepadAdapter seam and the GamepadEventPoller, and the Sdl2 package fills
  the seam.

INSTALLATION
============
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.MitLicenseForever

This single package bundles BOTH assemblies -- the engine core
(CodeBrix.Platform.GameEngine.dll) and the host layer
(CodeBrix.Platform.GameEngine.Host.dll) -- so one reference gives you
everything. There is no separate .Host package.

The namespaces are CodeBrix.Platform.GameEngine[.*] and
CodeBrix.Platform.GameEngine.Host[.*] (WITHOUT the license suffix).

License: MIT.

NuGet dependencies (pulled in automatically, listed by id):
    CodeBrix.Platform.ApacheLicenseForever                 -- the UI platform
    CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever    -- SKXamlCanvas base
    CodeBrix.Platform.Graphics3DGL.ApacheLicenseForever    -- GPU render path
    CodeBrix.Platform.Svg.ApacheLicenseForever
    SkiaSharp
    CodeBrix.SkiaSvg.MitLicenseForever
    CodeBrix.Compression.MitLicenseForever
    CodeBrix.Audio.MitLicenseForever                       -- device audio I/O
    CodeBrix.Json.Extensions.MitLicenseForever             -- save/load refs
    Microsoft.Extensions.Configuration (+ .Binder, .Json)
    Microsoft.Extensions.Logging.Console, Microsoft.Extensions.Logging.Debug

Requirements:
  * A CodeBrix.Platform application with exactly ONE head package per
    executable project (for example CodeBrix.Platform.Runtime.Skia.X11.
    ApacheLicenseForever for Linux X11, CodeBrix.Platform.Runtime.Skia.Win32.
    ApacheLicenseForever for Windows, CodeBrix.Platform.Runtime.Skia.MacOS.
    ApacheLicenseForever for macOS). The engine renders into a XAML control.
  * The head application supplies the SkiaSharp native libraries. A HEADLESS
    consumer on Linux (a test project driving the core directly) must add
    SkiaSharp.NativeAssets.Linux itself; the base SkiaSharp package carries
    Windows/macOS natives only.
  * GpuRendering on Windows needs a real OpenGL driver (see RENDER MODES).
  * Optional: CodeBrix.Audio.Opus.BsdLicenseForever for .opus assets (see
    AUDIO / FORMATS).

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.GameEngine;                 // Engine, EngineState, dispatchers,
                                                        //   TypedValueBag, ValueKey<T>
    using CodeBrix.Platform.GameEngine.Assets;          // AssetsFile
    using CodeBrix.Platform.GameEngine.Audio;           // AudioSystem, SoundChannel, streams,
                                                        //   MusicManager, SfxVoicePool
    using CodeBrix.Platform.GameEngine.Configuration;   // EngineConfiguration[File]
    using CodeBrix.Platform.GameEngine.Drawing;         // Tile, ImageFilterQuality, SvgResource
    using CodeBrix.Platform.GameEngine.Drawing.Sprites; // Sprite, CompositeSprite, SpriteManager
    using CodeBrix.Platform.GameEngine.Drawing.Direct;  // DirectImage, TextBlock, particles,
                                                        //   lighting, SplashOverlay, HealthBar
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;     // Tilesheet, TilesheetRegistry
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS; // TilesheetDefinition (.gts)
    using CodeBrix.Platform.GameEngine.Drawing.Animation;      // Cycle, FrameSequence, Animator
    using CodeBrix.Platform.GameEngine.Drawing.Collisions;     // TileCollider
    using CodeBrix.Platform.GameEngine.Rendering;       // render-surface hosts, backbuffers,
                                                        //   PixelFramePresenter
    using CodeBrix.Platform.GameEngine.Rendering.Views; // ViewManager, View, Camera, Viewport
    using CodeBrix.Platform.GameEngine.Rendering.Text;  // FontManager
    using CodeBrix.Platform.GameEngine.Scenes;          // Scene, SceneLayer, SceneLayerTile
    using CodeBrix.Platform.GameEngine.Physics.Movement;       // MovementController, easing
    using CodeBrix.Platform.GameEngine.Physics.Movement.Easing; // EasingFunctions, EasingKind
    using CodeBrix.Platform.GameEngine.Physics.Collisions;     // ICollider, Aabb, registries,
                                                        //   CollisionAdjust, TileCollisionType,
                                                        //   CollisionProfile[Names|Registry]
    using CodeBrix.Platform.GameEngine.Effects;         // EffectsManager, DisplayEffect, fades,
                                                        //   wipes, slides, zooms, earthquake
    using CodeBrix.Platform.GameEngine.Input;           // InputPump, InputEventConfigurationBase
    using CodeBrix.Platform.GameEngine.Input.Keyboard;  // KeyboardEventPoller, KeyAction
    using CodeBrix.Platform.GameEngine.Input.Mouse;     // MouseEventPoller, MouseButton
    using CodeBrix.Platform.GameEngine.Input.Touch;     // TouchEventPoller, TouchPoint
    using CodeBrix.Platform.GameEngine.Input.Touch.Gestures;   // Tap/Swipe/Pinch recognizers
    using CodeBrix.Platform.GameEngine.Input.Gamepad;   // IGamepadAdapter, GamepadStickState
    using CodeBrix.Platform.GameEngine.Timers;          // Timer, HighResTimer, FixedRateGameLoop
    using CodeBrix.Platform.GameEngine.Extensibility;   // IEnginePlugin, EnginePluginRegistry
    using CodeBrix.Platform.GameEngine.Logging;         // EngineLogger, EngineLoggingMode
    using CodeBrix.Platform.GameEngine.Serialization;   // EngineSaveContractResolver
    using CodeBrix.Platform.GameEngine.Host;            // EngineExtensions (adapter wiring)
    using CodeBrix.Platform.GameEngine.Host.Hosting;    // CodeBrixGameHost,
                                                        //   SoftwareRenderedGameHostBase
    using CodeBrix.Platform.GameEngine.Host.Rendering;  // GameSurfaceCanvas
    using CodeBrix.Platform.GameEngine.Host.Input.Keyboard; // CodeBrixKeyboardAdapter
    using CodeBrix.Platform.GameEngine.Host.Input.Mouse;    // CodeBrixMouseAdapter,
                                                            //   RelativeMouseSession
    using CodeBrix.Platform.GameEngine.Host.Input.Touch;    // CodeBrixTouchInputAdapter
    using CodeBrix.Platform.GameEngine.Host.Threading;      // CodeBrixPlatformUiDispatcher

CORE API REFERENCE
==================
The sub-sections below walk the engine subsystem by subsystem, in the order a
game meets them. Member names and signatures are inline; the QUICK REFERENCE
CARD at the end collects the most-used signatures in one place.

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

    void Initialize(string? configFileName = null, bool? autoSaveConfig = null,
                    IKeyboardAdapter? keyboardAdapter = null,
                    IMouseAdapter? mouseAdapter = null,
                    ITouchAdapter? touchAdapter = null,
                    IGamepadManager<IGamepadAdapter>? gamepadManager = null)
    void Start()                                   // captures SynchronizationContext.Current
    void Start(SynchronizationContext uiContext)
    void StartTimerDriven(SynchronizationContext uiContext)   // + Tick() per timer tick
    void Tick()

Start() must receive the UI thread's SynchronizationContext (the parameterless
overload captures SynchronizationContext.Current, so call it ON the UI thread).
For single-threaded runtimes, StartTimerDriven(uiContext) + a platform timer
calling Engine.Instance.Tick() replaces the background thread.

State and metrics on Engine.Instance: IsInitialized, IsInitializing, IsRunning,
IsPaused, IsDisposed, IsDisposing, CyclesPerSecond, FramesPerSecond,
TotalTicksEngineRunning, TotalSecondsEngineRunning, Configuration, State,
Managers, Input, UiDispatcher, EngineDispatcher, LastFrameBeforePause; static
Engine.Logger (an ILogger<Engine>).

Engine events (all Action unless noted): PreInitialization, PostInitialization,
InitializationComplete, BeforeBackgroundTasksExecute, AfterBackgroundTasksExecute,
BeforeFrameRender, AfterFrameRender, CPSCalculated
(Action<CyclesPerSecondCalculatedEventArgs>), Paused, Resumed, Disposing,
Disposed.

One cycle executes, in order (engine plugins get OnPreCycle / OnPreFrameRender /
OnPostFrameRender / OnPostCycle hooks around the same points — see PLUGINS):
   1. EngineDispatcher.Drain()          -- runs actions posted to the engine thread
   2. BeforeBackgroundTasksExecute event
   3. PreCycle Timer events
   4. Gamepad state refresh (throttled) then input pollers -- keyboard, mouse,
      touch, gamepad events fire HERE, against state read moments earlier
   5. Animator frame advancement        -- for every Tile in Tile.TilesAnimating
   6. Sprite movement                   -- SpriteManager.MoveSprites (paths, easing)
   7. Collision resolution              -- per scene layer
   8. Camera updates                    -- per render surface ViewManager
   9. AfterBackgroundTasksExecute event
  10. THROTTLE CHECK: if TargetFPS interval has not elapsed, skip 11-15
  11. BeforeFrameRender event
  12. DirectDrawingManager.UpdateAll    -- immediate-mode drawable state updates
  13. Render + present each non-GPU render surface
  14. AfterFrameRender event
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
    engine thread executes inline. PostAsync(Func<Task>) is the awaitable form —
    it returns a Task that completes when the posted work does (and faults with
    whatever it threw). Only the START is marshalled onto the engine thread:
    continuations after the action's first await resume on whatever context that
    await captured, so engine state is NOT automatically safe to touch after one.
    Never await it FROM the engine thread — that blocks the very cycle that has
    to drain the queue.
    IEngineDispatcher: IsOnEngineThread, Post(Action), PostAsync(Func<Task>),
    Drain(), BindToCurrentThread().

  UI THREAD
    Runs: XAML layout/input, GameSurfaceCanvas painting, CPSCalculated,
    PreInitialization/PostInitialization/InitializationComplete, Disposing/
    Disposed (all posted via the UiDispatcher captured at Start). To get onto
    it from the engine thread:
        Engine.Instance.UiDispatcher?.Post(() => { ...UI... });
    NEVER touch XAML elements from the engine thread directly.
    IUiDispatcher: IsOnUIThread, Post(Action) (async, preferred), Send(Action)
    (synchronous). The Host implementation is CodeBrixPlatformUiDispatcher
    (DispatcherQueue) with static ForCurrentThread() (null when the calling
    thread has no dispatcher queue).

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
  NEXT Pause() replaces it; copy it to keep it longer. GPU (GpuRendering)
  surfaces are captured too, from the adapter's copy of the most recently
  presented frame (null only if the surface never presented a frame).

  Skia-free access — LastFrameBeforePauseAsRgba(out int width, out int height)
  on all three (Engine, RenderSurfaceHostBase, PixelFramePresenter) returns the
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
  animators, direct drawings, particles, display effects, radial-light flicker,
  the cycle clocks) is shifted past the paused interval BEFORE the loops wake —
  the pause is invisible to game time. No sprite teleports, no timer bursts, no
  animation churn, no effect that bursts to completion on the first resumed frame.
  TotalTicksEngineRunning / TotalSecondsEngineRunning EXCLUDE paused time (the
  value holds still while paused). FixedRateGameLoop re-baselines its schedule
  on resume: no catch-up burst, nothing counted in DroppedTics.

The one rule games must respect:
  Engine input pollers DO NOT RUN while paused, so a game cannot un-pause
  itself through engine input. Wire the resume trigger at the hosting
  application's UI layer — the window's restore/visibility event, a UI-level
  KeyDown, or a UI-level PointerPressed (the ParticleTest sample's campfire
  toggle, OnCanvasPointerPressed, is the worked example — see WORKING EXAMPLES
  ON GITHUB). The obvious application wiring: minimize ->
  Engine.Instance.Pause(), restore -> Engine.Instance.Resume().

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
The one control games render into is GameSurfaceCanvas (a SKXamlCanvas
subclass in the Host assembly), placed in a XAML page:

    xmlns:game="using:CodeBrix.Platform.GameEngine.Host.Rendering"
    <game:GameSurfaceCanvas x:Name="GameCanvas" />

Key members:
    FirstStarted            -- FirstStartedEventHandler(object sender,
                               FirstStartedEventArgs e); e.NewSize is the
                               first non-zero layout size. Fires ONCE.
                               START YOUR GAME FROM THIS EVENT; before it, the
                               surface has no real size.
    SetRenderResolution(int width, int height)
                            -- pins the engine render resolution; frames are
                               aspect-fit letterboxed into the control. Call
                               BEFORE first access to Host. Non-positive values
                               track the control size instead.
    UseGpuRendering         -- opt-in to GpuRendering (GPU); set BEFORE
                               first access to Host, like SetRenderResolution.
                               Default false = CpuRendering (CPU). See RENDER MODES.
    Host                    -- the RenderSurfaceHost<BackbufferBase> the engine
                               renders into (Mode A); its backbuffer is a
                               BitmapBackbuffer (CpuRendering) or GpuBackbuffer
                               (GpuRendering). Host.Bind(Scene newScene, bool
                               limitCameraToWorldBoundPx = true) connects a scene.
                               ONE HOST PER SCENE: Bind throws
                               InvalidOperationException if that scene is already
                               bound to a different host (see VIEWS AND CAMERAS).
    RenderSurfaceAdapter    -- the RenderSurfaceAdapterBase in use (CPU or GPU).
    UsePixelFramePresenter()-- switches the canvas to presenter mode (Mode B);
                               returns the PixelFramePresenter.
    EnsureFocus()           -- makes the canvas reliably keyboard-focusable:
                               tab stop + focus-on-load + refocus-on-press.
    WindowToBuffer(Point) / BufferToWindow(Point) -- pointer coordinate mapping
                               across the letterbox (presenter mode); both
                               return a nullable Windows.Foundation.Point.
    SetPointerCursorHidden(bool hidden) -- hide/restore the cursor over the canvas.

During a live window resize the canvas suppresses engine presents and re-blits
the last frame at the new size; live presenting resumes ~500 ms after the size
settles. Do not fight this by forcing refreshes from resize handlers.

RENDER MODES: CpuRendering (CPU, default) vs GpuRendering (GPU, opt-in) — Mode A only
--------------------------------------------------------------------------------
    GameCanvas.UseGpuRendering = true;      // BEFORE first access to Host
    GameCanvas.SetRenderResolution(1280, 720);   // optional, works either way

  CpuRendering (default): the engine rasterises the scene on the CPU into a
  BitmapBackbuffer on the engine thread; the adapter
  (CodeBrixPlatformBitmapRenderSurfaceAdapter) blits it to the canvas.
  Dirty-rectangle present optimisation applies. Right for most 2D tile games.

  GpuRendering (opt-in): the scene is rasterised BY THE GPU into a GpuBackbuffer
  through a backend-neutral off-screen Skia GPU context (SkiaGpuContext, in
  CodeBrix.Platform.Graphics3DGL) — GpuRendering-OpenGL on the Windows, X11,
  Wayland and Frame Buffer heads (OpenGL/GLES), GpuRendering-Metal on macOS
  (Skia-on-Metal, since the stock SkiaSharp macOS binary has no OpenGL(ES)
  interface). The frame is then read back to CPU pixels once and presented
  through the same canvas path — letterboxing, resize behaviour,
  SetRenderResolution, and input mapping are identical either way. The engine
  loop never touches GPU-thread surfaces; the adapter
  (CodeBrixPlatformGpuRenderSurfaceAdapter) drives one GPU frame on the UI thread
  per engine frame notification (TargetFPS cadence, coalesced latest-wins). The
  full surface is re-rendered every frame (no dirty-rectangle path on GPU).
  * Worth it when GPU raster beats CPU raster for the scene: heavy blending,
    scaling/rotation, full-surface shader effects (SKRuntimeEffect/SkSL runs
    ON the GPU — the GpuRender sample's plasma runs ~60 FPS on GpuRendering vs
    single-digit FPS on CpuRendering at 1024x640). A plain tile blit may not benefit.
  * EngineConfiguration.MsaaSampleCount applies (surface re-init on change);
    VSync has no effect on this adapter (no swap chain — pacing comes from
    TargetFPS); CPSCalculated reports the actual rendered GPU FPS (GpuFps).
  * Dirty-region rendering is OFF for a scene bound to a GPU host: binding
    clears every layer's refresh queue and closes it, so nothing accumulates,
    and DirectDrawing.ForceRefresh() is a no-op there (correct — the GL path
    repaints the whole viewport every frame anyway). Scene.UsesDirtyRegionRendering
    reports which regime a scene is in. Games that call ForceRefresh() every
    frame in a view-mode drawable are safe under either backbuffer.
  * RenderBackbufferPostScene and custom DirectDrawingBase.OnDraw run on the
    UI thread with the GRContext current under GpuRendering — never marshal that
    canvas elsewhere; keep OnDraw a pure function of engine time/game state.
  * Pause: fully supported — rendering parks, LastFrameBeforePause is captured
    (from the adapter's latest presented frame), and one paused-overlay frame
    is rendered after the Paused handlers run, same as CpuRendering.
  * When no GPU context is available (no driver / no GPU support, or macOS in
    software-rendering mode) the adapter logs one warning and falls back to
    CPU-rendering the GpuBackbuffer's fallback surface (the game still runs);
    IsGpuInitialized (bool?, null until the first attempt) on the adapter
    reports the outcome, and the init log records the chosen backend ("GPU
    rendering initialized (backend: Metal)").
  * WINDOWS: GpuRendering needs a real OpenGL driver (ICD). Most x64 machines
    have one from their GPU vendor; many Windows-on-ARM devices do NOT, and get
    OpenGL only from Microsoft's free "OpenCL and OpenGL Compatibility Pack"
    (Microsoft Store: https://apps.microsoft.com/detail/9NQPSL29BFFF). Without
    it the head cannot create a GPU context and GpuRendering silently uses the
    CPU fallback above — the warning it logs points the user at that pack
    (Windows only). Installing it is a one-time, per-device end-user step.
  * Unload/reload aware: when the canvas leaves the visual tree (window close or
    page navigation) the adapter stops driving frames and tears the GPU surface
    and context down while the owning window is still alive — on WGL the
    off-screen context is bound to a window's device context, so Unloaded is
    the last moment teardown can reliably run. When the canvas returns, the
    context is rebuilt lazily on the next frame notification. The CpuRendering
    adapter and the Mode-B presenter likewise stop scheduling canvas paints
    while unloaded, so an engine still cycling after the last window closed no
    longer keeps the process alive (stopping the engine on close remains the
    application's job, but exit no longer depends on it).
  * The mode is fixed once Host is created; presenter mode (Mode B) is CPU-only.

MODE A WALKTHROUGH 1: DERIVING FROM CodeBrixGameHost (recommended)
--------------------------------------------------------------------------------
CodeBrixGameHost (Host assembly) wires the canvas, input adapters, scene
binding, and engine start; the game overrides content hooks.
Initialize(string? configPath = null, bool? autoSaveConfig = null,
LogLevel logLevel = LogLevel.Warning) runs this fixed sequence — override what
you need, in the order it fires:

    OnInitializing
    ConfigurePlatform  -> OnConfigurePlatform
    ConfigureInput     -> keyboard/mouse/touch adapters wired to the canvas,
                          OnKeyboardAdapterInitialized / OnMouseAdapterInitialized /
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

Each hook in that sequence fires EXACTLY ONCE per host, OnSceneBound included:
build the game object graph there without a re-entry guard.

Members you get: Engine (=> Engine.Instance), Scene, RenderSurface (the
GameSurfaceCanvas), OnRenderSurfaceResized(int width, int height),
OnEnginePaused() / OnEngineResumed(), OnDisposing() / OnDisposed(), Dispose().

ConfigureTouch is sealed. To make desktop mouse input ALSO arrive as touch
contact 0 (it does not by default), override the protected virtual property:

    protected override bool EmulateMouseAsTouch => true;

SoftwareRenderedGameHostBase carries the same override (see INPUT).

Minimal game skeleton (the Spot.Brix sample is the full worked example):

    public sealed class MyGameHost : CodeBrixGameHost
    {
        public MyGameHost(GameSurfaceCanvas canvas) : base(canvas) { }

        protected override void LoadAssets() { /* AudioResourceManager, AssetsFile... */ }
        protected override void LoadTilesheets() { /* TilesheetRegistry... */ }
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
Derive, implement the abstract members, construct with the canvas and tic
rate, call Initialize(LogLevel logLevel = LogLevel.Warning) from FirstStarted
(the SoftRender sample is the full worked example — 320x200 plasma+starfield
at 70 Hz with raw-PCM audio):

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

        protected override void ConfigureGamepads()   // opt-in; needs the
            => _gamepads = Engine.Instance.InitializeSdlGamepadManager(); // Sdl2 package

        protected override void OnTic() { /* one tic of game logic */ }

        protected override void OnRenderFrame(Span<byte> frame)
            { /* fill 320*200*4 bytes; presented when this returns */ }

        protected override void OnShutdown() { /* teardown */ }
        protected override void OnEnginePaused() { /* pause frame / save */ }
        protected override void OnEngineResumed() { }
    }

Members you get: RenderSurface (GameSurfaceCanvas), Presenter
(PixelFramePresenter), GameLoop (FixedRateGameLoop), ConfigureInput() (virtual;
wires the keyboard adapter), Dispose() (stops and disposes the loop, calls
OnShutdown, disposes the keyboard adapter and presenter — it does NOT dispose
the Engine singleton).

Per tic, on the dedicated game-loop thread, the base runs:
    InputPump.PollNow() -> OnTic() -> OnRenderFrame(buffer) -> present.
PollNow refreshes gamepad state and then runs every poller, so a Mode-B game
gets the same gamepad behavior — hotplug included — as a Mode-A one.

FixedRateGameLoop(int ticsPerSecond, Action onTic) semantics the game can rely
on:
  * Non-drifting fixed timestep: each tic's target advances by exactly one
    period; scheduling lag does not accumulate.
  * Bounded catch-up: at most MaxCatchUpTics (default 5) back-to-back tics;
    a longer stall re-baselines and counts DroppedTics instead of bursting.
  * Sleep+yield hybrid pacing — an idle loop does not burn a core.
  * Start()/Stop(), Pause()/Resume(), WaitUntilPaused() (and the global engine
    pause via PauseWithEngine, which the host base enables) park after the tic
    in progress and resume with a re-baselined schedule: no burst, nothing
    dropped. IsRunning / IsPaused / TargetTicsPerSecond report state.
  * ActualTicsPerSecond / TicCount / DroppedTics for health monitoring.
  * A callback exception stops the loop, lands in LastException, and raises
    UnhandledException (Action<Exception>) — a Mode-B game should log it (the
    SoftRender host shows the pattern).

PixelFramePresenter details:
  * Configure(width, height, format {Rgba8888,Bgra8888}, orientation
    {Identity,Rotate90}, scaleMode {Fit,Stretch,PixelPerfect,Center},
    filterQuality). Reconfigurable at any time from the game thread (e.g.
    320x200 <-> 640x400). IsConfigured, FrameWidth, FrameHeight, Format,
    Orientation, ScaleMode, FilterQuality read back the current setup.
  * PresentFrame(ReadOnlySpan<byte> | uint[] | ReadOnlyMemory<byte>): any
    thread, once per tic, exactly width*height*4 bytes; one full-frame copy,
    zero per-frame managed allocations, latest-frame-wins triple buffering.
  * Rotate90 displays column-major (transposed) buffers with NO CPU transpose
    — for column-major renderers.
  * WindowToBuffer(SKPoint) / BufferToWindow(SKPoint) map pointer coordinates
    across the letterbox (exposed on the canvas too, in XAML points).

TIMERS AND THE PER-CYCLE EVENTS (Mode A)
--------------------------------------------------------------------------------
Timers (CodeBrix.Platform.GameEngine.Timers.Timer) fire on the engine thread
inside the cycle:

    var t = Timer.Add("spawner", TimerType.PreCycle, TimerCycles.Repeating, 2.5);
    t.Tick += () => SpawnWave();          // every 2.5 s of engine time
    Timer.Remove("spawner");              // or t.Dispose()

    static Timer Add(string timerID, TimerType type, TimerCycles cycles, double length)
    static Timer Add(TimerType type, TimerCycles cycles, double length)
    static void Remove(string timerID);  static void ClearAll();  static bool PausedAll

  * TimerType.PreCycle fires at step 3 (before input/movement); PostCycle at
    step 15 (after rendering). TimerCycles.Once auto-removes after firing, and
    fires EXACTLY ONCE even when the engine was stalled long enough for several
    of its intervals to elapse.
  * Repeating timers are schedule-preserving: a late cycle does not shift the
    next due time (no drift), and a missed interval fires as soon as possible.
  * `length` is VALIDATED: both Timer.Add overloads throw
    ArgumentOutOfRangeException when it is not finite (NaN, +/-Infinity), is
    zero or negative, is shorter than one high-resolution tick, or is so large
    it does not fit a positive Int64 of ticks. (Timer.Add(..., 0) used to spin
    the engine thread.)
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
  KeyboardEventPoller signatures:
      StartMonitoringKey(int keyCode, string? displayName = null,
                         double timeBetweenEvents = -1, bool isPaused = false)
      StartMonitoringKeys(IEnumerable<int> keyCodes, double timeBetweenEvents = -1)
      StartMonitoringAllKeys(double timeBetweenEvents = -1)
      StopMonitoringKey(int keyCode) / StopMonitoringKey(string key)
      StopMonitoringAllKeys();  AllKeyConfigs;  PauseAllKeyEvents;  Adapter
  Key codes are Windows VirtualKey values (Windows.System.VirtualKey) cast to
  int. KeyDownEventArgs carries KeyConfig (KeyEventConfiguration), KeyCode,
  KeyAction (Pressed/Released/Repeated), Modifiers (KeyboardModifierState) and
  IsShift/IsCtrl/IsAlt.
  MouseEventPoller: StartMonitoringMouse(bool trackMouseMovement = true,
  double timeBetweenEvents = -1, bool isPaused = false), StopMonitoringMouse(),
  event MouseEvent (Action<MouseEventArgs>), plus polled properties
  CurrentPosition, ButtonStates (IReadOnlyDictionary<MouseButton,
  MouseButtonState>), ScrollDelta, CurrentKeyboardModifiers. MouseEventArgs
  has edge helpers (LeftButtonJustPressed, ...).
  MouseEventArgs.Tick / TouchEventArgs.Tick carry the HighResTimer tick of the
  poll that raised the event, for input timing that cannot be reconstructed
  afterwards (0 when the raiser did not supply one).
  Event pacing: Configuration.TimeBetweenKeyboardEvents /
  ...Mouse/Touch/GamepadEvents (default 0.03 — SECONDS, despite a few older
  doc comments saying milliseconds) throttle repeat delivery; per-key
  overrides via the StartMonitoring* timeBetweenEvents parameter.
  Start/StopMonitoring* registrations are queued and applied at the next
  poll — not instantaneous.

  THE MOUSE THROTTLE IS REAL (it silently did nothing in earlier versions).
  With the 0.03 s default, at most one mouse event per 30 ms reaches the game.
  Set Configuration.TimeBetweenMouseEvents = 0 for an event on every cycle
  (mouse-look, drawing tools, anything sampling the pointer path). A press and
  release that both fall inside one 30 ms window are collapsed. Automated UI
  tests must therefore HOLD the button for ~300 ms or more — a synthetic click
  as short as xdotool's default (~12 ms press-to-release) is dropped entirely.
  Human clicks are far longer and are unaffected.
  Also gone: the phantom "scroll delta 0" event that used to follow every real
  scroll. A scroll event is raised only when the delta is non-zero AND differs
  from the previous poll's delta.

  POLLING: IKeyboardAdapter.IsDown(int keyCode) (reach it via
  KeyboardEventPoller.Adapter or your own adapter reference) is lock-free and
  valid from any thread at any time — the per-tic gameplay path for held keys
  (movement). IMouseAdapter exposes CurrentPosition, PressedButtons
  (HashSet<MouseButton>), CurrentKeyboardModifiers, ScrollDelta. Gamepads: read
  IGamepadAdapter (LeftStick/RightStick as GamepadStickState?, LeftTrigger/
  RightTrigger, PressedButtons, GamepadId) straight off
  Engine.Instance.Input.GamepadManager.ConnectedAdapters. NEVER call
  IGamepadManager.Update() yourself — the engine refreshes gamepad state in
  BOTH modes (engine cycle step 4, or InputPump.PollNow in Mode B), throttled
  by Configuration.TimeBetweenGamepadStateUpdates. A game that calls it too gets
  unthrottled native device polling on top of the engine's. A gamepad BACKEND
  is a separate package — see the Sdl2 AGENT-README named in OVERVIEW.

  TOUCH AND GESTURES: Engine.Instance.Input.TouchEventPoller (TouchEventPoller,
  which implements ITouchInput):
      ActiveTouches : IReadOnlyList<TouchPoint>
      events TouchBegan / TouchMoved / TouchEnded : EventHandler<TouchEventArgs>
      event TouchEvent : Action<GestureEventArgs>     -- every recognized gesture
      TapRecognizer / SwipeRecognizer / PinchRecognizer (built in, always wired)
      StartMonitoringTouch(double timeBetweenEvents = -1, bool isPaused = false)
      StopMonitoringTouch();  Adapter;  Configuration
  TouchPoint(int Id, Point Position, TouchPhase Phase) is a readonly record
  struct; TouchPhase = Began, Moved, Stationary, Ended, Cancelled.
  TouchEventArgs: Touch (TouchPoint), Tick (long).

  TOUCH LIFECYCLE EVENTS ARE NEVER THROTTLED. TimeBetweenTouchEvents paces
  TouchMoved only; Began, Ended and Cancelled always get through, so a tap that
  starts and finishes between two polls is no longer lost. A contact the poller
  first sees mid-gesture is normalized to Began before anything else sees it.
  Pausing or stopping the poller CLEARS contact and recognizer state, so a
  finger held across Pause/Resume cannot complete into a phantom tap or swipe.

  Recognizers (each is constructed over an ITouchInput and is IDisposable;
  the poller owns its three, so a game normally just subscribes). All timing is
  engine-tick based, so paused time never counts toward a gesture, and a second
  contact CANCELS a pending tap or swipe candidate:
      TapGestureRecognizer   -- MaxTapDurationSeconds (0.3), MaxTapMovementPixels
                                (20); event Tapped : EventHandler<TappedEventArgs>
                                (Position, TouchId). The movement test is applied
                                at the END position too, so a drag that produced
                                no TouchMoved event is still rejected; a swipe
                                that qualifies wins the arbitration.
      SwipeGestureRecognizer -- MinimumSwipeSpeedPixelsPerSecond (200) AND
                                MinimumSwipeDistancePixels (30) must both be met,
                                so a short fast tap is no longer also reported as
                                a swipe; event Swiped : EventHandler<SwipedEventArgs>
                                (Direction : SwipeDirection Right/Left/Up/Down,
                                StartPosition, EndPosition, SpeedPixelsPerSecond)
      PinchGestureRecognizer -- a full lifecycle: PinchStarted, PinchUpdated and
                                PinchEnded, all EventHandler<PinchedEventArgs>.
                                PinchedEventArgs carries Phase (PinchPhase
                                Began/Updated/Ended), TouchIds, Center,
                                StartingDistance, PreviousDistance, CurrentDistance,
                                ScaleDelta and TotalScale (current / starting
                                distance). The old two-argument constructor
                                (scaleDelta, currentDistance) is still there.
  GestureEventArgs wraps one of them: GestureType (Tap/Swipe/Pinch), IsTap/
  IsSwipe/IsPinch, and the Tap/Swipe/Pinch payload (the others null).
  The engine-side seam is ITouchAdapter — ActiveTouches, ConsumeEndedTouches()
  and ConsumeBeganTouches() (a default-interface method, so existing custom
  adapters still compile; implement it to report begins that ended before the
  next poll). The Host's CodeBrixTouchInputAdapter implements all three over a
  UIElement.

  DESKTOP MOUSE IS NOT TOUCH by default. CodeBrixTouchInputAdapter(UIElement
  element, bool emulateMouse = false) ignores mouse pointers unless emulateMouse
  is true; a desktop click raises mouse events only. To opt back in, pass
  emulateMouse: true to the adapter or to
  Engine.InitializeCodeBrixTouchAdapter(element, emulateMouse), or override
  EmulateMouseAsTouch on CodeBrixGameHost / SoftwareRenderedGameHostBase.

  POLLER TEARDOWN: MouseEventPoller and TouchEventPoller are both IDisposable
  and both expose a static Reset() that disposes the current instance (and its
  adapter) and clears the singleton. Engine.Dispose() calls both, so the
  platform adapters really unsubscribe from the canvas; assigning
  Engine.Instance.Input.TouchAdapter = null tears the touch poller down on its
  own. Initialize(...) on either poller disposes whatever was there before.

  INPUT EVENT CONFIGURATION: every registration is an
  InputEventConfigurationBase (TimeBetweenEvents in seconds; IsPaused):
      KeyEventConfiguration(string key, double secondsBetweenEvents = 0,
                            bool isPaused = false)             -- Key
      MouseEventConfiguration(bool trackMouseMovement,
                            double secondsBetweenEvents = 0,
                            bool isPaused = false)             -- TrackMouseMovement
      TouchEventConfiguration(double secondsBetweenEvents = 0, bool isPaused = false)
      GamepadButtonEventConfiguration(string button, double secondsBetweenEvents = 0,
                            bool isPaused = false)             -- Button
  Set IsPaused on one configuration to mute that registration without
  unregistering it; PauseAllKeyEvents (keyboard) and PauseAllInput (gamepad
  poller) mute a whole poller. The live registrations are readable:
  KeyboardEventPoller.AllKeyConfigs, MouseEventPoller.Configuration,
  TouchEventPoller.Configuration, GamepadEventPoller.AllButtonConfigsByGamepadId.

  Wiring adapters (Host extension methods in CodeBrix.Platform.GameEngine.Host.
  EngineExtensions, canvas-based):
      Engine.Instance.InitializeCodeBrixKeyboardAdapter(UIElement element);
      Engine.Instance.InitializeCodeBrixMouseAdapter(UIElement element,
                                  MouseEventConfiguration? mouseEventConfiguration = null);
      Engine.Instance.InitializeCodeBrixTouchAdapter(UIElement element,
                                  bool emulateMouse = false);
  CodeBrixGameHost and SoftwareRenderedGameHostBase do this for you. The
  adapter classes themselves are public if you need them directly:
      CodeBrixKeyboardAdapter(UIElement element) : IKeyboardAdapter, IDisposable
          IsDown(int keyCode); CurrentKeyboardModifiers;
          static int? GetKeyCodeFromString(string keyName)
      CodeBrixMouseAdapter(UIElement element) : IMouseAdapter, IDisposable
          CurrentPosition; PressedButtons; CurrentKeyboardModifiers;
          ScrollDelta (reading it returns the accumulated delta and resets it)
      CodeBrixTouchInputAdapter(UIElement element, bool emulateMouse = false)
          : ITouchAdapter, IDisposable
          ActiveTouches; ConsumeEndedTouches(); ConsumeBeganTouches()
  Or pass your own adapters to Engine.Initialize(...) (see LIFECYCLE).

  FOCUS: keyboard input reaches the canvas only while it HAS focus. Call
  canvas.EnsureFocus() (host bases apply it) — and remember that clicking any
  other control (a toolbar button) steals focus; hand it back (see the
  Spot.Brix MainPage for the toolbar refocus recipe). KeyDown bubbles from the
  FOCUSED element: a handler attached to the canvas never sees keys while a
  sibling control is focused.

  RELATIVE MOUSE (FPS mouse look): RelativeMouseSession(GameSurfaceCanvas
  renderSurface) over MouseDevice.MouseMoved — Begin() (hide + confine +
  accumulate), per-tic ConsumeDelta() -> (int DeltaX, int DeltaY), End(),
  IsActive; Dispose() ends it. Inactive (logged) on platform versions without
  relative mouse support.

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

  SHORT-EFFECT PRELOAD (automatic): container-format sounds (.wav/.mp3/.ogg/
  .flac) no longer than AudioResourceManager.PreloadShortSoundEffectMaxSeconds
  (default 10 s; 0 disables) are decoded ONCE to raw float PCM in memory at
  load time (AudioResource.IsPreloaded == true; the CachedSound type). Plays,
  Clone()s, SoundChannel clips, and SfxVoicePool voices over a preloaded
  resource share that single decoded buffer — no decode, file, or MP3 work
  ever happens on the real-time audio thread. Ogg Vorbis and FLAC decode
  through the same managed path as WAV and MP3, which matters because free
  asset packs ship .ogg almost exclusively. When the app pinned the device
  format (AudioSystem.Initialize) the decode also rate-converts up front.
  Longer material (music, ambience) keeps its streaming reader — leave it that
  way; preloading minutes of PCM would waste memory for a single voice.

  RAPID-FIRE SFX — SfxVoicePool: route sound-effect TRIGGERS (shots, pickups,
  impacts) through a fixed-size voice pool instead of playing the
  AudioResource itself per trigger:
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

  FORMATS: .wav, .mp3, .ogg (Vorbis) and .flac are built in, all fully
  managed, so assets never need converting for a particular target. ANY OTHER
  format registered with CodeBrix.Audio works too, with no engine change:
  PlatformAudioFactory resolves an extension against its OWN table first and
  then CodeBrix.Audio's AudioFileReaderRegistry. That is how .opus works —
  Opus is BSD-3-Clause and this engine is MIT, so it ships as the separate
  CodeBrix.Audio.Opus.BsdLicenseForever package; the APPLICATION references it
  and calls CodeBrixAudioOpus.Register() once at start-up, BEFORE anything
  loads a .opus asset, and from then on .opus is first-class on every path a
  built-in format reaches (including CachedSound preload and the SFX pool).
  Miss the call and the load throws a NotSupportedException that names the
  package and the method. PlatformAudioFactory.Register(ext, factory,
  requiresFile) adds an engine-only reader, or overrides a built-in one.

  THE MIXER AND ITS BUSES — AudioMixer (static): MasterVolume, MusicVolume and
  SfxVolume, the three sliders a settings screen expects. A voice's audible
  gain is its own Volume x its bus x MasterVolume. Changing a bus reaches
  everything already playing on it — there is no walking of live voices, and
  nothing to re-apply after a slider moves. All three default to 1.0, so a
  game that never touches AudioMixer sounds exactly as it did before buses
  existed. Bus defaults: AudioResource, SoundChannel and pool voices are Sfx;
  StreamingAudioSource is Music (it is the endless-material path); everything
  MusicManager plays is Music. Each exposes a Bus property (AudioBus) to
  override. MusicDuckMultiplier is read-only here and owned by MusicManager's
  ducking — duck through that, never by writing MusicVolume, so the player's
  own setting survives and can be restored.

MUSIC (MusicManager)
--------------------------------------------------------------------------------
MusicManager.Instance is to music what SfxVoicePool is to sound effects: the
place the policy lives, so a game does not reimplement fade timing and
"which track is current" bookkeeping. Everything it plays is on AudioBus.Music.

  WHAT DRIVES THE FADES: ONE background thread (a 20 ms fade ticker). A
  thread rather than engine Timers because fades must behave identically in
  both hosting modes and MODE B NEVER RUNS THE ENGINE CYCLE. It is not started
  until the first fade, parks on a wait handle when idle rather than spinning,
  and allocates nothing per tick. It FREEZES with the global engine pause, so a
  two-second crossfade spanning a ten-minute pause is still two seconds.

  TRACKS — a track is a HANDLE, not a transport. Read its state and set its
  Volume; play/stop/crossfade/seek through the manager, which owns the fades.
  Three kinds:
    * FileMusicTrack   — wraps an AudioResource, so it STREAMS from the loaded
                         data. The right choice for long linear music.
    * MidiMusicTrack   — a MIDI sequence rendered live through a SoundFont
                         (.sf2), an SFZ instrument (.sfz) or a Decent Sampler
                         instrument (.dspreset, .dslibrary, .dsbundle, or a
                         FOLDER holding a preset). Kilobytes on disk instead of
                         megabytes, and the arrangement can change while it
                         plays. SHARE THE INSTRUMENT via SoundFontCache,
                         SfzInstrumentCache or DecentSamplerInstrumentCache.
                         The (key, instrumentPath, midiFilePath) overload
                         resolves a Decent Sampler path through the
                         process-wide MidiMusicPlayer.SharedDecentSamplerCache,
                         so two tracks naming one library decode it once AND
                         SHARE ITS KNOBS; a SoundFont or SFZ named there is
                         loaded fresh each time. Pass the instrument in instead
                         for a part that must move its own knobs.
                         track.Problems lists what the instrument and the MIDI
                         file objected to, logged once at load and empty for a
                         clean pair; the PATH form reports the FILE's only,
                         because the player keeps the instrument it built and
                         does not hand it back — pass the instrument in when
                         both lists matter. A file that breaks a rule still
                         LOADS: the MIDI reader is lenient by default, so what
                         it had to work around (a note left sounding when its
                         track ended, say) is reported there rather than
                         thrown, and what it can simply ignore (a key signature
                         outside the range the specification allows, which
                         machine-generated files do write) is not reported at
                         all. MPE settings live on track.Player.
                         AN INSTRUMENT FROM AN ASSET PACK MUST REACH THE DISK,
                         EXCEPT .sf2: a .sf2 is one file and loads from a
                         Stream, a .sfz and a .dspreset REFERENCE sample files
                         beside them, and a .dslibrary or .dsbundle is one file
                         but is read IN PLACE BY PATH. Extract all but the .sf2
                         from an AssetsFile before loading.
    * MusicStemSet     — several recordings of one piece playing in lock, with
                         the game fading layers in and out. See ADAPTIVE STEMS.

  TRANSPORT: Play(track, fadeIn), CrossfadeTo(track, duration), Stop(fadeOut),
  Pause(), Resume(), Seek(), NowPlaying, IsPlaying, ActiveFadeCount.
  A CROSSFADE USES ONE FADE FOR BOTH SIDES — two independent fades could drift
  a tick apart and leave a hole or a bump in the middle; complementary values
  from a single progress cannot. CrossfadeCurve is EqualPower by default
  (a linear crossfade sits at 0.5/0.5 halfway, ~6 dB down, and audibly dips);
  choose Linear (MusicFadeCurve) for CORRELATED material (a stem swap, a loop
  splice), where it is the correct law.

  DUCKING: PushDuck(depth, attack, release) returns a handle; dispose it to
  release. Overlapping ducks are reference-counted and the DEEPEST wins, so two
  dialogue lines that overlap do not fight and the music returns only when the
  last one ends. Duck(depth, attack, hold, release) is the fire-and-forget
  form, and its hold runs on the same ticker, so a duck cannot outlive its cue
  just because the game was paused. Ducking is a SEPARATE multiplier from
  MusicVolume, so the player's slider survives. ClearDucks() is the escape
  hatch for a leaked handle — without it, a lost handle quietens the music for
  the rest of the process with nothing to point at. A scene change is a
  reasonable place to call it.

  STINGERS: PlayStinger(key, volume, duckMusic) plays a one-shot musical hit on
  its OWN voice on the music bus. Deliberately NOT through SfxVoicePool: the
  pool has a polyphony cap, and a level-complete fanfare culled by a busy
  combat scene is exactly the wrong outcome.

  PLAYLISTS: MusicPlaylist with MusicRepeatMode None/One/All, seeded shuffle,
  Add/Remove/Clear/Reset/MoveNext/MovePrevious. MusicManager.Play(playlist,
  crossfade) advances on each track's Ended; Next(crossfade) skips. Shuffle
  avoids replaying the track that just finished when it reshuffles at the wrap.

  THREADING: every method is safe to call from any thread. Track Ended events
  and playlist advances arrive on a BACKGROUND OR AUDIO THREAD — marshal to the
  engine thread with Engine.Instance.EngineDispatcher.Post before touching game
  state.

  SHUTDOWN: Engine.Dispose() tears the music system down with the rest of the
  audio. A Mode-B game that never disposes the engine calls
  MusicManager.Instance.Dispose() alongside AudioSystem.Shutdown().

  NOT PERSISTED: music is deliberately absent from EngineState. A saved "now
  playing at 1:23.4" is a promise the engine cannot keep across a soundfont
  reload, and audio playback position does not round-trip anyway. A game that
  wants it saves the track key itself. This is a decision, not an oversight.

  ADAPTIVE STEMS — THREE ROUTES, PICK DELIBERATELY:

    (a) MIDI, via per-channel volume. The cheap one, and the default answer for
        synthesized music:
            track.SetLayerVolume(channel, 0f);          // 0-15
            track.FadeLayerTo(channel, 1f, TimeSpan.FromSeconds(2));
        No second copy of anything, no shared-format requirement, and the
        layers CANNOT drift because there is only one sequence. Also
        SetLayerPan and Speed (a tempo multiplier that does not change pitch —
        slow-motion music, which a mixed-down file cannot do).
        CAVEAT: it is sent as MIDI control change 7, so a track that automates
        its own volume will overwrite the game's value the next time it does.
        Reserve the channels the game means to drive and leave their CC7 alone
        in the arrangement.

    (b) Audio files, via MusicStemSet. For recorded stems:
            var stems = new MusicStemSet("battle", "explore.ogg", "combat.ogg");
            MusicManager.Instance.Play(stems);
            stems["combat"].FadeTo(1.0f, TimeSpan.FromSeconds(2));
        A MusicStemSet IS a MusicTrack, so the manager plays, crossfades, ducks
        and stops it like any other music. Layers are summed into ONE voice, not
        N — independent voices start at slightly different times and drift, and
        layers that drift phase against each other. One voice, one clock, exact
        lock by construction.
        REQUIREMENTS AND COSTS:
          - Every stem must share a SAMPLE RATE and CHANNEL COUNT. A mismatch
            throws, naming the stem and both formats; it is not mixed anyway,
            because that would play a layer at the wrong speed. Calling
            AudioSystem.Initialize(...) makes this a non-issue — stems then
            rate-convert to the pinned device rate as they decode.
          - Stems SHOULD share a length. If they do not, the set loops as one at
            the LONGEST, a shorter stem is silent until then, and the mismatch
            is logged once.
          - Stems are DECODED TO MEMORY (~10 MB per stereo minute at 44.1 kHz,
            per layer). That buys exact lock and an audio thread that never
            decodes. Layered music is normally a short loop, which is what this
            suits; a long linear piece is a FileMusicTrack, which streams.
          - Only the FIRST stem starts audible; the rest start at 0 and are
            brought in deliberately.
        Gain changes RAMP across an audio block rather than stepping, because a
        step change in gain is a click. Summing is NOT limited: N stems at full
        sum to N, and stems are expected to be mixed so the combinations the
        game actually uses do not clip.

    (c) A Suno stems download, via MusicStemSet.FromSunoStems. Such a download
        is a set of files named "<Title> (<Stem>).wav" with the song's MIDI
        beside them, as a zip or as a folder — both load:
            var stems = MusicStemSet.FromSunoStems("battle", zipOrFolder,
                                                   "Drums", "Bass", "Guitar");
            MusicManager.Instance.Play(stems);
        Name the layers the game will actually cross-fade; pass none for every
        stem that carries audio. A name the export does not have throws,
        LISTING THE ONES IT DOES. From there it is an ordinary MusicStemSet,
        except that its Timeline is already filled in from the MIDI that ships
        beside the recordings — so bar-locked layer changes and bar-quantised
        transitions work with nothing else set up, and they follow the tempo
        exactly (a generated arrangement writes one tempo event per beat).
        MusicStemSet.Problems carries whatever the export could not account for
        — an unrecognised stem name, a stem of the wrong length, MIDI that
        would not read. It is logged once and never thrown.
        COST: every stem is decoded to memory, about 23 MB per stereo minute at
        48 kHz — so a four-minute song is about 92 MB PER STEM, and taking all
        ten of a full export is most of a gigabyte. Three or four named layers
        is the difference between a feature and a memory problem. Call
        AudioSystem.Initialize first so the decode converts to the device rate
        once (a download is 48 kHz whatever the game is running at).
        A ZIP IS UNPACKED ON DEMAND into a cache folder keyed by the download,
        so it is unpacked once and reused; SunoLoadOptions.CacheFolder chooses
        where, and a game that ships a download should point it at its own
        writable folder. A folder is read where it lies.
        THE FULL MIX IS NOT A STEM. When the download includes one it is a long
        linear piece: load it with AudioResourceManager and play it as a
        FileMusicTrack, which streams.
        ALIGNMENT MEASUREMENT IS FORCED OFF here. It lines a recording up with
        its MIDI, which is a MIDI concern, and it costs a decode of every stem;
        the recordings are already locked to each other.
        GETTING THE DOWNLOAD RIGHT: choose "Extract Stems and MIDI", Auto split,
        Full Song, and tick WAV and MIDI; set Tempo to "Follow tempo changes",
        which keeps the map that a fixed tempo would flatten. TAKE THE WAVs —
        they line up exactly with each other, where an MP3 carries an encoder
        delay. An export with only MP3s loads and plays; it is simply worse.

  QUANTISED TRANSITIONS (bar / beat) — the difference between music that
  changes when the game says so and music that changes when the MUSIC says so:
      MusicManager.Instance.CrossfadeTo(combat, TimeSpan.FromSeconds(2),
                                        MusicTransitionQuantize.Bar);
  Play, CrossfadeTo and Stop all take a MusicTransitionQuantize
  (Immediate / Beat / Bar). The wait rides on the fade ticker, so it freezes
  with the global pause: a transition queued for the next bar cannot fire while
  the game is paused. HasPendingTransition reports one in flight;
  CancelPendingTransition() drops it (the enemy died before the bar arrived),
  and starting any transition outright cancels a queued one so a stale change
  cannot land after the game changed its mind.

  WHERE THE GRID COMES FROM — MusicTrack.Timeline (a MusicTimeline):
    * MIDI loaded FROM A PATH fills it in completely. The MidiMusicTrack
      (key, instrumentPath, midiFilePath) overload parses the file a SECOND
      time as CodeBrix.Audio.Midi.MidiFile to read the tempo MAP, the time
      signature and the markers. That second parse is necessary, not lazy:
      MidiSequence — the thing that PLAYS — bakes the tempo map into absolute
      times and keeps no meta events, so the markers and the meter genuinely
      are not in it any more. A MIDI file is kilobytes and this happens once at
      load.
    * MIDI loaded from a MidiSequence fills it in from the sequence's OWN tempo
      map (MusicTimeline.FromMidiSequence), so the grid is exact there too —
      but FOUR BEATS TO THE BAR IS ASSUMED and there are no markers, because a
      sequence keeps its tempo map and not its meta events' timing. Set
      Timeline yourself for another meter, or read the FILE for markers.
    * A SUNO STEMS DOWNLOAD fills it in from the MIDI in the export
      (MusicStemSet.FromSunoStems, above): four beats to the bar, no markers.
    * DECODED AUDIO: the game supplies it —
          track.Timeline = new MusicTimeline(beatsPerMinute: 128, beatsPerBar: 4);
      There is NO inference from a decoded stream on offer. Beat detection is a
      guess, and a guess here produces transitions that are subtly and
      unfixably late. The composer knows the tempo.
    * No timeline + a Beat/Bar request = it happens immediately and says so in
      the log. It is never silently dropped.
  A beat is the tempo's own unit (a quarter note, for MIDI), so BeatsPerBar is
  fractional where the time signature's beat unit differs: 6/8 read from a MIDI
  file is 3 quarter-note beats to the bar, not 6.
  THE GRID FOLLOWS THE TEMPO. A timeline given a tempo in its constructor is a
  CONSTANT grid. A timeline built from MIDI carries the source's whole tempo map
  (MusicTimeline.TempoMap, a CodeBrix.Audio.Synth.MidiTempoMap) and quantises
  THROUGH it, so a beat or bar boundary is exactly where the file puts it
  however often the tempo moves. That matters because a machine-generated arrangement
  routinely carries ONE TEMPO EVENT PER BEAT, and quantising such a file against
  its first tempo alone would be off the beat within a few bars.
  HasTempoChanges says whether the source's tempo varies at all; SecondsPerBeat
  and SecondsPerBar describe the tempo the piece STARTS at, so ask
  TimeToNextBoundary rather than doing that arithmetic. Markers come through the
  same map, so a jump point and the bar line it sits on agree. A game can build
  the same thing itself: new MusicTimeline(tempoMap, beatsPerBar).

  JUMP POINTS: a MIDI file's markers (and cue points) become
  MusicTimeline.Markers (MusicMarker(string Name, TimeSpan Time)), and
  MusicManager.JumpToMarker("chorus") seeks the current track to one
  (case-insensitive; returns false rather than seeking somewhere arbitrary if
  there is no such marker).

SCENES, LAYERS, AND TILES (Mode A)
--------------------------------------------------------------------------------
The scene graph is Scene -> SceneLayer (a 2D tile grid) -> SceneLayerTile:

    var scene = new Scene();
    var layer = scene.AddLayer(columnCount: 8, rowCount: 8,
                               width: 64, height: 64,          // tile size px
                               zOrder: 0, parallax: 1f,
                               coordinateSystem: CoordinateSystemTypes.Orthogonal);
    layer[0, 0].CurrentFrame = tilesheet[4, 4];   // place a graphic on a cell

    SceneLayer AddLayer(int columnCount, int rowCount, int width = 32,
                        int height = 32, int zOrder = 0, float parallax = 1f,
                        CoordinateSystemTypes coordinateSystem = Orthogonal)
    SceneLayer AddLayer(SceneLayer sceneLayer);   void RemoveAllLayers()

  * Layers: ZOrder (lower renders behind), Parallax (1 = moves with camera,
    <1 background, >1 foreground), Visible, WrapHorizontally/Vertically,
    OriginPx (world origin of tile (0,0)), ShowGridLines/ShowCollisionBoxes
    (debug overlays). Prefer SetTileSize(w,h) over setting TileWidth and
    TileHeight separately (one refresh instead of two). Nearly every layer
    property setter forces a full scene refresh — batch changes.
  * Coordinate systems per layer (CoordinateSystemTypes): Orthogonal (0),
    IsometricRhombic (1), IsometricAxial (2), HexAxialFlatTop (3),
    HexAxialPointedTop (4), ObliqueRight (5), ObliqueLeft (6). The two oblique
    systems are sheared square lattices — columns stay horizontal while rows
    advance down and to the RIGHT (ObliqueRight) or down and to the LEFT
    (ObliqueLeft), giving a parallelogram tile footprint rather than an
    isometric diamond; tile art fits that footprint with transparent
    bounding-box corners. The CoordinateTest sample exercises the first five.
    Conversions: layer.GridToWorldPx / WorldPxToGrid / GetAdjacentTile(tile,
    CardinalDirections); tile indexers return null out of bounds (no
    auto-wrap — call WrapGrid first when wrapping).
    NAMING NOTE: the member formerly called `Oblique` is now `ObliqueRight`.
    Its numeric value is still 5 and the enum serializes as an int, so saved
    layers are unaffected — only source has to be retargeted.
    ISOMETRICAXIAL PACKING FIXED. IsometricAxial now uses a true affine basis
    (px = gx*W + gy*W/2, py = gy*H/2), so its rows pack edge to edge as real
    diamonds. It previously stepped anchors by the full tile size, laying the
    layer out like an orthogonal grid — so TILE ANCHORS MOVE, and hand-placed
    content on an IsometricAxial layer shifts. Re-check any such layer.
    HEX layers map FRACTIONAL grid positions to interpolated pixel anchors
    (including a half stagger) instead of snapping to the nearest cell, so a
    sprite tweening across a hex layer moves smoothly. Integer positions are
    unchanged.
  * Scene.CollisionProfiles is the scene's CollisionProfileRegistry and
    SceneLayer.DefaultTileCollisionProfile ("World") the profile applied to a
    layer's fixed tiles — see MOVEMENT, EASING, AND COLLISIONS.
  * SceneLayerTile: cells are created with the layer; assigning CurrentFrame
    places a tilesheet frame. Set EnableAnimator = true only on tiles that
    animate (it allocates an Animator per tile).
  * Scenes self-register globally (Scene.GetSceneByID / GetAllScenes) and
    must be Dispose()d (or Scene.ClearAllScenes()) or they linger there.
  * Scene.FullRefreshNeeded flags a full redraw; structural changes set it
    automatically.
  * Scene, SceneLayer and every Tile carry a TypedValueBag (ValueBag) for the
    game's own per-object data (see VALUE BAGS; not serialized).

VIEWS AND CAMERAS (Mode A)
--------------------------------------------------------------------------------
Each render surface has a ViewManager; each View pairs a Viewport (screen
rectangle + zoom) with a Camera (world position):

    host.ViewManager.ConfigureSingleFullView();          // the usual case
    host.ViewManager.ConfigureVerticalSplit(1f, 1f);     // split screen
    host.ViewManager.AddView(targetRectPx, zoom, zOrder); // custom

    void ConfigureSingleFullView(float zoom = 1f, int zOrder = 0)
    void ConfigureVerticalSplit(float leftZoom = 1f, float rightZoom = 1f)
    void AddView(Rectangle targetRectPx, float zoom = 1f, int zOrder = 0,
                 RectangleF? worldBoundsPx = null)
    void ClearViews();   ReadOnlyCollection<View> Views

  * Camera movement (every move clamps to WorldBoundsPx unless it is Empty):
    instant — SnapTo, CenterOn, CenterOnGrid, PanBy; smooth — PanTo/
    PanCenterTo(speed), PanToOverDuration/PanCenterToOverDuration(seconds),
    PanToGridOverDuration; following — FollowCentered(movable, speed, hard),
    FollowCenteredX/Y (axis lock), Follow(func), ClearFollow().
    FollowLerpPerSecond (default 8) sets follow snappiness; DeadZonePx gives
    the target wiggle room. Camera.PositionPx is read-only — move via methods.
  * Viewport zoom: Zoom > 1 magnifies (zooms IN), < 1 zooms out — the world
    rectangle a view shows is TargetRectPx / Zoom (VisibleWorldSizePx).
    All four View conversions, TextBlock's scene-layer text scale and
    ZoomAroundScreenPoint follow that sense. (Earlier versions applied the
    factor the other way round, against their own documentation; a game that
    compensated for the old direction must drop the compensation.)
        void SnapZoom(float zoom)                       -- immediate
        void ZoomTo(float targetZoom, float lerpPerSecond)      -- ease-to
        void ZoomToOverDuration(float targetZoom, float durationSeconds)
    ZoomToOverDuration is a true fixed-duration eased tween: it takes exactly
    that many seconds and LANDS EXACTLY on the target (no asymptotic crawl and
    no end-of-tween snap).
    View.ZoomAroundScreenPoint(layer, screenPoint, targetZoom, durationSeconds)
    gives map-style wheel zoom: the View owns the anchor and re-derives the
    camera every update, so the world point under the cursor stays under the
    cursor for the whole animation. It cancels an explicit camera pan but
    leaves camera FOLLOW intact. MinZoom 0.1, MaxZoom 8 per view.
  * Picking: view.ScreenPxToGrid(layer, screenPoint) turns a pointer position
    into a grid cell (Spot.Brix does exactly this for click handling); plus
    ScreenPxToWorldPx / WorldPxToScreenPx / WorldRectToScreenRect /
    ScreenRectToWorldRect, all parallax-aware.
    Called from INSIDE a render pass for that view (a custom
    DirectDrawingBase.OnDraw, RenderBackbufferPostScene), the conversions read
    the render pass's SNAPSHOT of camera position, viewport rect, screen offset
    and zoom, so one frame is never drawn with two different transforms. Game
    code on the engine thread keeps seeing live values.
  * host.Bind(scene, limitCameraToWorldBoundPx) auto-creates one full-surface
    view if none exist; ConfigureSingleFullView throws before the adapter
    exists — bind/configure from FirstStarted onward, on the UI thread.
  * ONE RENDER-SURFACE HOST PER SCENE. Bind throws InvalidOperationException if
    the scene is already bound to a different host, and a failed bind leaves
    both hosts on the scenes they already had. For several camera perspectives
    into one scene, add VIEWS to the one host (ViewManager.AddView), not a
    second host. Disposing a host — or the scene bound to it — releases the
    binding and unsubscribes the host from the scene's SceneDisposing event; a
    disposed host's Scene reverts to Scene.Empty.
  * host.RedrawDirtyRectangleOnly (default true) presents only dirty regions;
    host.Backbuffer.ClearColor sets the letterbox/background color.
  * host.RenderBackbufferPostScene (event Action<SKCanvas>) is a post-scene
    overlay hook — it runs on the engine thread for CPU surfaces but on the UI
    thread with the GPU context current for GPU backbuffers; never marshal
    that canvas elsewhere.

TILESHEETS, SPRITES, AND ANIMATION (Mode A)
--------------------------------------------------------------------------------
TILESHEETS: TilesheetRegistry.Instance is the named store —
    Tilesheet LoadFromImageFile(string name, string imageFilePath)
    Tilesheet LoadFromBitmap(string name, SKBitmap bitmap)
    Tilesheet LoadFromStream(string name, Stream stream)
    Tilesheet LoadFromAssetsFile(AssetsFile assetsFile, string entryName)
    Tilesheet LoadFromDefinitionFile(string gtsPath)
    Tilesheet LoadFromDefinition(TilesheetDefinition definition,
                                 string? baseDirectory = null)
    Tilesheet LoadFromDefinitionAsset(AssetsFile assetsFile, string gtsEntryName)
    bool TryGet(string name, out Tilesheet? sheet);  Tilesheet? GetOrNull(name)
    this[string name];  Remove(string name, bool dispose = false);  Names;
    Count;  GetAll();  Clear()
A .gts file is a JSON TilesheetDefinition (image source, regions, mask);
relative image paths resolve against the .gts directory.

    var sheet = TilesheetRegistry.Instance.LoadFromImageFile("spots", path);
    sheet.DefaultRegion.TileSize = new Size(93, 96);
    sheet.ApplyMask(Color.Black.ToSKColor());   // optional color-key transparency
    Frame frame = sheet[0, 0];                  // or sheet[regionName, x, y]

Sheets can carry multiple named Regions (AddRegion with area, tile size,
padding, margin, overhang, and the two collision defaults below; GetRegion(name);
RemoveRegion(name, dispose); this[regionName]); Frame is the (sheet, cell)
handle everything else consumes (GetFrame(x, y) / GetFrame(regionName, x, y)).
ApplyMask(SKColor? maskColor = null, byte tolerance = 5) sets
MaskColor/MaskTolerance; GetImage/GetBitmap(regionName, x, y) hand back one cell.

    TilesheetRegion AddRegion(string name, Rectangle area, Size tileSize,
                              Spacing? tilePadding = null, Spacing? regionMargin = null,
                              CollisionAdjust? collisionAdjust = null,
                              TileCollisionType collisionType = TileCollisionType.None)

COLLISION METADATA ON A TILESHEET. A region carries defaults that every tile
drawn from it inherits, and any individual cell may override them:
    region.CollisionAdjust   (CollisionAdjust, default None) region default inset
    region.CollisionType     (TileCollisionType, default None) region default type
    region.CollisionArea                              -- the region default applied
    CollisionAdjust GetFrameCollisionAdjust(int x, int y)
    bool TryGetFrameCollisionAdjustOverride(int x, int y, out CollisionAdjust value)
    void SetFrameCollisionAdjust(int x, int y, CollisionAdjust value)
    bool ClearFrameCollisionAdjustOverride(int x, int y)
    Rectangle GetFrameCollisionArea(int x, int y)
    TileCollisionType GetFrameCollisionType(int x, int y)
    bool TryGetFrameCollisionTypeOverride(int x, int y, out TileCollisionType value)
    void SetFrameCollisionType(int x, int y, TileCollisionType value)
    bool ClearFrameCollisionTypeOverride(int x, int y)
  * Assigning a FRAME value ALWAYS records an override, even when it equals the
    region default. Changing the region default re-applies only to frames that
    have no override — so hand-tuned cells survive a region-wide edit.
  * Frame mirrors the same view of one cell: Frame.CollisionAdjust,
    Frame.CollisionArea, Frame.HasCollisionAdjustOverride,
    Frame.ClearCollisionAdjustOverride(), Frame.CollisionType,
    Frame.HasCollisionTypeOverride, Frame.ClearCollisionTypeOverride().
  * Tilesheet's copy constructor copies both region defaults and every
    per-frame override.
    void PersistImageToFile(string imageFilePath,
                            SKEncodedImageFormat format = SKEncodedImageFormat.Png,
                            int quality = 100)
    promotes a bitmap-only (runtime-generated) tilesheet to a file-backed one:
    it writes the sheet's source bitmap and clears the asset identifier.

THE TILESHEET DEFINITION MODEL (.gts) — namespace ...Drawing.Tilesheets.GTS:
    TilesheetDefinition          Name, Image (TilesheetImageDefinition),
                                 Regions (List<TilesheetRegionDefinition>),
                                 Mask (TilesheetMaskDefinition?),
                                 PremultiplyAlpha, Source (TilesheetDefinitionSource)
    TilesheetImageDefinition     FilePath, or AssetsFilePath + AssetEntryName
    TilesheetRegionDefinition    Name (default region name), Area (Rectangle),
                                 TileSize (Size), TilePadding / RegionMargin /
                                 Overhang (Spacing), CollisionAdjust
                                 (CollisionAdjust, default None), CollisionType
                                 (TileCollisionType, default None),
                                 Frames (List<TilesheetFrameDefinition>)
    TilesheetFrameDefinition     XTile, YTile, CollisionAdjust? (null = inherit
                                 the region default), CollisionType? (same rule)
    TilesheetMaskDefinition      Red, Green, Blue, Alpha (255), Tolerance (5)
    TilesheetDefinitionSource    Kind (TilesheetDefinitionSourceKind: None,
                                 LooseDefinitionFile, PackedDefinitionFile,
                                 Generated), GtsFilePath, AssetsFilePath,
                                 AssetEntryName; factories LooseDefinitionFile(
                                 gtsFilePath), PackedDefinitionFile(assetsFilePath,
                                 assetEntryName), Generated(), None()
    TilesheetDefinitionSerializer (static)
        TilesheetDefinition Load(string filePath) / Load(Stream stream)
        void Save(string filePath, TilesheetDefinition definition)
        TilesheetDefinition FromJson(string json);  string ToJson(TilesheetDefinition)
        TilesheetDefinition FromTilesheet(Tilesheet tilesheet,
                                          string? baseDirectory = null,
                                          bool makePathsRelative = false)
        void Save(string filePath, Tilesheet tilesheet, bool makePathsRelative = true)
        string ToJson(Tilesheet tilesheet, string? baseDirectory = null,
                      bool makePathsRelative = false)
Round trip: build a Tilesheet in code, Save(path, sheet) writes the .gts; the
save system's separateGtsFiles option and LoadFromDefinitionFile use the same
format.

  * The .gts collision members are ADDITIVE and backward compatible: a file
    written before they existed loads with CollisionAdjust.None,
    TileCollisionType.None and an empty Frames list. TileCollisionType is
    written as a STRING ("None" / "Blocking" / "Trigger") in both .gts files
    and engine save files.
  * Save(filePath, Tilesheet, ...) MUTATES a bitmap-only tilesheet: a sheet with
    neither an image file path nor an asset identifier gets its bitmap
    auto-persisted as a sibling .png next to the .gts (same base name), and the
    sheet's ImageFilePath is re-pointed at that file, so the definition is
    loadable. File-backed and asset-backed sheets are left untouched. Pass a
    path you are happy to have a .png appear beside.
  * FromTilesheet writes one Frames record per frame coordinate, so a large
    region produces a large .gts (a 100x100 region = 10,000 records).

SPRITES: create ONLY via the manager (the constructor is not public):

    var sprite = SpriteManager.Instance.CreateSprite(sceneLayer, new Frame(sheet, 0, 0), "hero");
    sprite.Visible = true;
    sprite.SetPosition(new Vector2(5, 0));      // GRID cells, not pixels

    Sprite CreateSprite(SceneLayer sceneLayer, Frame frame, string? id = null,
                        string? collisionProfileName = null)
    Sprite CloneSprite(Sprite sprite) / CloneSprite(Sprite sprite, SceneLayer sceneLayer)
    Sprite? CloneSprite(string id, SceneLayer sceneLayer);  Sprite? GetSpriteByID(string ID)
    List<Sprite> GetSpritesAtViewPixel(...);  bool SizeNewSpritesToSceneLayer
    string DefaultCollisionProfile      -- "Actor"; see COLLISIONS below

  * Sprite positions are GRID coordinates on their scene layer; RenderSize,
    NudgeX/NudgeY, and CollisionArea are pixels.
  * ROTATION: Sprite.Rotation (float degrees, clockwise about the centre of the
    sprite's render rectangle, normalised to 0 <= r < 360; a non-finite value
    throws ArgumentOutOfRangeException). It is a RENDERING property — the
    collision rectangle stays axis-aligned — and it round-trips through save
    files. Sprite.VisualBoundsWorld and Sprite.GetVisualBoundsScreen(View) are
    the axis-aligned bounds that ENCLOSE the rotated sprite; dirty-region
    invalidation, SpriteManager's hit tests
    (GetSpritesInWorldRectRange / GetSpritesInViewRectRange /
    GetSpritesAtViewPixel) and the SceneLayer sprite query all use them, so a
    rotated sprite is picked and repainted correctly.
  * CompositeSprite.GetPosition() returns GRID coordinates, matching
    SetPosition and AddChildWithOffset (it used to return world pixels).
  * CloneSprite(sprite, layer) binds the clone's MovementController and collider
    to the DESTINATION layer (so wrapping and bounds use the right grid), copies
    the source's rotation and collision settings, and registers the clone with
    SpriteManager only once it is fully built.
  * TranslateWorldPx applies a GRID-SPACE delta, so a collision push-out no
    longer snaps a sprite to whole pixels on both axes.
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
  * CompositeSprite groups sprites (CompositeAnchorMode) and is itself an
    IMovableOnSceneLayer.

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
  * Animator events: Started, Stopped, Cycled (per frame advance;
    AnimatorEventArgs). Never call Animator.Dispose directly — the owning Tile
    does.
  * Static scene tiles animate too: set tile.EnableAnimator = true first.

MOVEMENT, EASING, AND COLLISIONS (Mode A)
--------------------------------------------------------------------------------
Every Sprite (and DirectComposite / movable direct drawing) has a .Movement
MovementController. Units are the mover's space — GRID cells for sprites,
PIXELS for direct drawings (MovementSpace.Grid / MovementSpace.Pixel); all
durations are seconds. Per-frame priority: Follow > Scripted > Integrated
physics.

DIRECT-DRAWING MOVEMENT RUNS IN REAL TIME. It advances once per engine update
by the real elapsed delta; there is no fixed-step accumulator and no per-update
cap, so MoveTo/MoveBy/Follow on a direct drawing keep the wall-clock duration
they were given at any update rate (they used to slow down below ~30 Hz). The
flip side: a non-pause stall — a debugger break, a very long GC — advances
movement by the real elapsed time rather than slowing it down. Engine.Pause()
is unaffected: paused time is shifted out on resume, as always.

  SCRIPTED (tweens):
    sprite.Movement.MoveTo(target, 0.4f, EasingKind.SmootherStep);
    sprite.Movement.MoveBy(delta, 10f, EasingFunctions.EaseInOutQuad);
    sprite.Movement.MoveToward(target, speedPerSec);   // constant speed
    sprite.Movement.CancelScript(); / StopAllMovement();
    PER-MOVE CALLBACKS (preferred over the events below): the Move* methods
    return the MovementController, so one move's hooks chain onto it —
        sprite.Movement.MoveTo(target, 0.4f, EasingKind.SmootherStep)
                       .OnBeginning(() => PlayASound())
                       .OnComplete(() => ArrivedAt(target));
    OnComplete fires when THAT move ends; OnBeginning fires SYNCHRONOUSLY as it
    is chained (the move has already started by then) and THROWS if no scripted
    move is active — do not chain it onto a move that may have snapped to the
    target instantly. The Spot.Brix sample is the worked example.
    events: ScriptedMovementStarted / ScriptedMovementStopped fire for EVERY
    move on the controller, so a handler has to work out which move it is
    hearing about and detach itself; prefer the per-move callbacks.
    PITFALL: MoveBy(delta, float, ...) has two meanings — with an easing
    argument the float is a DURATION (tween); without, it is a SPEED
    (constant velocity). Pass EasingKind/Func explicitly to get the tween.
    Read-only introspection: Movement.MovementState (MovementState: Velocity,
    Acceleration, MaxSpeed, LinearDamping, HasMotion, MovementSpace),
    Movement.IsScripted, Movement.IsIntegratedActive. The active script is a
    ScriptedMovement (Type: MovementScriptType None/TweenTo/Toward, Origin,
    Target, DurationSec, ElapsedSec, SpeedPerSec, SnapEpsilon, Easing).
  INTEGRATED (physics): SetVelocity, SetAcceleration, SetMaxSpeed,
    SetLinearDamping. Setting velocity/acceleration cancels a script;
    starting a script zeroes velocity/acceleration.
  FOLLOW: FollowPixelSoft/Hard(getPos, speed, offset),
    FollowTileSoft/Hard(target), Unfollow(). StopAllMovement() also clears
    follow state (its doc comment always said so; now it does).
    WrapX / WrapY have public setters.
  EASING: EasingFunctions.Linear, EaseIn/Out/InOutQuad|Cubic|Quart|Quint,
    SmoothStep, SmootherStep — or the EasingKind enum.

COLLISIONS:
  * A tile collides when its Tile.CollisionType is not None. CollisionsEnabled
    is a PROJECTION of that type: enabling collisions on a None tile promotes it
    to Blocking (or Trigger, if its collider already responds as a trigger);
    disabling resets the type to None. Both directions keep the collider's
    response type in step.
        TileCollisionType : None, Blocking, Trigger
        (serialized as a STRING in .gts files and engine save files)
  * COLLISION PROFILES name a group/mask pair so games do not hand-assemble
    bitmasks. Scene.CollisionProfiles is a CollisionProfileRegistry carrying
    four standard profiles (CollisionProfileNames.World / Actor / Projectile /
    Sensor):
        "World"       group WorldStatic  collides with Actors, Projectiles
        "Actor"       group Actors       collides with WorldStatic, Actors,
                                         Projectiles, Triggers
        "Projectile"  group Projectiles  collides with WorldStatic, Actors
        "Sensor"      group Triggers     collides with Actors
    CollisionProfileRegistry: CollisionProfile Define(string name,
    string collisionGroup, IEnumerable<string>? collidesWith = null,
    bool collidesWithAll = false); Get(name); TryGet(name, out profile);
    GetProfileNames(). CollisionProfile exposes Name, CollisionGroup,
    CollidesWith, CollidesWithAll and resolves through the scene's group
    registry (ResolveCollisionGroup / ResolveCollidesWith). The registry is
    persisted with the scene; a save file written before profiles existed loads
    with the four standard profiles installed.
  * DEFAULTS CHANGED. New sprites take SpriteManager.Instance.DefaultCollisionProfile
    ("Actor") instead of the old all-groups/all-masks collider, and a layer's
    fixed tiles take SceneLayer.DefaultTileCollisionProfile ("World") instead of
    none/none. Override per object with Tile.SetCollisionProfile(string
    profileName) or per creation with
    SpriteManager.CreateSprite(..., collisionProfileName: "Projectile").
    UPSTREAM-PARITY QUIRK: adding a layer to a scene — which also happens during
    a load — re-applies that layer's DefaultTileCollisionProfile to every fixed
    tile, so a per-tile SetCollisionProfile on a LAYER TILE does not survive a
    save/load or a re-add. Sprites are unaffected.
  * Groups are still bitmasks underneath — allocate named bits via the scene's
    CollisionGroups registry (CollisionGroupRegistry: int Define(string name),
    int Get(string name), int GetMask(IEnumerable<string> names),
    GetGroupNames(); preset bits WorldStatic, Actors, Projectiles, Triggers).
    CollisionMasks.None (0) and CollisionMasks.All (~0) are the two constants.
    TileCollider(Tile tile, int collisionGroup, int collidesWith,
    CollisionResponseType responseType = CollisionResponseType.Solid) is still
    the way to build one by hand.
  * Resolution is AUTOMATIC, once per cycle, per layer: Solid vs Solid gets
    a minimum-axis push-out with velocity canceled on the hit axis (slide);
    Trigger reports without push-out. (Overlap events are currently
    engine-internal — collision response is automatic-only; query manually
    via SceneLayer.ColliderRegistry.QueryAabb for game logic.)
  * SceneLayer.ShowCollisionBoxes = true overlays collision bounds for
    debugging — for tiles whose collisions are actually enabled, only.

  COLLISION AREA ADJUSTMENT — Tile.AdjustCollisionArea is a CollisionAdjust
  (namespace ...Physics.Collisions; Top, Bottom, Left, Right in pixels;
  CollisionAdjust.None; ApplyTo(Rectangle), IEquatable, == / !=), and
  Tile.CollisionArea is exactly AdjustCollisionArea.ApplyTo(DrawLocationWorld).

      THE CONVENTION IS INSET ON EVERY EDGE. A POSITIVE value on ANY of the four
      edges moves that edge INWARD and shrinks the collision box; a NEGATIVE
      value moves it outward and grows the box. Positive Bottom raises the
      bottom edge, positive Right moves the right edge left. Concretely:
          Rectangle.FromLTRB(L + Left, T + Top, R - Right, B - Bottom)
      (This differs from the earlier CollisionDetectionAdjustment type, where
      positive Bottom/Right pushed the far edges OUTWARD. Saved files are
      unaffected — the member names Top/Bottom/Left/Right did not change — but
      any hand-written value must be re-read under the inset rule.)

      var feet = new CollisionAdjust(top: 40, bottom: 0, left: 6, right: 6);
      hero.AdjustCollisionArea = feet;   // only the boots collide

  BY-FRAME COLLISION. The FIRST frame assigned to a tile SEEDS its collision
  adjustment and collision type from that frame's tilesheet metadata, unless
  the tile set one explicitly first. Later frame changes move them only when
  the matching by-frame flag is on:
      Tile.AdjustCollisionAreaByFrame  (bool, default false, persisted)
      Tile.CollisionTypeByFrame        (bool, default false, persisted)
  Turn one on for an animation whose collision shape genuinely changes between
  frames (a crouch, a sword swing); leave it off — the default — to keep one
  stable collision box across a walk cycle.
  Tile.SetCollisionProfile(name), the protected Tile.AttachCollider(ICollider)
  and the protected Tile.CopyCollisionSettingsFrom(Tile) round the API out;
  a sprite clone carries the source's adjustment, type and profile.

THE COLLISION MODEL TYPES (namespace ...Physics.Collisions):
    ICollisionEntity            Rectangle CollisionArea
    ICollisionMovableEntity     : ICollisionEntity — TranslateWorldPx(int dx, int dy),
                                CancelVelocityComponent(bool cancelX, bool cancelY)
                                (Sprite implements this; the resolver pushes
                                through it)
    ICollider                   Aabb BoundsWorldPx; ICollisionEntity Owner;
                                bool IsStatic; int CollisionGroup {get;set;};
                                int CollidesWith {get;set;};
                                CollisionResponseType ResponseType {get;set;}
    CollisionResponseType       Solid, Trigger
    Aabb(float minX, float minY, float maxX, float maxY)
                                MinX/MinY/MaxX/MaxY, Width, Height, Center (PointF),
                                Intersects(in Aabb other), ToRectangle(),
                                static FromRectangle(Rectangle) / FromRectangleF(RectangleF)
    TileCollisionType           None, Blocking, Trigger
    CollisionAdjust             Top, Bottom, Left, Right (pixel INSETS), None,
                                ApplyTo(Rectangle)
    CollisionProfile            Name, CollisionGroup, CollidesWith, CollidesWithAll,
                                ResolveCollisionGroup(CollisionGroupRegistry),
                                ResolveCollidesWith(CollisionGroupRegistry)
    CollisionProfileNames       World, Actor, Projectile, Sensor (string constants)
    CollisionProfileRegistry    Define(name, collisionGroup, collidesWith,
                                collidesWithAll), Get(name), TryGet(name, out),
                                GetProfileNames()
    ColliderRegistry            (one per SceneLayer) StaticColliders, DynamicColliders,
                                Register(ICollider), Unregister(ICollider),
                                void QueryAabb(in Aabb area, int layerMask,
                                               int collidesWithMask,
                                               List<ICollider> results,
                                               ICollider? ignore = null)
  A typical game-logic query: build an Aabb around the player, call QueryAabb
  with CollisionMasks.All for both masks and a reusable List<ICollider>, then
  inspect each result's Owner (the Tile) and ResponseType.

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
        .SetFillPattern(bitmap, scale, ...) / .ClearFillPattern()
        .SetFillImage(bitmap|image, mode, scale, offsetPx, filterQuality)
        .ClearFillImage()
    TextBlock(host, viewOrLayer, bounds)
        .SetFont(SKTypeface.FromFamilyName("..."), 16f, minSize: 14f)
        .SetColors(fore, back).SetAlignment(SKTextAlign.Center, VerticalAlign.Center)
        .EnableWrapping().SetMaxLines(6).UseShadow().SetShadow(...)
        .UseOutline().PulseColor(...).StartTypewriter(...)/.StartWordReveal(...)
        .SetText("...")   // updatable every frame (score/FPS readouts)
    DirectComposite(host, DirectDrawingMode.View)
        .Add(child1).Add(child2)      // group; has .Movement (pixel space)
        .SetOpacity/FadeTo/FadeIn/FadeOut
    ImageInstanceLayer                // many ImageInstance copies of one image;
                                      //   View mode OR SceneLayer mode
    ParticleSurface(host, layerOrView, bounds, nickname, maxParticles)
        .Emitters.Add(new ParticleEmitter {
            Position, EmitRate, LifeRange, VelocityRangeX/Y, SizeRange,
            GravityY, JitterX/Y, Color, BlendMode, ParticleSprite,
            OnSpawn = (ref Particle p) => { /* per-particle custom */ } });
        // plus Burst(emitter, count), ActiveParticleCount, GlobalEmitScale,
        // CullingMarginX for off-surface emitters

Fonts for TextBlock come from SKTypeface or from FontManager (see FONTS AND
SVG). Custom drawables derive from DirectDrawingBase (or
DirectDrawingMovableBase for a .Movement) and override OnDraw — the GpuRender
sample's PlasmaBackdrop is the worked example.

The ParticleTest sample is the reference for particles + composites +
TextBlock; the glowing pulsing text box it animates upward is a
DirectComposite of a DirectRectangle and a TextBlock moved with
Movement.MoveBy(new Vector2(0, -500), 10f, EasingFunctions.EaseInOutQuad).
ZOrder orders direct drawings among themselves per drawing mode.

DIRECTRECTANGLE IMAGE FILLS. A rectangle can be filled with a bitmap or an
SKImage instead of (or after) a solid colour:

    DirectRectangle SetFillImage(SKBitmap bitmap, ImageFillMode mode = Stretch,
                                 float scale = 1f, SKPoint? offsetPx = null,
                                 ImageFilterQuality filterQuality = Medium)
    DirectRectangle SetFillImage(SKImage image, ...)     // same parameters
    DirectRectangle ClearFillImage()

  * DirectRectangle.ImageFillMode: Stretch (fill, ignore aspect), Fit (whole
    image inside, aspect kept), Fill (cover, aspect kept, overflow clipped),
    Center (native size, centred, clipped), PixelPerfect (largest whole-number
    scale that fits, never below native size), Repeat (tiled).
  * `scale` and `offsetPx` apply to Repeat only. `scale` must be finite and
    greater than zero and `mode` must be a defined enum value, or the call
    throws ArgumentOutOfRangeException; a null source throws
    ArgumentNullException. A rejected call leaves the existing fill intact.
  * The fill is clipped to the rectangle INCLUDING its rounded corners, and
    setting an image fill enables filled mode.
  * The image source stays CALLER-OWNED — the rectangle never disposes it.
  * Image fill and pattern fill are mutually exclusive: setting one clears the
    other. SetFillPattern validates its scale the same way.
  * DirectRectangle and DirectImage now release their cached SKPaint (and the
    pattern shader) deterministically in Dispose, instead of leaving them to
    the finalizer.

IMAGEINSTANCELAYER IN SCENE-LAYER MODE. Two constructors bind the layer to a
View (screen space, the original behaviour) and two to a SceneLayer (world
space), each with a plain form and a callbacks form:

    ImageInstanceLayer(host, View view, Rectangle screenBounds, string? nickname = null)
    ImageInstanceLayer(host, SceneLayer sceneLayer, Rectangle worldBounds,
                       string? nickname = null)
    ImageInstanceLayer(host, view|sceneLayer, bounds,
                       Func<Rectangle, Random, IEnumerable<ImageInstance>>? initializer,
                       Func<ImageInstance, Rectangle, bool>? shouldRecycle = null,
                       Func<ImageInstance, Rectangle, Random, ImageInstance>? recycleInstance = null,
                       Action<ImageInstance, float>? updateInstance = null,
                       string? nickname = null)

  * In SceneLayer mode the instances live in WORLD pixels, so they scroll with
    the camera and follow the layer's parallax and the view's zoom; the
    initializer / should-recycle / recycle callbacks receive WorldBounds, and
    dirty rectangles go to that layer's own refresh queue. In View mode the
    callbacks receive ScreenBounds, exactly as before.
  * ImageInstance.Bounds is world pixels in SceneLayer mode and screen pixels
    in View mode.
  * View-mode drawing now maps instance bounds INTO the destination rectangle
    rather than using absolute screen coordinates; with matching origins and
    equal sizes (the ordinary case) the output is identical, and a letterboxed
    or scaled destination now renders correctly.

LIGHTING (Mode A)
--------------------------------------------------------------------------------
Two independent halves, usable together or alone: LIGHTS that ADD glow, and
DARKNESS OVERLAYS that subtract it and are punched through by reveal sources.
Everything draws through the backbuffer canvas, so both work on CpuRendering
and GpuRendering.

    DirectRadialLight(Color lightColor, RenderSurfaceHostBase host,
                      SceneLayer sceneLayer, PointF centerWorldPx,
                      float radiusWorldPx, string? nickname = null)
        CenterWorldPx, RadiusWorldPx, LightColor, Intensity, EffectiveIntensity,
        BlendMode (SKBlendMode, Screen for a torch), HotspotRadiusRatio,
        MidpointRadiusRatio, MidpointIntensityRatio, IsAntialias,
        FlickerEnabled, FlickerAmount, FlickerRefreshHz, event Changed;
        fluent MoveTo(centerWorldPx) / SetRadius(r) / SetIntensity(i)

    DirectLightLayer(RenderSurfaceHostBase host, SceneLayer sceneLayer)
        the logical owner of a group of lights on ONE layer:
        AddTorchLight(PointF centerWorldPx, float radiusWorldPx,
                      Color? color = null, string? nickname = null),
        Remove(light), Clear(), Lights, DefaultZOrder (10,000),
        events LightAdded / LightRemoving; IDisposable

    DirectDarknessOverlay(host, View view, SceneLayer projectionLayer,
                          string? nickname = null)
        a VIEW-mode darkness quad over the whole viewport (player vision, fog of
        war). It needs the projectionLayer to map world points to screen.
    DirectSceneLayerDarknessOverlay(host, SceneLayer sceneLayer,
                                    Rectangle worldBounds, string? nickname = null)
        the SCENE-LAYER sibling: a world-bounded darkness region
        (DarknessWorldBounds) that scrolls with its layer and is visible to
        every view looking at that part of the world (swamp fog, a dark room).
        It refuses lights that belong to a different layer.

    Both overlays share the same surface:
        DarknessColor, DarknessOpacity, InnerClearRadiusRatio,
        MidpointRadiusRatio, MidpointStrength, RevealSources
        RevealSource AddRevealSource(PointF centerWorldPx, float radiusWorldPx,
                                     string? nickname = null)
        RevealSource TrackLight(DirectRadialLight light, ...)
        void TrackLightLayer(DirectLightLayer lightLayer, ...)
        bool UntrackLight(light);  bool RemoveRevealSource(source);  ClearRevealSources()
        fluent SetDarknessColor / SetDarknessOpacity / SetInnerClearRadiusRatio /
               SetMidpointRadiusRatio / SetMidpointStrength
               (+ SetDarknessWorldBounds on the scene-layer overlay)

A torchlit room, end to end:

    var lights = new DirectLightLayer(host, dungeonLayer);
    var torch  = lights.AddTorchLight(new PointF(520, 320), 110f, nickname: "torch-01");
    torch.FlickerEnabled = true;

    var darkness = new DirectDarknessOverlay(host, mainView, dungeonLayer, "dungeon-darkness")
                      .SetDarknessColor(Color.Black)
                      .SetDarknessOpacity(190);
    darkness.TrackLightLayer(lights);       // every present and future light carves a hole
    torch.MoveTo(playerWorldCenterPx);      // glow and reveal move together

  * Tracking is IDEMPOTENT per light and per light layer. To change a tracked
    light's radiusScale / intensityScale / trackIntensity, UntrackLight first,
    then TrackLight again. A tracked light that is disposed drops its hole.
  * Lights default to ZOrder 10,000 (DirectLightLayer.DefaultZOrder) and
    overlays to 20,000, so darkness composites over the lights.
  * Flicker is pause-safe: a flickering torch does not jump phase across
    Engine.Pause() / Resume().

DISPLAY EFFECTS (Mode A)
--------------------------------------------------------------------------------
Presentation-level transitions over a whole View or a whole SceneLayer, driven
by the render surface host. Effects change PRESENTATION only — they never move
world objects, alter collision geometry, or change a layer's origin.

    host.Effects                              -- EffectsManager on RenderSurfaceHostBase
        TEffect Run<TEffect>(View target, TEffect effect)
        TEffect Run<TEffect>(SceneLayer target, TEffect effect)
        void Cancel(DisplayEffect effect);  void CancelAll()
        ReadOnlyCollection<DisplayEffect> ActiveEffects

    // fade the HUD view in over 400 ms, then wipe the map layer away
    host.Effects.Run(hudView, new FadeInEffect(0.4f));
    host.Effects.Run(mapLayer, new EraseEffect(EffectDirection.FromLeftToRight, 0.8f));

The effect types (namespace CodeBrix.Platform.GameEngine.Effects):
    FadeInEffect(float durationSeconds, EasingKind easing = Linear)      view or layer
    FadeOutEffect(float durationSeconds, EasingKind easing = Linear)     view or layer
    SlideInEffect(EffectDirection, float durationSeconds,
                  EasingKind easing = EaseOutCubic)                      view or layer
    SlideOutEffect(EffectDirection, float durationSeconds,
                  EasingKind easing = EaseInCubic)                       view or layer
    FillEffect(EffectDirection, float durationSeconds,
               EasingKind easing = Linear)                               view or layer
    EraseEffect(EffectDirection, float durationSeconds,
               EasingKind easing = Linear)                               view or layer
    ZoomInEffect(float targetZoom, float durationSeconds)                VIEW only
    ZoomOutEffect(float targetZoom, float durationSeconds)               VIEW only
    EarthquakeEffect(float durationSeconds, float intensityPx = 8f,
                     bool decay = true, int? randomSeed = null)          VIEW only

    EffectDirection: None, FromLeftToRight, FromRightToLeft, FromTopToBottom,
        FromBottomToTop, FromTopLeftToBottomRight, FromTopRightToBottomLeft,
        FromBottomLeftToTopRight, FromBottomRightToTopLeft

The concrete types sit under four public abstract bases — FadeEffect (FadeIn /
FadeOut), SlideEffect (SlideIn / SlideOut), WipeEffect (Fill / Erase) and
ZoomEffect (ZoomIn / ZoomOut) — all deriving DisplayEffect. Every constructor in
that hierarchy, DisplayEffect's included, is `private protected`, so the set of
effect types is CLOSED: use the bases for type tests and pattern matching, not
for subclassing. They also carry the read-back properties:

    SlideEffect.Direction        EffectDirection, get-only
    WipeEffect.Direction         EffectDirection, get-only
    ZoomEffect.TargetZoom        float, get-only
    EarthquakeEffect.IntensityPx float, get-only
    EarthquakeEffect.Decay       bool, get-only

Every DisplayEffect carries Id, DurationSeconds, Easing, Status
(EffectStatus: Pending -> Running -> Completed | Cancelled), Progress, the
events Completed and Cancelled, and Cancel().

RULES WORTH KNOWING:
  * ONE EFFECT PER TARGET PER CHANNEL. The channels are Transform (slides,
    earthquake), Opacity (fades), Reveal (fill/erase) and Zoom. Running a
    second effect on the same target+channel REPLACES the first WITHOUT
    restoring its state, so the new effect continues from the value the old one
    had reached. Cancel / CancelAll / disposing the host DO restore the state.
  * An effect INSTANCE runs once. Construct a new one for the next run.
  * An effect whose target the host no longer owns (a removed view, a layer of
    a scene that has been unbound) is dropped silently.
  * A view running a presentation effect no longer CLIPS the views beneath it,
    so a translucent, wiped or slid view reveals what is below.
  * While ANY view-level effect is running, the CPU dirty-rectangle
    optimisation is suspended for its duration and the surface is recomposed in
    full each frame. Layer-only effects keep dirty-rect rendering.
  * View-mode direct drawings shift with their view's effect offset.
  * Effects advance on the FOREGROUND (render) cadence, so Engine.Pause()
    freezes one mid-effect and Resume() shifts its time baseline — nothing
    bursts to completion across a pause.
  * ZoomInEffect / ZoomOutEffect delegate the animation to
    Viewport.ZoomToOverDuration; the effect owns lifecycle only, and View.Update
    still drives the zoom.

SPLASH AND HUD COMPONENTS (Mode A)
--------------------------------------------------------------------------------
Two ready-made DirectComposite subclasses, so common game furniture does not
have to be rebuilt per game.

SPLASHOVERLAY — a view-sized splash that fades in, holds, fades out and
disposes itself:

    static SplashOverlay? TryCreate(string imagePath | Stream imageStream,
                                    RenderSurfaceHostBase host, View view,
                                    float fadeInSeconds = 0.45f,
                                    float holdSeconds = 3f,
                                    float fadeOutSeconds = 0.45f,
                                    Action? onHolding = null,
                                    Func<Task>? onHoldingAsync = null,
                                    Action? onSplashCompleted = null,
                                    string? nickname = null)
    Image (the DirectImage), Phase (SplashPhase: Hidden, FadingIn, Holding,
    FadingOut, Completed), FadeInSeconds / HoldSeconds / FadeOutSeconds

  * TryCreate returns NULL and logs a warning when the file is missing or the
    stream does not decode, or when the host has no views — so a game can start
    without a splash instead of throwing. A negative duration throws
    ArgumentOutOfRangeException.
  * The image is drawn with ScaleMode.Fit at ZOrder int.MaxValue and
    re-stretches when the viewport's target rectangle changes.
  * onHolding / onHoldingAsync run on the engine thread when the hold phase
    starts — the place to load content behind the splash. THE HOLD ENDS WHEN
    THE HOLD TIMER AND THAT WORK ARE BOTH FINISHED, whichever is later.
  * onSplashCompleted is raised on the engine thread AFTER the fade-out and
    after the overlay has disposed itself. Start music and show the title
    screen there.
  * The hold uses an engine Timer, so it is pause-shifted like everything else.

HEALTHBAR — a world-space bar that tracks a sprite:

    HealthBar(RenderSurfaceHostBase host, Sprite target, float maxValue,
              Size? size = null, Point? offsetPx = null, string? nickname = null)
    HealthBar(RenderSurfaceHostBase host, SceneLayer sceneLayer, Sprite target,
              float maxValue, int width, int height, int offsetY = 0,
              string? nickname = null)
    Target, Value, MaxValue, Fraction, BarSize, OffsetPx,
    FillColor / WarningColor / CriticalColor, WarningFraction / CriticalFraction,
    UseThresholdColors, TrackBoundsWorld, FillBoundsWorld
    fluent SetValue(v), SetFillColor(c), SetTrackColors(background, border),
           SetThresholdColors(warning, critical), SetThresholds(warningFraction,
           criticalFraction), Show(), Hide(); RefreshPosition()

  * It is a SceneLayer-mode composite of two DirectRectangles (track + fill,
    StrokeAlign.Inside on the track), centred above its target with OffsetPx,
    and it follows the sprite's SpriteMoved event.
  * Threshold colours are OPT-IN: set UseThresholdColors (or call
    SetThresholdColors) to have the bar switch to warning/critical colours.
  * It disposes itself with its target sprite. maxValue must be greater than
    zero and the bar big enough to draw, or the constructor throws
    ArgumentOutOfRangeException.

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
instance ($id/$ref via CodeBrix.Json.Extensions reference handling). Loaded
content is fully REHYDRATED: layer collision registries/refresh queues, tile
colliders, tile->layer back-references, sprite animators/movement/colliders,
and scene<->layer event wiring are rebuilt during the load's merge step.

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
    missing entry. AssetsFileEntry describes one stored entry.
  * AudioResourceManager.LoadFromEngineAssetsFile(pack) bulk-loads every
    audio entry; SvgResourceManager.Instance.LoadFromEngineAssetsFile(pack)
    does the same for SVGs; TilesheetRegistry.LoadFromAssetsFile /
    LoadFromDefinitionAsset pull images and .gts definitions.

CONFIGURATION: EngineConfiguration / EngineConfigurationFile
--------------------------------------------------------------------------------
Engine.Instance.Configuration (loaded by Initialize; default file
"gameengine.json"; a missing file just yields defaults):

    TargetFPS = 60                    -- render throttle; 0 = uncapped
    VSync = true                      -- GPU (GpuRendering) backbuffers only
    MsaaSampleCount = 1               -- GPU only; applies at next surface init
    SamplingTimeForCPS = 1.5          -- seconds between CPSCalculated events
    TimeBetweenKeyboardEvents = 0.03  -- repeat-event throttle floors (seconds)
    TimeBetweenMouseEvents / TouchEvents / GamepadEvents = 0.03
                                      -- these ARE enforced (the mouse one used
                                         to be ignored). 0 = an event every
                                         cycle. TimeBetweenTouchEvents paces
                                         TouchMoved only. See INPUT.
    TimeBetweenGamepadStateUpdates = 0.008
                                      -- how often gamepad DEVICE state is re-read
                                         (and hotplug detected), in both modes;
                                         0 = every poll. Distinct from
                                         TimeBetweenGamepadEvents, which throttles
                                         how often a HELD button re-raises its event
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

EngineConfigurationFile.CreateNew/Load/Save manage the file:

    static EngineConfigurationFile CreateNew(string? configFileName = null,
                                             bool? autoSave = null)
    static EngineConfigurationFile Load(string? configFileName = null,
                                        bool? autoSave = null)
    string FileName;  string FilePath;  bool AutoSave;
    EngineConfiguration EngineConfig;  void Save();  void Save(string jsonPath);
    void Dispose()

THE JSON ROOT KEY IS "EngineConfig". A gameengine.json whose settings sit under
any other root object is read as an empty configuration — no error, just
defaults. The shipped file looks like this:

    {
      "EngineConfig": {
        "TargetFPS": 60,
        "SamplingTimeForCPS": 1.5,
        "TimeBetweenKeyboardEvents": 0.03,
        "TimeBetweenGamepadEvents": 0.03,
        "TimeBetweenMouseEvents": 0.03
      }
    }

  * AUTO-SAVE WORKS. With autoSave true (Engine.Initialize(..., autoSaveConfig:
    true), or AutoSave on the file object) the configuration — including
    anything the game put in ConfigurationSections — is written back when the
    file is disposed, and Engine.Dispose() disposes it before the logging
    system shuts down. Earlier versions accepted the flag and never wrote.
  * THE DEFAULT PATH IS RELATIVE. Load()/Initialize() with no path resolve
    "gameengine.json" against the PROCESS WORKING DIRECTORY, which is not
    necessarily where the executable lives. Pass an absolute path for a
    predictable location:

        var configPath = Path.Combine(AppContext.BaseDirectory, "gameengine.json");
        host.Initialize(configPath: configPath, autoSaveConfig: true);

  * THE FILE IS READ ONCE. Load materialises the settings and releases the
    configuration root immediately — no reload-on-change watcher is left
    behind, and an EngineConfigurationFile obtained from Load does not track
    later edits to the file on disk. Call Load again to pick them up.
  * Configuration.LoggingQueueCapacity is honoured at Initialize, and shutdown
    stops asynchronous logging whenever it is asynchronous, flushing according
    to FlushAsyncLogsOnShutdown.

PLUGINS, LOGGING, DI, VALUE BAGS, FONTS AND SVG
--------------------------------------------------------------------------------
PLUGINS (namespace ...Extensibility): IEnginePlugin hooks the cycle without
subscribing to events —
    string Name;  string Version
    void OnInitialize(Engine engine)
    void OnPreCycle(Engine engine, double deltaMs)
    void OnPreFrameRender(Engine engine, double deltaMs)
    void OnPostFrameRender(Engine engine, double deltaMs)
    void OnPostCycle(Engine engine, double deltaMs)
    void OnPostRenderCanvas(Engine engine, RenderSurfaceHostBase host, SKCanvas canvas)
                                                  // default no-op overlay hook
    void OnShutdown(Engine engine)
    EnginePluginRegistry.Register(IEnginePlugin) / Unregister(IEnginePlugin) / All
The same thread rules as the matching events apply (engine thread; the canvas
hook follows the surface's render thread, UI thread under GpuRendering).

LOGGING (namespace ...Logging): the engine logs through
Microsoft.Extensions.Logging. EngineLogger (static):
    EngineLoggingMode Mode            -- Asynchronous (default) / Synchronous
    void StartAsyncLogging(int capacity = ...);  void StopAsyncLogging(
        bool flush = true, TimeSpan? flushTimeout = null)
    void SwitchToSyncAndFlush(TimeSpan? flushTimeout = null);  void SwitchToAsync(int? capacity = null)
    ILoggerFactory EngineLoggerFactory;  ILogger<T> GetLogger<T>()
    void SetLogLevel(LogLevel level)  -- what GameHostBase.Initialize(logLevel:) calls
    event EventHandler<LoggingErrorEventArgs> LoggingError
        (LoggingErrorEventArgs: Exception, CategoryName, LogLevel)
Engine.Logger is the engine's own ILogger<Engine>; a game logs through
EngineLogger.GetLogger<MyGame>(). Configuration.LoggingMode /
LoggingQueueCapacity / FlushAsyncLogsOnShutdown govern the async queue.

DI: ServiceCollectionExtensions.AddEngineLogging(this IServiceCollection
services) makes the application and the engine share ONE logging pipeline.

    services.AddLogging(b => b.AddConsole());
    services.AddEngineLogging();          // engine logs now go through it too
    var provider = services.BuildServiceProvider();

  * If the collection already registers an ILoggerFactory, that factory becomes
    the engine's: an ImplementationInstance is adopted directly, and a factory
    or type registration is re-registered at the SAME lifetime around the
    original one. Resolving ILoggerFactory no longer recurses into itself
    (AddEngineLogging used to produce a circular registration that threw on the
    first resolve).
  * Once an application-supplied factory is in use, EngineLogger.SetLogLevel is
    a NO-OP — the application's own filters own the level, and
    GameHostBase.Initialize(logLevel:) will not override them. Configure the
    level through the application's logging builder instead.

VALUE BAGS: TypedValueBag is a typed, key-safe property bag carried by
EngineState (State.ValueBag), Scene, SceneLayer and every Tile/Sprite
(ValueBag) for the game's own per-object data. Keys are typed:
    public static readonly ValueKey<int> Hp = new("hp");
    tile.ValueBag.Set(Hp, 10);
    int hp = tile.ValueBag.Get(Hp, defaultValue: 0);
    void Set<T>(ValueKey<T> key, T value);  bool TryGet<T>(ValueKey<T> key, out T? value)
    T Get<T>(ValueKey<T> key, T defaultValue = default);  bool Remove<T>(ValueKey<T> key)
    bool Contains(string keyName) / Contains<T>(ValueKey<T> key);  void Clear()
    void MergeFrom(TypedValueBag? incoming, bool overwriteExisting = false)
    TypedValueBag Clone();  Dictionary<string, object?> ToDictionary()
Value bags are NOT part of the save file (see SAVE / LOAD) — persist their
contents yourself if they matter.

FONTS: FontManager.Instance (namespace ...Rendering.Text) is a keyed SKTypeface
cache for TextBlock fonts:
    SKTypeface LoadFromFile(string key, string filePath)
    SKTypeface LoadFromResource(string key, Assembly assembly, string resourceName)
    SKTypeface LoadFromResource(string key, string resourceName)
    SKTypeface Get(string key);  bool TryGet(string key, out SKTypeface? typeface)
    SKTypeface GetOrDefault(string key);  bool Contains(key);  bool Remove(key)
    void Clear();  IReadOnlyCollection<string> Keys

SVG: SvgResourceManager.Instance (namespace ...Drawing) is the keyed store for
vector art that DirectSvg draws:
    SvgResource LoadFromFile(string key, string path)
    List<SvgResource> LoadFromEngineAssetsFile(AssetsFile resourceFile)
    bool Contains(string key);  SvgResource? Get(string key)
    Dictionary<string, SvgResource> GetAll();  void Unload(string key);  void Clear()
    SvgResource: static Load(string path); IntrinsicSize (SizeF);
                 SKBitmap Rasterize(int width, int height);
                 SKBitmap Rasterize(float scale = 1.0f); Dispose()

COMPLETE EXAMPLES
=================
The two host walkthroughs above (MODE A WALKTHROUGH 1 and MODE B WALKTHROUGH)
are the skeletons; this is a small but complete Mode-A game host that draws a
tile grid, places a sprite, moves it with the keyboard and tweens it on a
mouse click. Every call is a verified engine signature.

    using System.Drawing;
    using System.Numerics;
    using CodeBrix.Platform.GameEngine;
    using CodeBrix.Platform.GameEngine.Drawing;
    using CodeBrix.Platform.GameEngine.Drawing.Sprites;
    using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
    using CodeBrix.Platform.GameEngine.Host.Hosting;
    using CodeBrix.Platform.GameEngine.Host.Rendering;
    using CodeBrix.Platform.GameEngine.Input.Keyboard;
    using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
    using CodeBrix.Platform.GameEngine.Scenes;
    using Windows.System;

    public sealed class TinyGameHost : CodeBrixGameHost
    {
        private Tilesheet _sheet = null!;
        private SceneLayer _layer = null!;
        private Sprite _hero = null!;

        public TinyGameHost(GameSurfaceCanvas canvas) : base(canvas) { }

        protected override void LoadTilesheets()
        {
            _sheet = TilesheetRegistry.Instance.LoadFromImageFile("tiles", "assets/tiles.png");
            _sheet.DefaultRegion.TileSize = new Size(32, 32);
        }

        protected override Scene CreateInitialScene()
        {
            var scene = new Scene();
            _layer = scene.AddLayer(columnCount: 20, rowCount: 12, width: 32, height: 32);
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 20; x++)
                    _layer[x, y].CurrentFrame = _sheet[0, 0];     // grass everywhere
            return scene;
        }

        protected override void CreateInitialViews()
            => RenderSurface.Host.ViewManager.ConfigureSingleFullView();

        protected override void CreateSprites()
        {
            _hero = SpriteManager.Instance.CreateSprite(_layer, _sheet[1, 0], "hero");
            _hero.SetPosition(new Vector2(3, 3));       // grid cells
            _hero.Visible = true;
        }

        protected override void OnKeyboardAdapterInitialized()
        {
            var kb = Engine.Input.KeyboardEventPoller!;
            kb.KeyDown += OnKeyDown;
            kb.StartMonitoringKeys(new[] { (int)VirtualKey.Left, (int)VirtualKey.Right,
                                           (int)VirtualKey.Up,   (int)VirtualKey.Down });
        }

        private void OnKeyDown(KeyDownEventArgs e)          // engine thread
        {
            if (e.KeyAction != KeyAction.Pressed) return;
            Vector2 delta = e.KeyCode switch
            {
                (int)VirtualKey.Left  => new Vector2(-1, 0),
                (int)VirtualKey.Right => new Vector2( 1, 0),
                (int)VirtualKey.Up    => new Vector2(0, -1),
                (int)VirtualKey.Down  => new Vector2(0,  1),
                _ => Vector2.Zero
            };
            if (delta != Vector2.Zero)
                _hero.Movement.MoveBy(delta, 0.2f, EasingKind.SmootherStep)
                              .OnComplete(() => Engine.Logger.LogInformation("arrived"));
        }

        protected override void OnEngineStarted() => Engine.Configuration.TargetFPS = 60;

        protected override void OnDisposing()
        {
            if (Engine.Input.KeyboardEventPoller is { } kb) kb.KeyDown -= OnKeyDown;
            base.OnDisposing();
        }
    }

Page code-behind (the same in every sample):

    public sealed partial class MainPage : Page
    {
        private TinyGameHost? _host;

        public MainPage()
        {
            InitializeComponent();
            GameCanvas.FirstStarted += (_, _) =>
            {
                GameCanvas.SetRenderResolution(640, 384);     // 20x12 tiles of 32 px
                _host = new TinyGameHost(GameCanvas);
                _host.Initialize(logLevel: LogLevel.Warning);
            };
            Unloaded += (_, _) => { _host?.Dispose(); _host = null; };
        }
    }

MINIMUM VIABLE PROJECT
======================
A CodeBrix.Platform application is one shared library that holds the game
plus one thin executable per head. The layout below is the one the Spot.Brix
sample uses (three heads; a game library; a shared XAML project). Version
attributes are omitted — use the latest of each package.

MyGame.Core/MyGame.Core.csproj  (the shared library — engine reference lives here)

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
      </ItemGroup>
      <ItemGroup>
        <Content Include="assets\**\*" CopyToOutputDirectory="PreserveNewest" />
      </ItemGroup>
    </Project>

MyGame.LinuxX11/MyGame.LinuxX11.csproj  (one head; swap the single head
package for CodeBrix.Platform.Runtime.Skia.Win32.ApacheLicenseForever or
CodeBrix.Platform.Runtime.Skia.MacOS.ApacheLicenseForever for the others)

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Exe</OutputType>
      </PropertyGroup>
      <ItemGroup>
        <Page Include="**\*.xaml" Exclude="bin\**\*.xaml;obj\**\*.xaml" />
        <None Remove="**\*.xaml" />
      </ItemGroup>
      <Import Project="..\MyGame.UI\MyGame.UI.projitems" Label="Shared" />
      <ItemGroup>
        <ProjectReference Include="..\MyGame.Core\MyGame.Core.csproj" />
      </ItemGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Platform.Runtime.Skia.X11.ApacheLicenseForever" />
      </ItemGroup>
    </Project>

MyGame.LinuxX11/Program.cs

    using CodeBrix.Platform.UI.Hosting;
    using System;

    namespace MyGame;

    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var host = CodeBrixPlatformHostBuilder.Create()
                .App(() => new App())
                .UseLinuxX11()          // .UseWin32Skia() / .UseMacOS() on the other heads
                .Build();
            host.Run();
        }
    }

MyGame.UI/App.xaml  (shared project; the .projitems lists App.xaml, App.xaml.cs,
Views/MainPage.xaml and Views/MainPage.xaml.cs)

    <Application x:Class="MyGame.App"
           xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Application.Resources>
        <ResourceDictionary>
          <ResourceDictionary.MergedDictionaries>
            <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
          </ResourceDictionary.MergedDictionaries>
          <FontFamily x:Key="OpenSansFont">ms-appx:///CodeBrix.Platform.Fonts.OpenSans/Fonts/OpenSans.ttf</FontFamily>
        </ResourceDictionary>
      </Application.Resources>
    </Application>

MyGame.UI/App.xaml.cs

    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;

    namespace MyGame;

    public partial class App : Application
    {
        public App()
        {
            global::CodeBrix.Platform.UI.FeatureConfiguration.Font.DefaultTextFontFamily =
                "ms-appx:///CodeBrix.Platform.Fonts.OpenSans/Fonts/OpenSans.ttf";
            InitializeComponent();
        }

        protected Window MainWindow { get; private set; }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new Window { Title = "MyGame" };
            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                MainWindow.Content = rootFrame;
            }
            if (rootFrame.Content == null)
                rootFrame.Navigate(typeof(Views.MainPage), args.Arguments);
            MainWindow.Activate();
        }
    }

MyGame.UI/Views/MainPage.xaml  (the canvas is the whole page)

    <Page
        x:Class="MyGame.Views.MainPage"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:game="using:CodeBrix.Platform.GameEngine.Host.Rendering"
        FontFamily="{StaticResource OpenSansFont}">
        <Grid Background="#FF222222">
            <game:GameSurfaceCanvas x:Name="GameCanvas" />
        </Grid>
    </Page>

MyGame.UI/Views/MainPage.xaml.cs — the page code-behind from COMPLETE
EXAMPLES (FirstStarted -> new host -> Initialize; Unloaded -> Dispose).

Run it with `dotnet run --project MyGame.LinuxX11`. The page hosts the
canvas; the canvas raises FirstStarted once it has a size; the host builds
the scene and starts the engine.

PERFORMANCE TIPS
================
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
  [] Do not call blocking waits (Task.Wait, lock convoys, I/O) inside cycle
     events, timer handlers, or OnTic — one slow handler stalls the whole
     game.

SOUND EFFECTS
  [] Fire rapid SFX through AudioResourceManager.TryPlaySfx (or your own
     SfxVoicePool) — never a fresh decode or player per shot. Short effects
     preload automatically; check IsPreloaded if a pool play returns false.
  [] Keep music/ambience on streaming readers (one long-lived voice each);
     don't raise PreloadShortSoundEffectMaxSeconds to cover them.
  [] Pick the cull policy deliberately; with CullLowestPriority, give the
     player's critical cues the highest priorities so they are never stolen.
  [] Audio fill callbacks: fast, allocation-free, never block, never touch
     game state.

RENDERING
  [] Pin a render resolution (SetRenderResolution) when the game's layout
     assumes fixed coordinates; letterboxing is automatic, and pointer
     mapping across the letterbox is provided (WindowToBuffer).
  [] Do not force refreshes during window resizes; the canvas already
     suppresses and resumes presenting around a resize.
  [] Size-anchored HUD content: reposition in OnRenderSurfaceResized.
  [] GpuRendering pays off for blending, scaling/rotation and SkSL shader
     scenes; a plain tile blit may not benefit and still re-renders the full
     surface every frame (no dirty rectangles on GPU).
  [] Batch SceneLayer property changes — nearly every setter forces a full
     scene refresh; use SetTileSize(w, h) rather than TileWidth + TileHeight.
  [] Enable tile.EnableAnimator only on tiles that animate (one Animator each).
  [] Load content in the host's load hooks (before the loop starts), not
     mid-game on the hot path; AssetsFile/tilesheets/audio all support
     up-front loading.

COMMON PITFALLS TO AVOID
========================
THREADS
  [] Mutate game state on the engine thread (Mode A) or the game-loop thread
     (Mode B) only; use EngineDispatcher.Post to get there.
  [] Never touch XAML/UI from the engine or game-loop thread; use
     UiDispatcher.Post.
  [] Never await EngineDispatcher.PostAsync FROM the engine thread; and after
     the first await inside the posted action you are no longer on it.
  [] MusicManager Ended/playlist events arrive on a background or audio
     thread — marshal before touching game state.

PAUSE CORRECTNESS
  [] Wire the hosting app: minimize -> Pause(), restore -> Resume().
  [] Register save-game / pause-screen logic on Paused (or OnEnginePaused);
     tear down on Resumed. Never poll IsPaused from game logic to "stop
     yourself" — the engine already stopped you.
  [] Put un-pause input at the UI layer, never engine input (pollers are
     parked while paused).
  [] Long-running voices the game manages specially: set SuspendOnEnginePause
     explicitly instead of fighting the automatic rule.

MUTUAL-EXCLUSIVITY RULES (each throws if violated)
  [] One mode per canvas: Host XOR UsePixelFramePresenter().
  [] InputPump.PollNow() only when the engine loop is NOT running.
  [] AudioSystem.Initialize before SoundChannel/callback streams.
  [] Presenter.Configure before the Mode-B loop starts (OnLoadContent).
  [] SetRenderResolution / UseGpuRendering BEFORE the first access to Host.
  [] ConfigureSingleFullView / Bind only from FirstStarted onward, on the UI
     thread.

RESOURCES AND SHUTDOWN
  [] Dispose the game host on page close; it stops the loop, unhooks events,
     and (CodeBrixGameHost) tears the engine down in the right order. After
     Engine.Dispose() the singleton is dead for the process — do not try to
     restart it. SoftwareRenderedGameHostBase.Dispose does NOT dispose the
     engine; call AudioSystem.Shutdown() / MusicManager.Instance.Dispose()
     yourself in that mode.
  [] Unsubscribe any engine events you subscribed outside the host bases
     (the bases unhook their own).
  [] Scenes self-register globally: Dispose() them (or Scene.ClearAllScenes())
     or they linger. Never call Animator.Dispose directly.

API TRAPS
  [] MoveBy(delta, float) — the float is a SPEED without an easing argument
     and a DURATION with one. Pass EasingKind/Func explicitly for a tween.
  [] OnBeginning(...) throws if no scripted move is active (a move that
     snapped instantly). OnComplete is safe.
  [] Configuration.TimeBetween*Events are SECONDS (0.03), whatever older doc
     comments say — and the mouse one is now enforced. Set
     TimeBetweenMouseEvents = 0 for a mouse event every cycle, and make
     SYNTHETIC clicks in UI automation hold the button ~300 ms or more; a
     ~12 ms press-and-release (xdotool's default) falls inside one 30 ms
     throttle window and is dropped.
  [] Start/StopMonitoring* registrations apply at the NEXT poll, not
     instantly; keys must be registered before KeyDown fires for them.
  [] Desktop mouse does NOT arrive as touch contact 0 any more. Pass
     emulateMouse: true, or override EmulateMouseAsTouch on the host base, for
     a game that reads only the touch stream.
  [] CollisionAdjust INSETS on every edge: positive shrinks the box on that
     edge, negative grows it. Positive Bottom/Right no longer push outward.
  [] One render-surface host per Scene — Bind throws if the scene is already
     bound elsewhere. Use several Views on the one host instead.
  [] Viewport.Zoom > 1 zooms IN. Code written against the older, inverted
     behaviour has to drop its compensation.
  [] Timer.Add validates its length: zero, negative, NaN, infinite and
     sub-tick lengths throw instead of hanging the engine thread.
  [] TilesheetDefinitionSerializer.Save(path, tilesheet) MUTATES a bitmap-only
     tilesheet — it writes a sibling .png and re-points the sheet at it.
  [] gameengine.json's root key is "EngineConfig", and the default path is
     RELATIVE to the process working directory. Pass an absolute path.
  [] An EffectsManager effect instance runs once, and a second effect on the
     same target+channel replaces the first WITHOUT restoring its state.
  [] Keyboard focus: a toolbar click steals focus from the canvas and the
     engine poller then sees nothing. Call EnsureFocus() and hand focus back
     after toolbar interactions.
  [] Sprite positions are GRID cells; RenderSize/Nudge/CollisionArea are
     pixels. SizeNewSpritesToSceneLayer (default true) resizes new sprites to
     the layer's tile size.
  [] Cycle keys are global; constructing a Cycle with an existing key
     replaces it, and StartAnimation(key) fetches a CLONE.
  [] Save/load: compress flags must agree; load tilesheets with the scenes
     that use them; keep EngineSaveContractResolver if you replace
     SerializerOptions; ValueBags and subclasses do not round-trip.
  [] MusicDuckMultiplier is owned by MusicManager — duck through PushDuck/
     Duck, never by writing AudioMixer.MusicVolume. ClearDucks() rescues a
     leaked duck handle.
  [] AN INSTRUMENT FROM AN ASSET PACK MUST REACH THE DISK, EXCEPT .sf2. A .sfz
     and a .dspreset REFERENCE sample files beside them; a .dslibrary or
     .dsbundle IS one file, but it is read IN PLACE BY PATH and nothing is
     unpacked. Only a .sf2 loads from a Stream. Extract the rest from an
     AssetsFile before loading.
  [] Decent Sampler knob positions and modulated parameters are INSTRUMENT
     state, not synthesizer state, so two tracks over one instrument share
     them — right for two players of the same sound, wrong when each part must
     move its own. The (key, instrumentPath, midiFilePath) constructor shares by
     design, through the process-wide MidiMusicPlayer.SharedDecentSamplerCache;
     hand a part its own instrument when it needs its own knobs.
  [] MusicStemSet.FromSunoStems decodes every stem it is given to memory (about
     23 MB per stereo minute at 48 kHz). Name the layers the game will actually
     cross-fade rather than taking the whole export.
  [] Never call IGamepadManager.Update() yourself — the engine refreshes
     gamepad state in both modes.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * No gamepad BACKEND. It defines IGamepadManager<T>/IGamepadAdapter and the
    GamepadEventPoller; the SDL2 implementation is the separate
    CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever package.
  * No 3D rendering. GpuRendering rasterises the same 2D/2.5D scene on the
    GPU and reads it back; it is not a 3D pipeline.
  * Mode B (presenter mode) is CPU-only — no GPU presentation path.
  * The engine singleton is not restartable after Engine.Dispose(): one game
    host per process lifetime. Stop() (not Dispose) is the restartable halt.
  * It does not un-pause itself: engine input pollers are parked while paused,
    so the resume trigger must come from the hosting application's UI layer.
  * Save files do not persist: value bags (TypedValueBag on state, scenes,
    layers, tiles), in-flight movement/jiggle/pulse state, animation
    playback position, audio playback position, or the music system's state.
    Custom Sprite/Tile subclasses are not round-trip aware.
  * Pre-schema (Newtonsoft-era) save files are rejected, not migrated.
  * Collision overlap events are engine-internal: response is automatic
    (Solid push-out / Trigger report); game logic queries
    ColliderRegistry.QueryAabb itself.
  * No beat/tempo detection for decoded audio — the game supplies the
    MusicTimeline; a MIDI file supplies its own.
  * .opus is not built in (license separation); register
    CodeBrix.Audio.Opus.BsdLicenseForever yourself.
  * It ships no SkiaSharp Linux native assets of its own — the CodeBrix.Platform
    head application provides them; a headless Linux consumer adds
    SkiaSharp.NativeAssets.Linux.
  * No Windows OpenGL driver: GpuRendering on a machine without an ICD falls
    back to CPU rendering and logs a warning.

WORKING EXAMPLES ON GITHUB
==========================
Repository root: https://github.com/ellisnet/CodeBrix.Platform.GameEngine

SAMPLES — nine complete games/demos, each with LinuxX11, Win32Skia and MacOS
heads plus a shared .UI project and a .Game library; each is the reference
consumer for the subsystems it exercises. None of them is in the repository
.slnx: each sample carries its OWN .slnx and is built and run on its own.

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/Spot.Brix
      Mode A via CodeBrixGameHost: scenes, sprites, tilesheets, engine
      mouse+keyboard input, the toolbar/focus recipe (src/Spot.Brix.UI/Views/
      MainPage.xaml.cs), per-move callbacks (src/libs/Spot.Brix.Game/
      SpotBrixGameHost.cs). It also demonstrates the whole start-up shape a
      finished game wants: a SplashOverlay title card whose completion callback
      starts the music and the opening screen; a CodeBrix.Platform XAML
      ContentDialog (New Game: 2-4 players, names/colours, human or computer,
      board 3x3 to 12x12) driving the engine from the UI thread through
      Engine.EngineDispatcher.Post; option persistence (music, sound effects,
      jiggle, clouds, GPU) in the "spot" section of a gameengine.json pinned to
      AppContext.BaseDirectory; and EngineState.SaveToFile("savegame.json") when
      a game ends.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/Platformer.Brix
      Mode A via CodeBrixGameHost, at a pinned 960x576 render resolution
      (GameSurfaceCanvas.SetRenderResolution): a side-view platform game and the
      reference consumer for fixed layer-tile colliders. Collision profiles and
      Tile.CollisionType on world tiles, CollisionAdjust in its INSET form on
      the player/hazards/relics, a foot probe through
      ColliderRegistry.QueryAabb, integrated velocity + gravity movement,
      horizontal camera follow with a dead zone, a view-bound
      DirectRectangle + TextBlock HUD, and a procedural tilesheet painted in
      code (TilesheetRegistry.LoadFromBitmap), so the sample ships no image
      assets.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/SpaceDuel.Brix
      Mode A via CodeBrixGameHost on the GPU tier: Sprite.Rotation on ships and
      lasers, MovementController.WrapX/WrapY for a wrap-around world, two
      parallax star layers, ParticleSurface explosion bursts, AI raiders, a
      per-ship HealthBar, a SplashOverlay title card, explicit Actor/Projectile
      collision profiles with CollisionAdjust insets, TargetFPS 0 / VSync off /
      MSAA 4 set from Engine.InitializationComplete, and a view-space HUD fed by
      CPSCalculated (GpuFps ?? NetCPS). SPACEDUEL_USE_CPU=1 runs the identical
      game on CpuRendering. All of its art is generated in code.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/Slider
      Mode A, direct Engine: sprites built on the engine thread via
      EngineDispatcher.Post, engine mouse events, rebuild-while-running.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/CoordinateTest
      Mode A, direct Engine: coordinate systems (orthogonal, isometric, hex),
      cameras/views.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/ParticleTest
      Mode A, direct Engine: ParticleSurface/emitters, DirectComposite/
      TextBlock/DirectRectangle, movement easing — plus the campfire click =
      global Pause()/Resume() toggle (UI-level pointer input + letterbox
      mapping; src/libs/ParticleTest.Game/ParticleTestGame.cs,
      OnCanvasPointerPressed). PARTICLETEST_USE_GPU=1 runs it on GpuRendering.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/SoftRender
      Mode B end-to-end: 320x200/70 Hz plasma+starfield, presenter, InputPump,
      raw-PCM blips (SoundChannel), streamed drone (StreamingAudioSource),
      zero-alloc frame loop, loop health stats (src/libs/SoftRender.Game/
      SoftRenderGameHost.cs).
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/GpuRender
      Mode A, direct Engine: the GpuRendering (GPU) showcase and SoftRender's
      GPU-first counterpart — resolution-independent SkSL plasma + starfield
      via a custom DirectDrawingBase subclass (PlasmaBackdrop), stats
      TextBlock with live GPU FPS, click-anywhere pause with a pause overlay
      (paused-frame + snapshot demo), window-tracking resolution with resize
      handling. GPURENDER_USE_CPU=1 runs the same scene on CpuRendering.
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/samples/MusicDemo
      The MUSIC SYSTEM reference: volume buses, fades and equal-power
      crossfades, ducking (fire-and-forget and the held-handle form),
      stingers, playlists, layered adaptive stems (MusicStemSet) AND the MIDI
      per-channel route, MIDI rendered through both an SFZ and a Decent
      Sampler instrument, a bar-quantised crossfade ACROSS A TEMPO CHANGE (the
      log says what the tempo map answered and what a fixed grid would have),
      a stems export loaded with MusicStemSet.FromSunoStems, marker jump
      points, and the global pause freezing music and fades together. It
      GENERATES every asset it plays on first run (src/libs/MusicDemo.Game/
      MusicAssetFactory.cs: layers, two tracks, a stinger, an SFZ and a Decent
      Sampler instrument, two MIDI files and a whole stems export), so the
      sample runs anywhere.

TESTS — headless unit tests that double as usage references, in an engine core
suite, a gamepad suite and a host suite:

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.Tests
      EngineStateRoundTripTests.cs / EngineStateSaveTests.cs — populated-graph
          save/load round-trips (scenes/layers/tile grids, shared sprite
          references, cycles, loose-file and asset-pack audio, compression,
          merge semantics), plus a legacy-save regression that strips the newer
          collision members from a real save file and reloads it
      SpriteRotationRoundTripTests.cs, CollisionProfileRoundTripTests.cs — the
          rotation and collision-profile/type members through save and load
      EnginePauseTests.cs — park/resume semantics, no-burst time shifting,
          audio suspend rules, snapshot capture
      TimerTests.cs — length validation, schedule preservation, one-shot timers
      ViewTests.cs, ViewportTests.cs, TextBlockTests.cs — zoom direction,
          anchored zoom, fixed-duration zoom tweens, the render-pass snapshot
      HexAxialCoordinatesTests.cs, SceneLayerCoordinateSystemTests.cs — the
          seven coordinate systems and fractional hex anchors
      SpriteTests.cs, SpriteRotationTests.cs, CompositeSpriteTests.cs,
          SpriteManagerTests.cs, TileTests.cs, SceneLayerTileTests.cs
      TilesheetCollisionAdjustTests.cs, TilesheetCollisionTypeTests.cs,
          TilesheetFactoryTests.cs, TilesheetDefinitionSerializerTests.cs,
          CollisionProfileTests.cs — the collision metadata and .gts round trip
      MouseEventPollerTests.cs, TouchEventPollerTests.cs,
          TapGestureRecognizerTests.cs, SwipeGestureRecognizerTests.cs,
          PinchGestureRecognizerTests.cs — throttling, touch lifecycle, gestures
      EffectsManagerTests.cs, EffectGeometryTests.cs, EffectsRenderingTests.cs —
          the effects subsystem, including its pause behaviour
      DirectRadialLightTests.cs, DirectDarknessOverlayTests.cs,
          DirectSceneLayerDarknessOverlayTests.cs — lighting, sampled from a
          rendered backbuffer
      SplashOverlayTests.cs, HealthBarTests.cs — the two ready-made components
      DirectRectangleTests.cs, DirectImageTests.cs, ImageInstanceLayerTests.cs,
          DirectDrawingMovableBaseTests.cs, ParticleSurfaceTests.cs,
          MovementControllerTests.cs, RefreshQueueTests.cs,
          RenderSurfaceHostTests.cs
      CachedSoundTests.cs / SfxVoicePoolTests.cs — decode-once preload and the
          pool's cull-policy selection (nothing opens the audio device)
      AudioMixerTests.cs, MusicManagerTests.cs, MusicStemSetTests.cs,
          MusicStemSetSunoTests.cs, MusicTimelineTests.cs,
          MusicQuantizedTransitionTests.cs, MidiMusicTrackLayerTests.cs,
          MidiMusicTrackInstrumentTests.cs — the music system, with fades
          advanced by hand and every instrument, MIDI file and stems export
          built in code (SyntheticInstrumentAssets.cs), so no binary fixture is
          needed to run them
      FixedRateGameLoopTests.cs, PixelFramePresenterTests.cs — Mode B
      InputPumpGamepadTests.cs — the Mode-B gamepad refresh path
      EngineConfigurationTests.cs, ServiceCollectionExtensionsTests.cs,
          EngineLoggerTests.cs, PlatformAudioFactoryTests.cs (the .opus
          registration proof), DirectCompositeTests.cs, GpuBackbufferTests.cs,
          ImageFilterQualityTests.cs, SpacingTests.cs, VariableRateSampleProviderTests.cs,
          AudioResourceDisposalTests.cs, AudioResourceManagerPcmTests.cs
  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.Host.Tests
      CodeBrixPlatformUiDispatcherTests.cs — the Host UI dispatcher

QUICK REFERENCE CARD
====================
LIFECYCLE (Engine.Instance)
    void Initialize(string? configFileName = null, bool? autoSaveConfig = null,
                    IKeyboardAdapter? keyboardAdapter = null, IMouseAdapter? mouseAdapter = null,
                    ITouchAdapter? touchAdapter = null,
                    IGamepadManager<IGamepadAdapter>? gamepadManager = null)
    void Start();  void Start(SynchronizationContext uiContext)
    void StartTimerDriven(SynchronizationContext uiContext);  void Tick()
    void Stop();  void Pause();  void Resume();  void Dispose()
    bool IsRunning / IsPaused / IsInitialized / IsDisposed
    SKImage? LastFrameBeforePause;  byte[]? LastFrameBeforePauseAsRgba(out int w, out int h)
    IEngineDispatcher EngineDispatcher   -- Post(Action), PostAsync(Func<Task>), IsOnEngineThread
    IUiDispatcher? UiDispatcher          -- Post(Action), Send(Action), IsOnUIThread
    EngineConfiguration Configuration;  EngineState State;  EngineInputSystems Input
    static ILogger<Engine> Logger

HOST (CodeBrix.Platform.GameEngine.Host.*)
    class GameSurfaceCanvas : SKXamlCanvas
        event FirstStartedEventHandler FirstStarted   (FirstStartedEventArgs.NewSize)
        RenderSurfaceHost<BackbufferBase> Host;  RenderSurfaceAdapterBase RenderSurfaceAdapter
        bool UseGpuRendering;  void SetRenderResolution(int width, int height)
        PixelFramePresenter UsePixelFramePresenter();  void EnsureFocus()
        void SetPointerCursorHidden(bool hidden)
        Point? WindowToBuffer(Point canvasPoint);  Point? BufferToWindow(Point bufferPoint)
    abstract class CodeBrixGameHost : GameHostBase
        ctor(GameSurfaceCanvas renderSurface);  GameSurfaceCanvas RenderSurface
        void Initialize(string? configPath = null, bool? autoSaveConfig = null,
                        LogLevel logLevel = LogLevel.Warning)
        overrides: LoadAssets, LoadTilesheets, LoadAnimationCycles, Scene CreateInitialScene,
                   CreateInitialViews, CreateSprites, CreateDirectDrawings, OnSceneBound,
                   OnEngineInitialized, OnEngineStarted, OnEnginePaused, OnEngineResumed,
                   OnConfigureGamepads, OnKeyboardAdapterInitialized, OnMouseAdapterInitialized,
                   OnTouchAdapterInitialized, OnRenderSurfaceResized(int, int), OnDisposing,
                   bool EmulateMouseAsTouch (default false)
    abstract class SoftwareRenderedGameHostBase
        ctor(GameSurfaceCanvas renderSurface, int ticsPerSecond)
        void Initialize(LogLevel logLevel = LogLevel.Warning)
        PixelFramePresenter Presenter;  FixedRateGameLoop GameLoop;  GameSurfaceCanvas RenderSurface
        abstract OnLoadContent(), OnTic(), OnRenderFrame(Span<byte> frameBuffer)
        virtual ConfigureInput(), ConfigureGamepads(), ConfigureAudio(), OnShutdown(),
                OnEnginePaused(), OnEngineResumed(), bool EmulateMouseAsTouch (default false)
    static class EngineExtensions
        InitializeCodeBrixKeyboardAdapter(this Engine, UIElement element)
        InitializeCodeBrixMouseAdapter(this Engine, UIElement element,
                                       MouseEventConfiguration? mouseEventConfiguration = null)
        InitializeCodeBrixTouchAdapter(this Engine, UIElement element, bool emulateMouse = false)
    CodeBrixKeyboardAdapter(UIElement) / CodeBrixMouseAdapter(UIElement) /
    CodeBrixTouchInputAdapter(UIElement, bool emulateMouse = false);
    static int? CodeBrixKeyboardAdapter.GetKeyCodeFromString(string)
    RelativeMouseSession(GameSurfaceCanvas): Begin(), End(), (int DeltaX, int DeltaY) ConsumeDelta()
    CodeBrixPlatformUiDispatcher(DispatcherQueue);  static CodeBrixPlatformUiDispatcher? ForCurrentThread()

MODE B
    FixedRateGameLoop(int ticsPerSecond, Action onTic): Start(), Stop(), Pause(), Resume(),
        PauseWithEngine, MaxCatchUpTics, TicCount, DroppedTics, ActualTicsPerSecond,
        LastException, event Action<Exception> UnhandledException
    PixelFramePresenter: Configure(width, height, PixelBufferFormat, FrameOrientation,
        PixelFrameScaleMode, ImageFilterQuality); PresentFrame(ReadOnlySpan<byte> | uint[] |
        ReadOnlyMemory<byte>); SKPoint? WindowToBuffer(SKPoint); SKPoint? BufferToWindow(SKPoint)
    static void InputPump.PollNow()

INPUT
    KeyboardEventPoller: StartMonitoringKey(int keyCode, string? displayName = null,
        double timeBetweenEvents = -1, bool isPaused = false); StartMonitoringKeys(IEnumerable<int>,
        double = -1); StartMonitoringAllKeys(double = -1); StopMonitoringKey(int|string);
        StopMonitoringAllKeys(); event Action<KeyDownEventArgs> KeyDown; IKeyboardAdapter? Adapter
    KeyDownEventArgs: KeyCode, KeyAction (Pressed/Released/Repeated), Modifiers, KeyConfig
    MouseEventPoller: StartMonitoringMouse(bool trackMouseMovement = true,
        double timeBetweenEvents = -1, bool isPaused = false); StopMonitoringMouse();
        event Action<MouseEventArgs> MouseEvent; CurrentPosition; ButtonStates; ScrollDelta
        (both pollers: IDisposable + static Reset(); Engine.Dispose calls both)
    TouchEventPoller: StartMonitoringTouch(double = -1, bool = false); StopMonitoringTouch();
        ActiveTouches; TouchBegan/TouchMoved/TouchEnded (never throttled);
        event Action<GestureEventArgs> TouchEvent;
        TapRecognizer.Tapped / SwipeRecognizer.Swiped /
        PinchRecognizer.PinchStarted|PinchUpdated|PinchEnded
    SwipeGestureRecognizer: MinimumSwipeSpeedPixelsPerSecond (200),
        MinimumSwipeDistancePixels (30)
    PinchedEventArgs: Phase (PinchPhase Began/Updated/Ended), TouchIds, Center,
        StartingDistance, PreviousDistance, CurrentDistance, ScaleDelta, TotalScale
    ITouchAdapter: ActiveTouches; ConsumeEndedTouches(); ConsumeBeganTouches()
    TouchPoint(int Id, Point Position, TouchPhase Phase)
    GamepadEventPoller: StartMonitoringButton(string gamepadId, string button,
        double timeBetweenEvents = -1, bool isPaused = false); StopMonitoringButton(gamepadId,
        button); StopMonitoringAllButtons(gamepadId); event Action<GamepadButtonDownEventArgs> ButtonDown
    IGamepadAdapter: GamepadId, PressedButtons, LeftStick/RightStick (GamepadStickState?),
        LeftTrigger/RightTrigger;  GamepadStickState: X, Y, Magnitude, Angle,
        IsEngaged(float threshold = 0.15f), Direction(float = 0.15f), WithDeadzone(float = 0.15f)

TIMERS
    static Timer Timer.Add(string timerID, TimerType type, TimerCycles cycles, double length)
    static Timer Timer.Add(TimerType type, TimerCycles cycles, double length)
        (length must be finite, > 0, >= 1 high-res tick and fit a positive Int64
         of ticks, or ArgumentOutOfRangeException)
    static void Timer.Remove(string timerID);  static void Timer.ClearAll();  static bool Timer.PausedAll
    Timer: event Tick; Paused; Dispose()

SCENE GRAPH
    Scene: SceneLayer AddLayer(int columnCount, int rowCount, int width = 32, int height = 32,
        int zOrder = 0, float parallax = 1f, CoordinateSystemTypes coordinateSystem = Orthogonal);
        AddLayer(SceneLayer); RemoveAllLayers(); FullRefreshNeeded; CollisionProfiles;
        CollisionGroups; ValueBag; Dispose()
    CoordinateSystemTypes: Orthogonal 0, IsometricRhombic 1, IsometricAxial 2,
        HexAxialFlatTop 3, HexAxialPointedTop 4, ObliqueRight 5, ObliqueLeft 6
    SceneLayer: this[x, y] (SceneLayerTile?), SetTileSize(w, h), ZOrder, Parallax, Visible,
        WrapHorizontally/WrapVertically, OriginPx, ShowGridLines, ShowCollisionBoxes,
        DefaultTileCollisionProfile ("World"),
        GridToWorldPx / WorldPxToGrid / GetAdjacentTile(tile, CardinalDirections),
        ColliderRegistry, RefreshQueue, ValueBag
    Tile (SceneLayerTile, Sprite): CurrentFrame, Visible, CollisionsEnabled, CollisionType,
        CollisionTypeByFrame, CollisionArea, AdjustCollisionArea (CollisionAdjust),
        AdjustCollisionAreaByFrame, CollisionProfileName, SetCollisionProfile(name),
        EnableAnimator, TileAnimator, ValueBag
    RenderSurfaceHost<T>: void Bind(Scene newScene, bool limitCameraToWorldBoundPx = true)
        (throws InvalidOperationException if the scene is bound to another host);
        ViewManager; Backbuffer; Effects; RedrawDirtyRectangleOnly;
        event Action<SKCanvas> RenderBackbufferPostScene
    ViewManager: ConfigureSingleFullView(float zoom = 1f, int zOrder = 0);
        ConfigureVerticalSplit(float leftZoom = 1f, float rightZoom = 1f);
        AddView(Rectangle targetRectPx, float zoom = 1f, int zOrder = 0, RectangleF? worldBoundsPx = null);
        ClearViews(); Views
    View: Camera; Viewport; ZOrder; MinZoom 0.1; MaxZoom 8;
        ZoomAroundScreenPoint(layer, screenPoint, targetZoom, durationSeconds);
        ScreenPxToWorldPx / WorldPxToScreenPx / ScreenPxToGrid /
        WorldRectToScreenRect / ScreenRectToWorldRect
    Viewport: Zoom (>1 = zoomed IN); SnapZoom(zoom); ZoomTo(zoom, lerpPerSecond);
        ZoomToOverDuration(zoom, durationSeconds); TargetRectPx; Resize(w, h);
        ScreenOffsetPx; VisibleWorldSizePx; events TargetRectChanged, ZoomChanged

TILESHEETS / SPRITES / ANIMATION
    TilesheetRegistry.Instance: LoadFromImageFile(string name, string imageFilePath);
        LoadFromBitmap(name, SKBitmap); LoadFromStream(name, Stream);
        LoadFromAssetsFile(AssetsFile, string entryName); LoadFromDefinitionFile(string gtsPath);
        LoadFromDefinition(TilesheetDefinition, string? baseDirectory = null);
        LoadFromDefinitionAsset(AssetsFile, string gtsEntryName); TryGet; GetOrNull; this[name]
    Tilesheet: DefaultRegion, Regions, GetRegion(name), this[regionName],
        this[regionName, x, y], GetFrame(x, y), ApplyMask(SKColor? maskColor = null, byte tolerance = 5);
        AddRegion(string name, Rectangle area, Size tileSize, Spacing? tilePadding = null,
        Spacing? regionMargin = null, CollisionAdjust? collisionAdjust = null,
        TileCollisionType collisionType = None);
        PersistImageToFile(string path, SKEncodedImageFormat format = Png, int quality = 100)
    TilesheetRegion: CollisionAdjust; CollisionType; CollisionArea;
        Get/TryGet…Override/Set/Clear FrameCollisionAdjust(x, y, …);
        Get/TryGet…Override/Set/Clear FrameCollisionType(x, y, …); GetFrameCollisionArea(x, y)
    Frame: CollisionAdjust; CollisionArea; HasCollisionAdjustOverride;
        ClearCollisionAdjustOverride(); CollisionType; HasCollisionTypeOverride;
        ClearCollisionTypeOverride()
    TilesheetDefinitionSerializer: Load(string|Stream); Save(string filePath, TilesheetDefinition);
        FromJson(string); ToJson(TilesheetDefinition); FromTilesheet(Tilesheet, string? baseDirectory = null,
        bool makePathsRelative = false); Save(string filePath, Tilesheet, bool makePathsRelative = true)
        (the Tilesheet overload writes a sibling .png for a bitmap-only sheet
         and re-points the sheet at it)
    SpriteManager.Instance: Sprite CreateSprite(SceneLayer sceneLayer, Frame frame, string? id = null,
        string? collisionProfileName = null); CloneSprite(Sprite[, SceneLayer]);
        Sprite? GetSpriteByID(string ID); GetSpritesAtViewPixel(...);
        GetSpritesInWorldRectRange(...); GetSpritesInViewRectRange(...);
        bool SizeNewSpritesToSceneLayer; string DefaultCollisionProfile ("Actor")
    Sprite: SetPosition(Vector2 pos) (grid); Visible; RenderSize; Rotation (degrees, clockwise);
        VisualBoundsWorld; GetVisualBoundsScreen(View); Movement; TileAnimator;
        ResizeTo / ScaleBy / PulseTo / PulseBy / StopPulse / CancelResize; StartJiggle / JiggleOnce / StopJiggle
    FrameSequence: AddFrame(sheet, x, y); SequenceCycleType (CycleType Simple/Repeating/PingPong)
    Cycle(FrameSequence seq, double throttleSeconds, string key); NextCycle
    Animator: CurrentCycle; StartAnimation(); events Started, Stopped, Cycled

MOVEMENT / COLLISION
    MovementController: MoveTo(target, seconds, EasingKind|Func); MoveBy(delta, seconds, easing)
        or MoveBy(delta, speed); MoveToward(target, speedPerSec); OnBeginning(Action); OnComplete(Action);
        CancelScript(); StopAllMovement(); SetVelocity / SetAcceleration / SetMaxSpeed / SetLinearDamping;
        FollowPixelSoft/Hard, FollowTileSoft/Hard, Unfollow(); StopAllMovement() also unfollows;
        WrapX / WrapY (public setters); MovementState; IsScripted
    TileCollider(Tile tile, int collisionGroup, int collidesWith,
                 CollisionResponseType responseType = Solid)
    TileCollisionType: None, Blocking, Trigger   (serialized as a string)
    CollisionAdjust(int top, int bottom, int left, int right): Top/Bottom/Left/Right are
        pixel INSETS on their own edge (positive shrinks the box); None; ApplyTo(Rectangle)
    CollisionProfileNames: World, Actor, Projectile, Sensor
    CollisionProfileRegistry (Scene.CollisionProfiles): Define(name, collisionGroup,
        collidesWith, collidesWithAll); Get(name); TryGet(name, out); GetProfileNames()
    CollisionGroupRegistry: int Define(string name); int Get(string name);
        int GetMask(IEnumerable<string> names); WorldStatic/Actors/Projectiles/Triggers
    ColliderRegistry.QueryAabb(in Aabb area, int layerMask, int collidesWithMask,
                               List<ICollider> results, ICollider? ignore = null)
    Aabb(float minX, float minY, float maxX, float maxY): Intersects(in Aabb), Center, ToRectangle()

EFFECTS / LIGHTING / COMPONENTS
    host.Effects (EffectsManager): Run<TEffect>(View|SceneLayer target, TEffect effect);
        Cancel(effect); CancelAll(); ActiveEffects
    FadeInEffect / FadeOutEffect(float seconds, EasingKind = Linear)
    SlideInEffect / SlideOutEffect(EffectDirection, float seconds, EasingKind)
    FillEffect / EraseEffect(EffectDirection, float seconds, EasingKind = Linear)
    ZoomInEffect / ZoomOutEffect(float targetZoom, float seconds)          -- View only
    EarthquakeEffect(float seconds, float intensityPx = 8f, bool decay = true,
                     int? randomSeed = null)                               -- View only
    DisplayEffect: Id, DurationSeconds, Easing, Status (EffectStatus), Progress,
        events Completed / Cancelled, Cancel()
    DirectRadialLight(Color, host, SceneLayer, PointF centerWorldPx, float radiusWorldPx, nickname)
    DirectLightLayer(host, SceneLayer): AddTorchLight(centerWorldPx, radiusWorldPx, color, nickname)
    DirectDarknessOverlay(host, View, SceneLayer projectionLayer, nickname)
    DirectSceneLayerDarknessOverlay(host, SceneLayer, Rectangle worldBounds, nickname)
        both: AddRevealSource / TrackLight / TrackLightLayer / UntrackLight / ClearRevealSources
    SplashOverlay.TryCreate(string|Stream image, host, View, fadeIn 0.45f, hold 3f,
        fadeOut 0.45f, onHolding, onHoldingAsync, onSplashCompleted, nickname) -> null on failure
    HealthBar(host, Sprite target, float maxValue, Size? size = null, Point? offsetPx = null,
        string? nickname = null): SetValue / SetFillColor / SetTrackColors /
        SetThresholdColors / SetThresholds / Show / Hide

AUDIO / MUSIC
    AudioResourceManager.Instance: LoadFromFile / LoadFromStream / LoadFromPcm(key, data, rate,
        bits, channels) / LoadFromEngineAssetsFile(pack); bool TryPlaySfx(string key, float volume,
        float pan, int priority); SfxPool; PreloadShortSoundEffectMaxSeconds
    AudioSystem.Initialize(int sampleRate, int channels); AudioSystem.Shutdown()
    SoundChannel: SetClip(key); Play(volume, pan, pitch); Volume/Pan/Pitch; State
    StreamingAudioSource: FillAudioBuffer(Span<float>) callback or ISampleProvider; Start/Stop; Volume
    AudioMixer: MasterVolume, MusicVolume, SfxVolume, MusicDuckMultiplier (read-only)
    MusicManager.Instance: Play(track, fadeIn[, MusicTransitionQuantize]); CrossfadeTo(track,
        TimeSpan duration[, MusicTransitionQuantize]); Stop(fadeOut[, quantize]); Pause(); Resume();
        Seek(); PushDuck(depth, attack, release) -> IDisposable; Duck(depth, attack, hold, release);
        ClearDucks(); PlayStinger(key, volume, duckMusic); Play(playlist, crossfade); Next(crossfade);
        JumpToMarker(name); HasPendingTransition; CancelPendingTransition(); NowPlaying; IsPlaying
    MusicTimeline(beatsPerMinute, beatsPerBar[, offsetSeconds, markers]);
        MusicTimeline(MidiTempoMap, beatsPerBar[, offsetSeconds, markers]);
        static FromMidiFile(path) / FromMidiEvents(events) / FromMidiSequence(sequence, beatsPerBar);
        TempoMap; HasTempoChanges; BeatsPerMinute; BeatsPerBar; SecondsPerBeat; SecondsPerBar;
        OffsetSeconds; Markers; TimeToNextBoundary(position, quantize); TryGetMarker(name, out time);
        MusicMarker(string Name, TimeSpan Time)
    MusicStemSet(key, params stem paths) / (key, IReadOnlyDictionary<string,string>) /
        (key, names, decoded): this[stem].FadeTo(volume, TimeSpan); Stems; Count; Problems;
        static FromSunoStems(key, stemsZipOrFolder[, SunoLoadOptions], params stemNames)
    MidiMusicTrack(key, instrumentPath, midiFilePath) -- .sf2 / .sfz / .dspreset / .dslibrary /
        .dsbundle / a folder holding a preset; (key, SoundFont | SfzInstrument |
        DecentSamplerInstrument, MidiSequence); Problems; Player (MPE lives here);
        SetLayerVolume(channel, v); FadeLayerTo(channel, v, TimeSpan); SetLayerPan; Speed

SAVE / LOAD / ASSETS / CONFIG
    EngineState: SaveToFile(path, compress); static LoadFromFile(path, compressed[, parts]);
        static MergeFromFile(path, overwriteExisting, parts); SerializerOptions; ValueBag
    AssetsFile.LoadOrCreate(path); Get(AssetTypes, name); this[AssetTypes, name]; Add(AssetTypes, path); Save()
    EngineConfiguration: TargetFPS, VSync, MsaaSampleCount, TimeBetween*Events,
        TimeBetweenGamepadStateUpdates, LoggingMode, LoggingQueueCapacity,
        FlushAsyncLogsOnShutdown, PauseSuspendsAudio, PauseShortSoundEffectSeconds,
        StateFiles, ConfigurationSections
    EngineConfigurationFile: static CreateNew/Load(string? configFileName = null,
        bool? autoSave = null); FileName; FilePath; AutoSave; EngineConfig; Save();
        Save(string jsonPath); Dispose()  -- JSON root key "EngineConfig";
        the default file name is RELATIVE to the working directory; read once
    TypedValueBag: Set<T>(ValueKey<T>, T); Get<T>(ValueKey<T>, T defaultValue = default);
        TryGet<T>(ValueKey<T>, out T?); Remove<T>; Contains; Clear(); Clone()

================================================================================
END OF AGENT-README
================================================================================
