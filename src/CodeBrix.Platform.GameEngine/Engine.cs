using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Configuration;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Input.Touch;
using CodeBrix.Platform.GameEngine.Extensibility;
using CodeBrix.Platform.GameEngine.Logging;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Timers;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Timer = CodeBrix.Platform.GameEngine.Timers.Timer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine; //was previously: Gondwana;
/// <summary>
/// Represents the core game engine singleton responsible for managing the main update loop,
/// input systems, rendering, timing, and subsystem coordination.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Engine"/> class is implemented as a thread-safe singleton and provides
/// centralized access to all major engine subsystems including input polling, scene management,
/// rendering, and collision detection.
/// </para>
/// <para>
/// Typical usage involves calling <see cref="Initialize"/> to configure the engine,
/// then <see cref="Start(SynchronizationContext)"/> to begin the main loop, and finally
/// <see cref="Stop"/> followed by <see cref="Dispose"/> for cleanup.
/// </para>
/// </remarks>
public sealed class Engine : IDisposable
{
    #region static members

    private static readonly Lazy<Engine> _instance = new(() => new Engine());
    
    /// <summary>
    /// Gets the singleton instance of the <see cref="Engine"/>.
    /// </summary>
    /// <value>The globally shared <see cref="Engine"/> instance.</value>
    /// <remarks>
    /// This property provides thread-safe access to the engine instance using lazy initialization.
    /// The instance is created on first access and persists for the application lifetime.
    /// </remarks>
    public static Engine Instance => _instance.Value;

    /// <summary>
    /// Gets the logger instance used by the engine for diagnostic and informational messages.
    /// </summary>
    /// <value>An <see cref="ILogger{TCategoryName}"/> instance configured for the <see cref="Engine"/> type.</value>
    /// <remarks>
    /// This logger is used internally by the engine to report initialization status,
    /// errors, warnings, and other runtime information.
    /// </remarks>
    public static ILogger<Engine> Logger => EngineLogger.GetLogger<Engine>();

    #endregion

    #region private fields

    private long _startTick;
    private long _lastCPSSamplingTick;
    private long _lastBackgroundTick = HighResTimer.GetCurrentTick();
    private long _lastForegroundTick = HighResTimer.GetCurrentTick();
    private long _lastCycleTick = HighResTimer.GetCurrentTick();

    private long _grossCyclesThisMeasure = 0;
    private long _netCyclesThisMeasure = 0;
    private double _grossCPS = 0;
    private double _netFPS = 0;

    private Task? _cycleTask;

    // Throttling state for logging unhandled exceptions raised from within an engine cycle.
    private long _lastCycleExceptionLogTick;
    private int _suppressedCycleExceptionCount;

    // Global pause state (see Pause/Resume). _pauseGate serializes the Pause/Resume state
    // flips; _parkMonitor is the cycle loop's park/wake handshake, kept separate so waiters
    // never hold the state gate while blocked.
    private readonly object _pauseGate = new();
    private readonly object _parkMonitor = new();
    private bool _cycleLoopParked;
    private volatile bool _isPaused;
    private volatile bool _pauseTransitionDone;
    private volatile bool _inCycle;
    private bool _isTimerDriven;
    private long _pauseStartTick;

    // Game loops (framebuffer-style games) that pause and resume with the global engine
    // pause; see FixedRateGameLoop.PauseWithEngine.
    private readonly List<FixedRateGameLoop> _enginePausableLoops = new();

    #endregion private fields

    #region events

    /// <summary>
    /// Occurs immediately before the engine begins its internal initialization sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This event is raised once per engine lifetime, the first time <see cref="Initialize"/> 
    /// is called. It provides an early hook for systems that must perform setup prior to
    /// configuration loading or input subsystem initialization.
    /// </para>
    /// <para>
    /// If a <see cref="UiDispatcher"/> is available, this event is posted to the UI thread;
    /// otherwise, it executes on the calling thread.
    /// </para>
    /// </remarks>
    public event Action? PreInitialization;

    /// <summary>
    /// Occurs after all internal initialization routines have completed, but before
    /// <see cref="InitializationComplete"/> is raised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This event is raised once per engine lifetime, following successful configuration
    /// loading, state restoration, and adapter setup.
    /// </para>
    /// <para>
    /// Use this event for post-initialization logic that depends on fully loaded engine
    /// settings but precedes runtime activation.
    /// </para>
    /// <para>
    /// If a <see cref="UiDispatcher"/> is available, this event is posted to the UI thread;
    /// otherwise, it executes on the calling thread.
    /// </para>
    /// </remarks>
    public event Action? PostInitialization;

    /// <summary>
    /// Occurs after all initialization steps and post-initialization logic have completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This event is raised at the end of <see cref="Initialize"/>, every time the method is called.
    /// It signifies that the engine and its subsystems are fully active and ready for runtime operations.
    /// </para>
    /// <para>
    /// If a <see cref="UiDispatcher"/> is available, this event is posted to the UI thread;
    /// otherwise, it executes on the calling thread.
    /// </para>
    /// </remarks>
    public event Action? InitializationComplete;

    /// <summary>
    /// Occurs immediately before <see cref="DoBackgroundTasks(long)"/> executes within each engine cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this event to inject custom background logic such as diagnostics, AI updates,
    /// or subsystem polling prior to the engine's own background operations.
    /// </para>
    /// </remarks>
    public event Action? BeforeBackgroundTasksExecute;

    /// <summary>
    /// Occurs immediately after <see cref="DoBackgroundTasks(long)"/> has completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this event to perform custom actions or monitoring after all background updates 
    /// (timers, input, animations, surface refreshes, etc.) have been processed.
    /// </para>
    /// </remarks>
    public event Action? AfterBackgroundTasksExecute;

    /// <summary>
    /// Occurs immediately before <see cref="DoForegroundTasks(long)"/> executes within each engine cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this event to perform per-frame setup tasks prior to rendering or to update 
    /// game state that must occur before foreground drawing.
    /// </para>
    /// </remarks>
    public event Action? BeforeFrameRender;

    /// <summary>
    /// Occurs immediately after <see cref="DoForegroundTasks(long)"/> completes within each engine cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this event to perform logic that depends on a completed render frame,
    /// such as post-render effects, profiling, or scheduling background jobs.
    /// </para>
    /// </remarks>
    public event Action? AfterFrameRender;

    /// <summary>
    /// Occurs whenever cycles-per-second (CPS) and frames-per-second (FPS) metrics are calculated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised at a regular interval defined by <see cref="EngineConfiguration.SamplingTimeForCPS"/>.
    /// Provides a snapshot of gross and net cycle rates, total elapsed time, and sample interval 
    /// through a <see cref="CyclesPerSecondCalculatedEventArgs"/> payload.
    /// </para>
    /// <para>
    /// This event is posted to the UI thread when a <see cref="UiDispatcher"/> is available.
    /// </para>
    /// </remarks>
    public event Action<CyclesPerSecondCalculatedEventArgs>? CPSCalculated;

    /// <summary>
    /// Raised when the global engine pause takes effect — the game's "do this when paused"
    /// hook (save the game, build a pause screen, and so on).
    /// </summary>
    /// <remarks>
    /// <para>
    /// By the time this event is raised, game execution is quiescent: registered game loops
    /// have parked (their tic in progress completed), and in engine-cycle mode the event is
    /// raised ON THE ENGINE THREAD between cycles — handlers have race-free access to game
    /// state, which makes this the right place for a save-game routine.
    /// </para>
    /// <para>
    /// <see cref="LastFrameBeforePause"/> is already captured when handlers run, so a handler
    /// can use it (for example, a dimmed copy of it) to build a pause screen. In engine-cycle
    /// mode, one final frame is rendered AFTER this event returns — ignoring the
    /// <see cref="EngineConfiguration.TargetFPS"/> throttle — so scene changes a handler makes
    /// (a "PAUSED" overlay, a dimmed snapshot) actually reach the screen before rendering
    /// halts. A software-rendered (framebuffer) game instead presents its own pause frame
    /// directly from the handler; presentation stays available while the loop is parked.
    /// </para>
    /// <para>
    /// Engine input pollers do not run while paused — the resume trigger must come from the
    /// hosting application's UI layer (window restore, a UI-level key or pointer handler).
    /// </para>
    /// </remarks>
    public event Action? Paused;

    /// <summary>
    /// Raised when <see cref="Resume"/> lifts the global engine pause, on the caller's
    /// thread, after time baselines have been shifted and suspended audio resumed but BEFORE
    /// the engine cycle and registered game loops wake — so handlers still see quiescent game
    /// state (the right place to tear down a pause screen).
    /// </summary>
    public event Action? Resumed;

    /// <summary>
    /// Raised when <see cref="Dispose()"/> begins the explicit disposal sequence.
    /// </summary>
    /// <remarks>
    /// Fired only when <see cref="Dispose()"/> is called (never from the finalizer).
    /// Handlers run before managed cleanup while engine state is still readable.
    /// If a <see cref="UiDispatcher"/> is available, this event is posted to the UI thread.
    /// </remarks>
    public event Action? Disposing;

    /// <summary>
    /// Raised after the engine has completed explicit disposal.
    /// </summary>
    /// <remarks>
    /// Fired only when <see cref="Dispose()"/> is called (never from the finalizer).
    /// Indicates all managed cleanup has completed and <see cref="IsDisposed"/> is <c>true</c>.
    /// If a <see cref="UiDispatcher"/> is available, this event is posted to the UI thread.
    /// </remarks>
    public event Action? Disposed;

    #endregion events

    private Engine() { }

    private volatile bool _isInitialized = false;
    private volatile bool _isInitializing = false;
    private readonly ManualResetEventSlim _initDone = new(false);

    /// <summary>
    /// Performs one-time or on-demand initialization of the <see cref="Engine"/> instance, 
    /// loading configuration, state files, and input adapters required for execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is responsible for preparing all core systems of the engine prior to starting
    /// the main loop. It performs the following operations in order:
    /// </para>
    /// <list type="number">
    ///   <item><description>Raises the <see cref="PreInitialization"/> event (on the UI thread if available).</description></item>
    ///   <item><description>Loads engine configuration settings from file using <see cref="EngineConfigurationFile.Load"/>.</description></item>
    ///   <item><description>Loads any <see cref="EngineState"/> files declared in configuration.</description></item>
    ///   <item><description>Initializes input subsystems for keyboard, mouse, and gamepad polling, 
    ///     if corresponding adapters are provided.</description></item>
    ///   <item><description>Raises <see cref="PostInitialization"/> after all internal setup is complete.</description></item>
    ///   <item><description>Marks the engine as initialized and raises <see cref="InitializationComplete"/>.</description></item>
    /// </list>
    /// <para>
    /// This method is automatically invoked by <see cref="Start(SynchronizationContext)"/> if the engine 
    /// has not yet been initialized. It is safe to call multiple times, but subsequent calls will 
    /// return immediately once initialization has been completed or is in progress.
    /// </para>
    /// <para>
    /// Thread-safe guarantees:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Concurrent calls are prevented by internal <c>_isInitializing</c> and <c>_isInitialized</c> flags.</description></item>
    ///   <item><description>Events that must run on the UI thread are dispatched through <see cref="UiDispatcher"/> if available.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="configFileName">
    /// Optional path to a configuration file to load. If <c>null</c>, the default configuration is used.
    /// </param>
    /// <param name="autoSaveConfig">
    /// Optional flag indicating whether configuration changes should be automatically saved back to disk.
    /// </param>
    /// <param name="keyboardAdapter">
    /// Optional <see cref="IKeyboardAdapter"/> instance used to initialize the keyboard input subsystem.
    /// </param>
    /// <param name="mouseAdapter">
    /// Optional <see cref="IMouseAdapter"/> instance used to initialize the mouse input subsystem.
    /// </param>
    /// <param name="touchAdapter">
    /// Optional <see cref="ITouchAdapter"/> instance used to initialize the touch input subsystem.
    /// </param>
    /// <param name="gamepadManager">
    /// Optional <see cref="IGamepadManager{T}"/> instance used to initialize the gamepad subsystem.
    /// </param>
    /// <seealso cref="Start(SynchronizationContext)"/>
    /// <seealso cref="Stop"/>
    /// <seealso cref="EngineConfiguration"/>
    /// <seealso cref="EngineState"/>
    public void Initialize(
        string? configFileName = null,
        bool? autoSaveConfig = null,
        IKeyboardAdapter? keyboardAdapter = null,
        IMouseAdapter? mouseAdapter = null,
        ITouchAdapter? touchAdapter = null,
        IGamepadManager<IGamepadAdapter>? gamepadManager = null)
    {
        if (_isInitialized || _isInitializing)
            return;

        // reset in case this instance has been initialized before
        _initDone.Reset();

        _isInitializing = true;

        if (UiDispatcher == null)
            PreInitialization?.Invoke();
        else
            UiDispatcher!.Post(() => PreInitialization?.Invoke());

        Configuration = EngineConfigurationFile.Load(configFileName, autoSaveConfig).EngineConfig;

        EngineLogger.Mode = Configuration.LoggingMode;

        if (Configuration.StateFiles?.Any() ?? false)
        {
            foreach (var stateFile in Configuration.StateFiles)
            {
                EngineState.MergeFromFile(stateFile.File, stateFile.IsCompressed, stateFile.OverwriteExisting, stateFile.EngineStateParts);
            }
        }

        if (keyboardAdapter != null)
            KeyboardEventPoller.Initialize(keyboardAdapter);

        if (mouseAdapter != null)
            MouseEventPoller.Initialize(mouseAdapter);

        if (touchAdapter != null)
            Input.TouchAdapter = touchAdapter;

        Input.GamepadManager = gamepadManager;

        if (UiDispatcher == null)
            PostInitialization?.Invoke();
        else
            UiDispatcher!.Post(() => PostInitialization?.Invoke());

        EnginePluginRegistry.InvokeInitialize(this);

        _isInitializing = false;
        _isInitialized = true;

        if (UiDispatcher == null)
            InitializationComplete?.Invoke();
        else
            UiDispatcher!.Post(() => InitializationComplete?.Invoke());

        // signal that init is done
        _initDone.Set();
    }

    /// <summary>
    /// Starts the <see cref="Engine"/> using the current thread's <see cref="SynchronizationContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This overload is intended for convenience when starting the engine from the UI thread.
    /// It retrieves the current <see cref="SynchronizationContext"/> and forwards it to 
    /// <see cref="Start(SynchronizationContext)"/>.
    /// </para>
    /// <para>
    /// The engine must be started from a thread that has a valid <see cref="SynchronizationContext"/>,
    /// typically the primary UI thread. If no synchronization context is available, an 
    /// <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="SynchronizationContext.Current"/> is <c>null</c>.
    /// </exception>
    /// <seealso cref="Start(SynchronizationContext)"/>
    /// <seealso cref="Initialize"/>
    /// <seealso cref="Stop"/>
    public void Start()
    {
        if (SynchronizationContext.Current == null)
            throw new InvalidOperationException("SynchronizationContext cannot be null.");

        Start(SynchronizationContext.Current);
    }

    /// <summary>
    /// Starts the <see cref="Engine"/> main loop using the provided <see cref="SynchronizationContext"/>,
    /// initializing the engine if it has not yet been started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is the entry point for runtime execution. It ensures the engine is fully initialized
    /// before beginning the continuous background processing loop. The loop runs on a separate worker 
    /// thread and repeatedly invokes <see cref="Cycle"/>, yielding between iterations to allow 
    /// cooperative multitasking.
    /// </para>
    /// <para>
    /// The <paramref name="uiContext"/> argument establishes the <see cref="UiDispatcher"/> used for 
    /// posting events and callbacks to the UI thread. All UI-bound events such as 
    /// <see cref="PreInitialization"/>, <see cref="PostInitialization"/>, 
    /// <see cref="InitializationComplete"/>, and <see cref="CPSCalculated"/> 
    /// will be marshalled through this dispatcher when available.
    /// </para>
    /// <para>
    /// If the engine is already running, this method returns immediately without taking further action.
    /// </para>
    /// <para>
    /// Threading behavior:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>The engine's main loop runs on a background task, not the UI thread.</description></item>
    ///   <item><description>All rendering and timing operations are controlled through <see cref="Cycle"/>.</description></item>
    ///   <item><description>The <see cref="UiDispatcher"/> guarantees that event notifications 
    ///   targeting the UI are executed safely on the originating thread.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="uiContext">
    /// The <see cref="SynchronizationContext"/> that defines the UI thread context to which 
    /// UI-related operations and events will be dispatched.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="uiContext"/> is <c>null</c>.
    /// </exception>
    /// <seealso cref="Initialize"/>
    /// <seealso cref="Stop"/>
    /// <seealso cref="Cycle"/>
    /// <seealso cref="UiDispatcher"/>
    public void Start(SynchronizationContext uiContext)
    {
        if (IsRunning)
            return;

        UiDispatcher = new UiDispatcher(uiContext);

        if (!IsInitialized)
        {
            if (IsInitializing)
            {
                _initDone.Wait();        // someone else is initializing—wait for it
            }
            else
            {
                Initialize();            // we're the initializer—do it now
            }
        }

        IsRunning = true;
        _isTimerDriven = false;

        _startTick = HighResTimer.GetCurrentTick();
        _lastCPSSamplingTick = _startTick;
        _lastCycleTick = _startTick;

        _cycleTask = Task.Run(() =>
        {
            EngineDispatcher.BindToCurrentThread();

            while (Instance.IsRunning)
            {
                // Globally paused (possibly before Start): transition and park at zero CPU.
                if (Instance._isPaused)
                {
                    Instance.PauseCycleLoop();
                    continue;
                }

                try
                {
                    Instance.Cycle();
                }
                catch (Exception ex)
                {
                    // A single unhandled exception in a cycle must never permanently kill the engine
                    // loop — that loop drives input polling, movement, timers and rendering, so losing
                    // it silently bricks the running game (the faulted Task is never observed). Log
                    // (throttled) and continue so the engine recovers from transient faults.
                    Instance.HandleCycleException(ex);
                }
                Thread.Yield(); // optional
            }
        });
    }

    /// <summary>
    /// Starts the <see cref="Engine"/> in timer-driven mode using the provided
    /// <see cref="SynchronizationContext"/>, without spawning a background thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This overload is designed for environments where a background thread is not viable,
    /// such as single-threaded WebAssembly (WASM) runtimes. Instead of spawning a
    /// <see cref="Task"/> to drive the loop, it binds the <see cref="EngineDispatcher"/>
    /// to the calling thread and returns immediately. The caller is then responsible for
    /// advancing the engine by calling <see cref="Tick"/> on each timer tick (e.g. via
    /// a platform-specific timer such as <c>DispatcherTimer</c>).
    /// </para>
    /// <para>
    /// Unlike <see cref="Start(SynchronizationContext)"/>, this method will throw if
    /// initialization is already in progress, because waiting for it to complete (via
    /// <c>ManualResetEventSlim.Wait</c>) is not safe on a single-threaded runtime.
    /// </para>
    /// </remarks>
    /// <param name="uiContext">
    /// The <see cref="SynchronizationContext"/> that defines the UI thread context to which
    /// UI-related operations and events will be dispatched.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="uiContext"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if initialization is currently in progress on another thread.
    /// </exception>
    /// <seealso cref="Tick"/>
    /// <seealso cref="Start(SynchronizationContext)"/>
    /// <seealso cref="Stop"/>
    public void StartTimerDriven(SynchronizationContext uiContext)
    {
        if (IsRunning)
            return;

        UiDispatcher = new UiDispatcher(uiContext);

        if (!IsInitialized)
        {
            if (IsInitializing)
                throw new InvalidOperationException(
                    "Engine initialization is already in progress on another thread. " +
                    "Blocking waits are not supported in timer-driven (single-threaded) mode.");

            Initialize();
        }

        // Bind the dispatcher to the calling (UI) thread — Tick() will be called from here.
        EngineDispatcher.BindToCurrentThread();

        IsRunning = true;
        _isTimerDriven = true;

        _startTick = HighResTimer.GetCurrentTick();
        _lastCPSSamplingTick = _startTick;
        _lastCycleTick = _startTick;

        // _cycleTask is intentionally left null; the caller drives the loop via Tick().
    }

    /// <summary>
    /// Advances the engine by one cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is intended for use with the timer-driven startup path initiated by
    /// <see cref="StartTimerDriven(SynchronizationContext)"/>. The caller (typically a
    /// platform timer such as a <c>DispatcherTimer</c>) should invoke this method once per
    /// timer tick to drive the engine loop.
    /// </para>
    /// <para>
    /// If the engine is not currently running, this method returns immediately without
    /// performing any work.
    /// </para>
    /// </remarks>
    /// <seealso cref="StartTimerDriven(SynchronizationContext)"/>
    /// <seealso cref="Stop"/>
    public void Tick()
    {
        if (!IsRunning)
            return;

        if (_isPaused)
        {
            // Timer-driven mode cannot park a thread; the tick itself becomes the pause
            // transition point (once per pause episode) and then a cheap no-op.
            RunPauseTransition(renderFinalFrame: true);
            return;
        }

        Cycle();
    }

    /// <summary>
    /// Stops the <see cref="Engine"/> main loop and halts all ongoing processing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method cleanly terminates the engine's background execution cycle started by 
    /// <see cref="Start(SynchronizationContext)"/>. It sets <see cref="IsRunning"/> to <c>false</c>,
    /// signaling the loop in <see cref="Cycle"/> to exit on the next iteration.
    /// </para>
    /// <para>
    /// <b>Stop()</b> does not immediately dispose of resources or clear state. It simply halts
    /// ongoing updates and rendering, allowing the engine's subsystems (timers, surfaces, 
    /// input pollers, etc.) to remain intact for later reuse or inspection.
    /// </para>
    /// <para>
    /// To fully clean up and release all managed resources, call <see cref="Dispose"/> after
    /// stopping the engine.
    /// </para>
    /// <para>
    /// This method is thread-safe and may be called from any thread.
    /// </para>
    /// </remarks>
    /// <seealso cref="Start()"/>
    /// <seealso cref="Cycle"/>
    /// <seealso cref="Dispose()"/>
    /// <seealso cref="IsRunning"/>
    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;

        // Release a cycle loop parked by the global pause so it can observe the stop.
        lock (_parkMonitor)
        {
            Monitor.PulseAll(_parkMonitor);
        }

        InvokeShutdownAfterCycleStops();
    }

    /// <summary>
    /// Pauses the engine globally: all rendering and all game operation halt near-immediately,
    /// in both engine-cycle mode (<see cref="Start()"/>/<see cref="StartTimerDriven"/>) and
    /// software-rendered framebuffer mode (game loops with
    /// <see cref="FixedRateGameLoop.PauseWithEngine"/> enabled). The cycle or tic in progress
    /// completes; at most one further frame is rendered (see <see cref="Paused"/>); then the
    /// loops park at zero CPU until <see cref="Resume"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pause sequence: registered game loops park (waiting for the tic in progress) →
    /// <see cref="LastFrameBeforePause"/> is captured → playing audio is suspended (unless
    /// <see cref="EngineConfiguration.PauseSuspendsAudio"/> is off; short fire-and-forget
    /// sound effects ring out — see
    /// <see cref="EngineConfiguration.PauseShortSoundEffectSeconds"/>) → <see cref="Paused"/>
    /// is raised → in engine-cycle mode, one final frame is rendered so pause-screen scene
    /// changes become visible.
    /// </para>
    /// <para>
    /// The call blocks until the engine is quiescent (bounded by roughly one cycle or tic),
    /// EXCEPT when called from the engine thread or a game-loop thread itself (a game pausing
    /// from inside its own update): then it returns immediately and the loop parks as soon as
    /// the current cycle/tic completes. Pausing before the engine or loop starts is valid —
    /// the loop then starts parked. Thread-safe and idempotent.
    /// </para>
    /// <para>
    /// While paused, engine input pollers do not run: wire the resume trigger at the hosting
    /// application's UI layer (window restore, a UI-level key or pointer handler).
    /// </para>
    /// </remarks>
    /// <seealso cref="Resume"/>
    /// <seealso cref="IsPaused"/>
    /// <seealso cref="Paused"/>
    /// <seealso cref="LastFrameBeforePause"/>
    public void Pause()
    {
        lock (_pauseGate)
        {
            if (_isPaused)
                return;

            _pauseStartTick = HighResTimer.GetCurrentTick();
            _pauseTransitionDone = false;
            _isPaused = true;
        }

        // Park every registered engine-pausable game loop (framebuffer-style games) and wait
        // for their tics in progress to complete, so game state is quiescent when Paused is
        // raised. WaitUntilPaused returns immediately on a loop's own thread.
        var loops = SnapshotEnginePausableLoops();
        foreach (var loop in loops)
            loop.EnginePause();
        foreach (var loop in loops)
            loop.WaitUntilPaused();

        if (IsRunning && !_isTimerDriven)
        {
            // The engine cycle loop performs the pause transition (snapshot capture → audio
            // suspend → Paused event → final frame) and parks itself; wait for the park
            // unless this IS the engine thread (then the loop parks right after the current
            // cycle's handler returns).
            if (!EngineDispatcher.IsOnEngineThread)
                WaitForCycleLoopPark();
            return;
        }

        if (_isTimerDriven && _inCycle)
        {
            // Pause() called from inside a timer-driven cycle: the next Tick() runs the
            // transition, avoiding re-entry into the rendering path.
            return;
        }

        // Timer-driven (between ticks) or engine not running: transition inline on the
        // caller's thread. A final frame only makes sense when the engine is cycling.
        RunPauseTransition(renderFinalFrame: IsRunning);
    }

    /// <summary>
    /// Lifts the global engine pause: every time baseline (timers, sprite movement,
    /// animations, direct drawings, the engine's own cycle clocks) is shifted past the paused
    /// interval FIRST — so nothing sees the pause as elapsed time (no sprite teleports, no
    /// timer or animation burst) — then suspended audio resumes, <see cref="Resumed"/> is
    /// raised, and finally the engine cycle and registered game loops wake. Thread-safe and
    /// idempotent; safe to call from any thread except the parked loops' own (which cannot
    /// run while parked).
    /// </summary>
    /// <seealso cref="Pause"/>
    /// <seealso cref="Resumed"/>
    public void Resume()
    {
        bool transitioned;

        lock (_pauseGate)
        {
            if (!_isPaused)
                return;

            long resumeTick = HighResTimer.GetCurrentTick();
            long pausedTicks = resumeTick - _pauseStartTick;
            transitioned = _pauseTransitionDone;

            RebaselineAfterPause(pausedTicks, resumeTick);

            _isPaused = false;
            _pauseTransitionDone = false;
        }

        // Resume exactly the voices the pause suspended (no-op when nothing was suspended).
        try
        {
            AudioPauseRegistry.ResumeAll();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resume suspended audio after the engine pause.");
        }

        // Raise Resumed on the caller's thread while the loops are still parked, so handlers
        // see quiescent game state. Skipped when the pause never transitioned (a
        // pause/resume flicker faster than the loop could react) so Paused/Resumed stay
        // symmetric.
        if (transitioned)
            SafeInvoke(Resumed);

        // Wake the engine cycle loop...
        lock (_parkMonitor)
        {
            Monitor.PulseAll(_parkMonitor);
        }

        // ...and the registered game loops.
        foreach (var loop in SnapshotEnginePausableLoops())
            loop.EngineResume();
    }

    /// <summary>
    /// Registers a game loop that pauses and resumes with the global engine pause. Called by
    /// <see cref="FixedRateGameLoop"/> when it starts with
    /// <see cref="FixedRateGameLoop.PauseWithEngine"/> enabled.
    /// </summary>
    /// <param name="loop">The loop to register.</param>
    internal void RegisterEnginePausableLoop(FixedRateGameLoop loop)
    {
        lock (_enginePausableLoops)
        {
            if (!_enginePausableLoops.Contains(loop))
                _enginePausableLoops.Add(loop);
        }

        // A loop registered while the engine is already paused parks immediately.
        if (_isPaused)
            loop.EnginePause();
    }

    /// <summary>
    /// Unregisters a game loop from the global engine pause. Called by
    /// <see cref="FixedRateGameLoop"/> when it stops.
    /// </summary>
    /// <param name="loop">The loop to unregister.</param>
    internal void UnregisterEnginePausableLoop(FixedRateGameLoop loop)
    {
        lock (_enginePausableLoops)
        {
            _enginePausableLoops.Remove(loop);
        }
    }

    private FixedRateGameLoop[] SnapshotEnginePausableLoops()
    {
        lock (_enginePausableLoops)
        {
            return _enginePausableLoops.ToArray();
        }
    }

    private void WaitForCycleLoopPark()
    {
        lock (_parkMonitor)
        {
            while (_isPaused && IsRunning && !_cycleLoopParked)
                Monitor.Wait(_parkMonitor);
        }
    }

    /// <summary>
    /// The engine cycle loop's pause path: runs the pause transition (idempotent per pause
    /// episode), then parks at zero CPU until <see cref="Resume"/> or <see cref="Stop"/>.
    /// Runs on the engine thread.
    /// </summary>
    private void PauseCycleLoop()
    {
        RunPauseTransition(renderFinalFrame: true);

        lock (_parkMonitor)
        {
            _cycleLoopParked = true;
            Monitor.PulseAll(_parkMonitor); // release Pause() callers waiting for the park

            // Wake on any pulse and return to the outer loop, which re-checks the pause
            // state — so a new pause episode that begins while the loop is still parked from
            // the previous one still gets its own transition (Paused event, snapshot, audio).
            // Spurious wakes are safe: the outer loop just re-parks.
            if (_isPaused && IsRunning)
                Monitor.Wait(_parkMonitor);

            _cycleLoopParked = false;
        }
    }

    /// <summary>
    /// The once-per-pause-episode transition: capture <see cref="LastFrameBeforePause"/>,
    /// suspend audio, raise <see cref="Paused"/>, and (in engine-cycle mode) render one final
    /// frame so pause-screen scene changes become visible.
    /// </summary>
    /// <param name="renderFinalFrame">Whether to render a final frame after the Paused event.</param>
    private void RunPauseTransition(bool renderFinalFrame)
    {
        lock (_pauseGate)
        {
            if (_pauseTransitionDone || !_isPaused)
                return;

            _pauseTransitionDone = true;
        }

        CaptureLastFramesBeforePause();

        if (Configuration.PauseSuspendsAudio)
        {
            try
            {
                AudioPauseRegistry.SuspendAll(Configuration.PauseShortSoundEffectSeconds);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to suspend audio for the engine pause.");
            }
        }

        SafeInvoke(Paused);

        if (renderFinalFrame)
        {
            try
            {
                RenderPausedFrame();
            }
            catch (Exception ex)
            {
                HandleCycleException(ex);
            }
        }
    }

    /// <summary>
    /// Captures what the viewer is seeing on every render surface at the moment the pause
    /// takes effect — before the <see cref="Paused"/> event, so handlers can use the images.
    /// Scene-pipeline surfaces snapshot their backbuffers; framebuffer presenters copy their
    /// newest presented frame. GL-thread-rendered (GPU) surfaces are skipped.
    /// </summary>
    private void CaptureLastFramesBeforePause()
    {
        SKImage? first = null;

        try
        {
            foreach (var surface in RenderSurfaceHostRegistry.All.ToArray())
            {
                if (surface.Backbuffer.IsGlThreadRendered)
                    continue;

                SKImage? snapshot = null;
                try
                {
                    snapshot = surface.Backbuffer.Snapshot();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to snapshot a render surface for the engine pause.");
                }

                surface.LastFrameBeforePause?.Dispose();
                surface.LastFrameBeforePause = snapshot;
                first ??= snapshot;
            }

            var presenterCapture = PixelFramePresenter.CaptureAllForEnginePause();
            first ??= presenterCapture;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to capture the last frame before the engine pause.");
        }

        LastFrameBeforePause = first;
    }

    /// <summary>
    /// Renders and presents one frame outside the normal cycle — the final frame after the
    /// <see cref="Paused"/> event, ignoring the TargetFPS throttle. Runs on the thread that
    /// performs the pause transition.
    /// </summary>
    private void RenderPausedFrame()
    {
        long tick = HighResTimer.GetCurrentTick();

        foreach (var surface in RenderSurfaceHostRegistry.All.ToArray())
        {
            if (surface.Backbuffer.IsGlThreadRendered)
                continue;

            surface.RenderToBackbuffer(tick);
            surface.PresentBackbufferToAdapter();
        }
    }

    /// <summary>
    /// Shifts every time baseline in the engine past the paused interval, so the first
    /// resumed cycle sees no time jump: no giant movement delta, no timer burst, no animation
    /// churn. Baselines captured DURING the pause (objects created by a Paused handler) are
    /// never pushed into the future. Also shifts the engine start tick so
    /// <see cref="TotalTicksEngineRunning"/> excludes paused time. Caller holds the pause gate.
    /// </summary>
    /// <param name="pausedTicks">The duration of the pause, in ticks.</param>
    /// <param name="resumeTick">The current tick at the moment of resume.</param>
    private void RebaselineAfterPause(long pausedTicks, long resumeTick)
    {
        if (pausedTicks <= 0)
            return;

        if (IsRunning)
        {
            _startTick += pausedTicks;
            _lastCycleTick = HighResTimer.ShiftBaselineForResume(_lastCycleTick, pausedTicks, resumeTick);
            _lastBackgroundTick = HighResTimer.ShiftBaselineForResume(_lastBackgroundTick, pausedTicks, resumeTick);
            _lastForegroundTick = HighResTimer.ShiftBaselineForResume(_lastForegroundTick, pausedTicks, resumeTick);
            _lastCPSSamplingTick = HighResTimer.ShiftBaselineForResume(_lastCPSSamplingTick, pausedTicks, resumeTick);
        }

        Timer.ShiftAllForResume(pausedTicks, resumeTick);
        SpriteManager.Instance.ShiftTimeBaselineForResume(pausedTicks, resumeTick);
        DirectDrawingManager.Instance.ShiftTimeBaselinesForResume(pausedTicks, resumeTick);

        foreach (var tile in Tile.TilesAnimating.ToArray())
            tile.TileAnimator?.ShiftTimeBaselineForResume(pausedTicks, resumeTick);
    }

    private void InvokeShutdownAfterCycleStops()
    {
        var cycleTask = _cycleTask;

        if (cycleTask is not null && !cycleTask.IsCompleted)
        {
            cycleTask.ContinueWith(
                _ => EnginePluginRegistry.InvokeShutdown(this),
                System.Threading.CancellationToken.None,
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
                System.Threading.Tasks.TaskScheduler.Default);
            return;
        }

        EnginePluginRegistry.InvokeShutdown(this);
    }

    #region public properties

    /// <summary>
    /// Gets the UI dispatcher used for marshalling operations to the UI thread.
    /// </summary>
    /// <value>
    /// An <see cref="IUiDispatcher"/> instance if the engine was started with a valid
    /// <see cref="SynchronizationContext"/>; otherwise, <c>null</c>.
    /// </value>
    /// <remarks>
    /// This dispatcher is established when <see cref="Start(SynchronizationContext)"/> is called
    /// and is used to post events and operations that must execute on the UI thread.
    /// </remarks>
    public IUiDispatcher? UiDispatcher { get; private set; }

    /// <summary>
    /// Gets the engine dispatcher used for marshalling operations to the engine's background thread.
    /// </summary>
    /// <value>An <see cref="IEngineDispatcher"/> instance bound to the engine's update loop thread.</value>
    /// <remarks>
    /// This dispatcher allows external code to safely post work items that should execute
    /// on the engine's dedicated background thread, ensuring thread-safe access to engine state.
    /// </remarks>
    /// <returns>The result.</returns>
    public IEngineDispatcher EngineDispatcher { get; } = new EngineDispatcher();

    /// <summary>
    /// Gets a value indicating whether the engine has completed its initialization sequence.
    /// </summary>
    /// <value><c>true</c> if initialization is complete; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// This property returns <c>true</c> after <see cref="Initialize"/> has successfully
    /// completed all setup operations and raised the <see cref="InitializationComplete"/> event.
    /// </remarks>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets a value indicating whether the engine is currently in the process of initializing.
    /// </summary>
    /// <value><c>true</c> if initialization is in progress; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// This property is <c>true</c> between the start of <see cref="Initialize"/> and
    /// the completion of all initialization steps. It is used to prevent concurrent initialization attempts.
    /// </remarks>
    public bool IsInitializing => _isInitializing;

    /// <summary>
    /// Gets a value indicating whether the engine's main loop is currently executing.
    /// </summary>
    /// <value><c>true</c> if the engine loop is active; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// This property is set to <c>true</c> when <see cref="Start(SynchronizationContext)"/> is called
    /// and remains <c>true</c> until <see cref="Stop"/> is invoked or the engine is disposed.
    /// </remarks>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the engine is globally paused via <see cref="Pause"/>.
    /// </summary>
    /// <value><c>true</c> from <see cref="Pause"/> until <see cref="Resume"/>; otherwise <c>false</c>.</value>
    /// <remarks>
    /// Paused is orthogonal to <see cref="IsRunning"/>: a paused engine is still running, just
    /// parked. Pausing before the engine starts is valid — the loop then starts parked.
    /// </remarks>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Gets the frame the viewer was seeing at the moment the global engine pause took
    /// effect — captured by <see cref="Pause"/> before the <see cref="Paused"/> event is
    /// raised, so both the hosting application and a pause handler can use it (for example,
    /// to display a dimmed copy as a pause screen).
    /// </summary>
    /// <value>
    /// The last pre-pause frame of the first capturable render surface (scene-pipeline
    /// backbuffer or framebuffer presenter), or <c>null</c> before the first pause or when
    /// nothing had been rendered. With multiple surfaces, each
    /// <see cref="RenderSurfaceHostBase.LastFrameBeforePause"/> (and each
    /// <see cref="PixelFramePresenter.LastFrameBeforePause"/>) holds its own capture.
    /// </value>
    /// <remarks>
    /// The image is owned by the engine and remains valid through the resume, until the next
    /// <see cref="Pause"/> capture replaces it; copy it to keep it longer.
    /// GL-thread-rendered (GPU) surfaces are not captured.
    /// </remarks>
    public SKImage? LastFrameBeforePause { get; private set; }

    /// <summary>
    /// Returns <see cref="LastFrameBeforePause"/> as a raw RGBA8888 bitmap — a Skia-free
    /// shape for hosting applications: 4 bytes per pixel in R,G,B,A memory order, row-major,
    /// unpremultiplied alpha, <c>width * height * 4</c> bytes total.
    /// </summary>
    /// <remarks>
    /// This layout loads directly into imaging libraries — for example, saving the pause
    /// screenshot as a PNG with CodeBrix.Imaging takes no translation code:
    /// <c>Image.LoadPixelData&lt;Rgba32&gt;(bytes, width, height)</c> followed by
    /// <c>SaveAsPng(...)</c>. Each call converts and copies afresh; hold the result rather
    /// than re-calling per frame.
    /// </remarks>
    /// <param name="width">The bitmap width in pixels; 0 when the result is <c>null</c>.</param>
    /// <param name="height">The bitmap height in pixels; 0 when the result is <c>null</c>.</param>
    /// <returns>
    /// The RGBA8888 pixel bytes of the last pre-pause frame, or <c>null</c> when no frame
    /// has been captured (see <see cref="LastFrameBeforePause"/>).
    /// </returns>
    public byte[]? LastFrameBeforePauseAsRgba(out int width, out int height)
        => RgbaPixelExport.FromImage(LastFrameBeforePause, out width, out height);

    /// <summary>
    /// Gets the total number of high-resolution timer ticks that have elapsed since the engine started,
    /// excluding time spent globally paused via <see cref="Pause"/>.
    /// </summary>
    /// <value>The elapsed active ticks as measured by <see cref="HighResTimer"/>.</value>
    /// <remarks>
    /// This value represents elapsed time in the native resolution of the high-resolution timer.
    /// While paused, the value holds at the moment the pause began. Use
    /// <see cref="TotalSecondsEngineRunning"/> for a time value in seconds.
    /// </remarks>
    public long TotalTicksEngineRunning => (_isPaused ? _pauseStartTick : HighResTimer.GetCurrentTick()) - _startTick;

    /// <summary>
    /// Gets the total number of seconds that have elapsed since the engine started, excluding
    /// time spent globally paused via <see cref="Pause"/>.
    /// </summary>
    /// <value>The elapsed active time in seconds as a floating-point value.</value>
    /// <remarks>
    /// This value is derived from <see cref="TotalTicksEngineRunning"/> and provides
    /// a convenient measure of total runtime duration.
    /// </remarks>
    public double TotalSecondsEngineRunning => TotalTicksEngineRunning / (double)HighResTimer.TicksPerSecond;

    /// <summary>
    /// Gets the current gross cycles per second rate.
    /// </summary>
    /// <value>The number of complete engine cycles executed per second, including throttled cycles.</value>
    /// <remarks>
    /// <para>
    /// This metric reflects all calls to <see cref="Cycle"/>, regardless of whether
    /// a foreground render was performed. It represents the engine's update frequency
    /// for background tasks such as input polling, timers, and animations.
    /// </para>
    /// <para>
    /// This value is updated at the interval specified by <see cref="EngineConfiguration.SamplingTimeForCPS"/>.
    /// </para>
    /// </remarks>
    public double CyclesPerSecond => _grossCPS;

    /// <summary>
    /// Gets the current net frames per second rate.
    /// </summary>
    /// <value>The number of complete render frames presented per second.</value>
    /// <remarks>
    /// <para>
    /// This metric reflects only cycles that resulted in a foreground render operation,
    /// as controlled by <see cref="EngineConfiguration.TargetFPS"/>. It represents the
    /// actual visual frame rate delivered to the user.
    /// </para>
    /// <para>
    /// This value is updated at the interval specified by <see cref="EngineConfiguration.SamplingTimeForCPS"/>.
    /// </para>
    /// </remarks>
    public double FramesPerSecond => _netFPS;

    /// <summary>
    /// Gets the engine's persistent state container for storing arbitrary key-value data.
    /// </summary>
    /// <value>An <see cref="EngineState"/> instance that persists across engine sessions.</value>
    /// <remarks>
    /// <para>
    /// The <see cref="State"/> container provides a convenient mechanism for storing
    /// game-specific configuration, player progress, or other persistent data that should
    /// survive between application runs.
    /// </para>
    /// <para>
    /// State can be loaded from and saved to disk using the methods provided by
    /// the <see cref="EngineState"/> class.
    /// </para>
    /// </remarks>
    public EngineState State { get; } = new EngineState();

    private EngineConfiguration? _config = new();

    /// <summary>
    /// Gets the current engine configuration settings.
    /// </summary>
    /// <value>An <see cref="EngineConfiguration"/> instance containing all engine settings.</value>
    /// <remarks>
    /// <para>
    /// This property provides thread-safe access to the engine's configuration,
    /// which is loaded during <see cref="Initialize"/> from a configuration file
    /// or default values.
    /// </para>
    /// <para>
    /// Configuration settings control behavior such as target frame rate, logging mode,
    /// sampling intervals, and other core engine parameters.
    /// </para>
    /// </remarks>
    public EngineConfiguration Configuration
    {
        get => Volatile.Read(ref _config!);
        private set => Volatile.Write(ref _config, value);
    }

    /// <summary>
    /// Gets the collection of resource managers for audio, SVG, sprites, fonts, tilesheets, and direct drawing.
    /// </summary>
    /// <value>An <see cref="EngineManagers"/> instance providing access to all engine resource managers.</value>
    /// <remarks>
    /// <para>
    /// This property provides centralized access to subsystem managers responsible for loading
    /// and managing various types of game resources. Use the nested properties to access
    /// specific managers such as <see cref="EngineManagers.AudioResources"/>, 
    /// <see cref="EngineManagers.SvgResources"/>, <see cref="EngineManagers.Sprites"/>,
    /// <see cref="EngineManagers.Fonts"/>, <see cref="EngineManagers.Tilesheets"/>,
    /// and <see cref="EngineManagers.DirectDrawings"/>.
    /// </para>
    /// </remarks>
    public EngineManagers Managers { get; } = new();

    /// <summary>
    /// Gets the collection of input subsystems for keyboard, mouse, touch, and gamepad input.
    /// </summary>
    /// <value>An <see cref="EngineInputSystems"/> instance providing access to all input subsystems.</value>
    /// <remarks>
    /// <para>
    /// This property provides centralized access to input event pollers and managers
    /// for keyboard, mouse, touch, and gamepad devices. Use the nested properties to access
    /// specific subsystems such as <see cref="EngineInputSystems.KeyboardEventPoller"/>,
    /// <see cref="EngineInputSystems.MouseEventPoller"/>,
    /// <see cref="EngineInputSystems.TouchEventPoller"/>,
    /// <see cref="EngineInputSystems.GamepadEventPoller"/>, and
    /// <see cref="EngineInputSystems.GamepadManager"/>.
    /// </para>
    /// <para>
    /// Input adapters must be provided during <see cref="Initialize"/> for the
    /// corresponding input subsystems to become available.
    /// </para>
    /// </remarks>
    public EngineInputSystems Input { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the engine has been disposed.
    /// </summary>
    /// <value><c>true</c> if <see cref="Dispose"/> has completed; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// Once this property is <c>true</c>, the engine instance should not be used further.
    /// All managed resources have been released and subsystems have been shut down.
    /// </remarks>
    public bool IsDisposed { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether the engine is currently executing its disposal sequence.
    /// </summary>
    /// <value><c>true</c> if disposal is in progress; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// This property is set to <c>true</c> at the start of <see cref="Dispose"/> and can be
    /// used by subsystems to detect when cleanup is underway.
    /// </remarks>
    public bool IsDisposing { get; private set; }

    #endregion public properties

    #region private methods

    /// <summary>
    /// Handles an unhandled exception raised from within an engine cycle so a transient fault does
    /// not permanently stop the engine loop. The exception is logged (throttled to at most once per
    /// second, with a count of any suppressed in between) and the loop continues on the next cycle.
    /// </summary>
    /// <param name="ex">The exception thrown by <see cref="Cycle"/>.</param>
    private void HandleCycleException(Exception ex)
    {
        _suppressedCycleExceptionCount++;

        long now = HighResTimer.GetCurrentTick();
        if (_lastCycleExceptionLogTick != 0 && HighResTimer.GetDuration(_lastCycleExceptionLogTick, now) < 1f)
            return;

        int suppressed = _suppressedCycleExceptionCount - 1;
        _lastCycleExceptionLogTick = now;
        _suppressedCycleExceptionCount = 0;

        if (suppressed > 0)
            Logger.LogError(ex, "Unhandled exception in engine cycle; engine continued ({SuppressedCount} further occurrences suppressed in the last second).", suppressed);
        else
            Logger.LogError(ex, "Unhandled exception in engine cycle; engine continued.");
    }

    private void Cycle()
    {
        _inCycle = true;
        try
        {
            CycleCore();
        }
        finally
        {
            _inCycle = false;
        }
    }

    private void CycleCore()
    {
        EngineDispatcher.Drain();

        long tick = HighResTimer.GetCurrentTick();
        var deltaMs = HighResTimer.GetDuration(_lastCycleTick, tick);
        _lastCycleTick = tick;

        EnginePluginRegistry.InvokePreCycle(this, deltaMs);

        DoBackgroundTasks(tick);

        // if TargetFPS <= 0, render to screen unbounded
        // otherwise, check if throttle time has passed since last tick...
        if ((Configuration.TargetFPS <= 0)
            || (tick - _lastForegroundTick) >= HighResTimer.TicksPerSecond / Configuration.TargetFPS)
        {
            EnginePluginRegistry.InvokePreFrameRender(this, deltaMs);

            DoForegroundTasks(tick);

            EnginePluginRegistry.InvokePostFrameRender(this, deltaMs);

            // save time of this last tick; increment CPS counter
            _lastForegroundTick = tick;
            _netCyclesThisMeasure++;
        }

        // increment CPS counter
        _grossCyclesThisMeasure++;

        // if 0 or negative, sampling is turned off
        if (Configuration.SamplingTimeForCPS > 0)
            CalculateCPS(tick);

        EnginePluginRegistry.InvokePostCycle(this, deltaMs);
    }

    private void DoBackgroundTasks(long tick)
    {
        // find total real seconds passed since last background loop
        var deltaSeconds = HighResTimer.GetDuration(_lastBackgroundTick, tick);

        BeforeBackgroundTasksExecute?.Invoke();

        // raise pre-cycle timer events
        Timer.RaiseTimerEvents(TimerType.PreCycle, tick);

        // check for keyboard events
        KeyboardEventPoller.Instance?.PollForEvents(tick);

        // check for mouse events
        MouseEventPoller.Instance?.PollForEvents(tick);

        // check for touch events
        TouchEventPoller.Instance?.PollForEvents(tick);

        // check for gamepad events
        GamepadEventPoller.Instance?.PollForEvents(tick);

        // cycle Animator frames
        for (int i = 0; i < Tile.TilesAnimating.Count; i++)
            Tile.TilesAnimating[i].TileAnimator.CycleAnimation(tick);

        // advance Sprite Movement paths
        SpriteManager.Instance.MoveSprites(tick);

        // resolve collisions after movement
        foreach (var scene in Scenes.Scene.GetAllScenes())
        {
            foreach (var layer in scene.SceneLayers)
                layer.CollisionResolver.Resolve();
        }

        // update cameras so any movement can mark RefreshNeeded = All.
        foreach (var surface in RenderSurfaceHostRegistry.All)
            surface.ViewManager.UpdateCameras(deltaSeconds);

        AfterBackgroundTasksExecute?.Invoke();

        _lastBackgroundTick = tick;
    }

    private void DoForegroundTasks(long tick)
    {
        // raise event
        BeforeFrameRender?.Invoke();

        // update the DirectDrawing instances' states
        DirectDrawingManager.Instance.UpdateAll(tick);

        // refresh all RenderSurfaceHost backbuffers (skip surfaces rendered on the GL thread)
        foreach (var surface in RenderSurfaceHostRegistry.All)
            if (!surface.Backbuffer.IsGlThreadRendered)
                surface.RenderToBackbuffer(tick);

        // render each Backbuffer to RenderSurfaceHost adapter (skip GL-thread surfaces)
        foreach (var surface in RenderSurfaceHostRegistry.All)
            if (!surface.Backbuffer.IsGlThreadRendered)
                surface.PresentBackbufferToAdapter();

        // update state of gamepad(s)
        Input.GamepadManager?.Update();

        // raise event
        AfterFrameRender?.Invoke();

        // raise post-cycle timer events
        Timer.RaiseTimerEvents(TimerType.PostCycle, tick);
    }

    private void CalculateCPS(long tick)
    {
        // Has the sampling interval elapsed?
        long elapsedTicks = tick - _lastCPSSamplingTick;
        if (elapsedTicks < Configuration.SamplingTimeForCPSTicks)
            return;

        // SNAPSHOT the counters BEFORE resetting or posting
        long grossCycles = _grossCyclesThisMeasure;
        long netCycles = _netCyclesThisMeasure;

        // Compute using the snapshot
        double elapsedSec = elapsedTicks / (double)HighResTimer.TicksPerSecond;
        double grossCps = grossCycles * HighResTimer.TicksPerSecond / (double)elapsedTicks;
        double netCps = netCycles * HighResTimer.TicksPerSecond / (double)elapsedTicks;

        // Collect actual GPU FPS from all registered GPU-rendered backbuffers.
        long totalGpuFrames = 0;
        int gpuSurfaceCount = 0;
        foreach (var surface in RenderSurfaceHostRegistry.All.ToArray())
        {
            if (surface.Backbuffer is GpuBackbuffer gpuBb)
            {
                totalGpuFrames += gpuBb.ConsumeFrameCount();
                gpuSurfaceCount++;
            }
        }
        double? gpuFps = gpuSurfaceCount > 0
            ? totalGpuFrames * HighResTimer.TicksPerSecond / (double)elapsedTicks
            : null;

        // Build immutable args NOW (so lambda doesn't read changing fields later)
        var args = new CyclesPerSecondCalculatedEventArgs(
            grossCycles,
            netCycles,
            grossCps,
            netCps,
            elapsedSec,
            gpuFps
        );

        // Post the snapshot
        UiDispatcher!.Post(() => CPSCalculated?.Invoke(args));

        _grossCPS = grossCps;
        _netFPS = netCps;

        // Reset for next window
        _lastCPSSamplingTick = tick;
        _grossCyclesThisMeasure = 0;
        _netCyclesThisMeasure = 0;
    }

    #endregion private methods

    #region IDisposable support

    private void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
                IsDisposing = true;

                // stop the loop first so handlers don't race the cycle thread
                try
                {
                    Stop();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unhandled exception calling Stop()");
                }

                // wait for the background loop to actually exit
                try
                {
                    _cycleTask?.Wait();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error waiting for engine loop to exit.");
                }

                // raise Disposing on UI thread if possible; otherwise inline
                if (UiDispatcher is not null)
                    UiDispatcher.Post(() => SafeInvoke(Disposing));
                else
                    SafeInvoke(Disposing);

                // managed cleanup...
                Input.KeyboardEventPoller?.StopMonitoringAllKeys();
                Input.MouseEventPoller?.StopMonitoringMouse();
                Input.TouchEventPoller?.StopMonitoringTouch();
                (Input.TouchEventPoller?.Adapter as IDisposable)?.Dispose();

                if (Input.GamepadManager is not null)
                    foreach (var gamepadAdapter in Input.GamepadManager.ConnectedAdapters)
                        Input.GamepadEventPoller?.StopMonitoringAllButtons(gamepadAdapter.GamepadId);

                if (Configuration.LoggingMode == EngineLoggingMode.Asynchronous && Configuration.FlushAsyncLogsOnShutdown)
                    EngineLogger.StopAsyncLogging(flush: true);

                Timer.ClearAll();
                State.Clear();

                // The pause snapshots are owned by their surfaces/presenters; just drop the
                // engine's reference.
                LastFrameBeforePause = null;

                lock (_enginePausableLoops)
                {
                    _enginePausableLoops.Clear();
                }
            }

            // unmanaged cleanup...
            IsDisposed = true;

            if (disposing)
            {
                // now signal we're fully torn down
                if (UiDispatcher is not null)
                    UiDispatcher.Post(() => SafeInvoke(Disposed));
                else
                    SafeInvoke(Disposed);
            }
        }
    }

    private static void SafeInvoke(Action? evnt)
    {
        try { evnt?.Invoke(); }
        catch (Exception ex)
        {
            // Keep disposal robust; log and continue
            Logger.LogError(ex, "Unhandled exception in disposal event handler.");
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="Engine"/> and stops all subsystems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method performs an orderly shutdown of the engine, including:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Stopping the main engine loop</description></item>
    ///   <item><description>Waiting for the background thread to exit</description></item>
    ///   <item><description>Raising the <see cref="Disposing"/> event</description></item>
    ///   <item><description>Cleaning up input subsystems</description></item>
    ///   <item><description>Flushing asynchronous logs if configured</description></item>
    ///   <item><description>Clearing timers and state</description></item>
    ///   <item><description>Raising the <see cref="Disposed"/> event</description></item>
    /// </list>
    /// <para>
    /// After disposal, the engine instance should not be used. To restart the engine,
    /// a new application session is required.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~Engine()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    #endregion IDisposable support
}
