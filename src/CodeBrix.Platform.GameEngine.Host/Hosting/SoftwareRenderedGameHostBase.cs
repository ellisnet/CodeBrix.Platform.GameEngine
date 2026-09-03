using System;
using CodeBrix.Platform.GameEngine.Host.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Host.Input.Mouse;
using CodeBrix.Platform.GameEngine.Host.Input.Touch;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Input.Touch;
using CodeBrix.Platform.GameEngine.Logging;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Timers;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// The host base class for software-rendered (framebuffer-style) games — games that own their
/// game loop, render whole CPU frames at a fixed tic rate (35 Hz, 70 Hz, ...), and never use
/// the engine's scene/sprite pipeline. It wires a <see cref="GameSurfaceCanvas"/> in presenter
/// mode, the input adapters + <see cref="InputPump"/>, and a <see cref="FixedRateGameLoop"/>;
/// the game supplies logic through the <see cref="OnLoadContent"/>, <see cref="OnTic"/>,
/// <see cref="OnRenderFrame"/>, and <see cref="OnShutdown"/> overrides and owns everything
/// that happens inside a tic.
/// </summary>
/// <remarks>
/// <para>
/// Per tic, on the dedicated game-loop thread: <see cref="InputPump.PollNow"/> →
/// <see cref="OnTic"/> → <see cref="OnRenderFrame"/> into the reusable frame buffer → the
/// frame is presented (latest-frame-wins) to the canvas.
/// </para>
/// <para>
/// Audio stays opt-in: override <see cref="ConfigureAudio"/> and call
/// <see cref="Audio.AudioSystem.Initialize"/> there when the game uses
/// <see cref="Audio.SoundChannel"/>/<see cref="Audio.StreamingAudioSource"/> voices.
/// Gamepads are opt-in the same way, through <see cref="ConfigureGamepads"/>.
/// </para>
/// <para>
/// This is a sibling of <see cref="GameHostBase"/>, not a subclass: the engine cycle never
/// runs, so none of the scene-graph lifecycle applies.
/// </para>
/// </remarks>
public abstract class SoftwareRenderedGameHostBase : IDisposable
{
    private CodeBrixKeyboardAdapter? _keyboardAdapter;
    private byte[] _frameBuffer = [];
    private bool _initialized;
    private bool _isDisposed;
    private Action? _enginePausedHandler;
    private Action? _engineResumedHandler;

    /// <summary>
    /// Creates the host for the given canvas and tic rate. Call <see cref="Initialize"/>
    /// (typically from the canvas's <see cref="GameSurfaceCanvas.FirstStarted"/> handler) to
    /// start the game.
    /// </summary>
    /// <param name="renderSurface">The canvas the game presents to.</param>
    /// <param name="ticsPerSecond">The fixed tic rate of the game loop in Hz (e.g. 35 or 70).</param>
    protected SoftwareRenderedGameHostBase(GameSurfaceCanvas renderSurface, int ticsPerSecond)
    {
        RenderSurface = renderSurface ?? throw new ArgumentNullException(nameof(renderSurface));
        Presenter = renderSurface.UsePixelFramePresenter();
        GameLoop = new FixedRateGameLoop(ticsPerSecond, Tic)
        {
            // The host's loop honors the global engine pause: Engine.Pause() parks it after
            // the tic in progress and Engine.Resume() wakes it burst-free.
            PauseWithEngine = true,
        };
        GameLoop.UnhandledException += OnGameLoopException;
    }

    /// <summary>The canvas the game presents to.</summary>
    public GameSurfaceCanvas RenderSurface { get; }

    /// <summary>
    /// The presenter for this host's canvas. <see cref="OnLoadContent"/> must configure it
    /// (<see cref="PixelFramePresenter.Configure"/>) before the game loop starts.
    /// </summary>
    public PixelFramePresenter Presenter { get; }

    /// <summary>The fixed-rate loop that paces the game (owned by this host).</summary>
    public FixedRateGameLoop GameLoop { get; }

    /// <summary>
    /// Initializes the host and starts the game loop: logging → input adapters → gamepads
    /// (opt-in) → audio (opt-in) → <see cref="OnLoadContent"/> → loop start. Call once, on the
    /// UI thread, after the canvas has its first real size.
    /// </summary>
    /// <param name="logLevel">The minimum engine log level. Default Warning.</param>
    public void Initialize(LogLevel logLevel = LogLevel.Warning)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_initialized)
        {
            throw new InvalidOperationException("The host is already initialized.");
        }

        EngineLogger.SetLogLevel(logLevel);

        ConfigureInput();
        ConfigureGamepads();
        ConfigureAudio();

        OnLoadContent();

        if (!Presenter.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{nameof(OnLoadContent)} must configure the presenter ({nameof(Presenter)}.{nameof(PixelFramePresenter.Configure)}) so the host knows the game's frame size.");
        }

        _frameBuffer = new byte[Presenter.FrameWidth * Presenter.FrameHeight * 4];

        // Hook the global pause before the loop starts, so a pause that lands during
        // startup still reaches the game's overrides.
        _enginePausedHandler = OnEnginePaused;
        _engineResumedHandler = OnEngineResumed;
        Engine.Instance.Paused += _enginePausedHandler;
        Engine.Instance.Resumed += _engineResumedHandler;

        GameLoop.Start();
        _initialized = true;
    }

    /// <summary>
    /// Called when the global engine pause (<see cref="Engine.Pause"/>) takes effect — the
    /// game's "do this when paused" hook (save the game, present a pause screen). By the time
    /// this runs the game loop is parked (no <see cref="OnTic"/>/<see cref="OnRenderFrame"/>
    /// in flight), and <see cref="Engine.LastFrameBeforePause"/> holds what the player was
    /// seeing — presenting a dimmed copy of it via <see cref="Presenter"/> works while
    /// parked. The base implementation does nothing.
    /// </summary>
    protected virtual void OnEnginePaused()
    {
    }

    /// <summary>
    /// Called when <see cref="Engine.Resume"/> lifts the global engine pause, before the game
    /// loop wakes — the place to tear down a pause screen. The base implementation does
    /// nothing.
    /// </summary>
    protected virtual void OnEngineResumed()
    {
    }

    /// <summary>Stops the game loop, calls <see cref="OnShutdown"/>, and releases the host's resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_enginePausedHandler is not null)
        {
            Engine.Instance.Paused -= _enginePausedHandler;
            _enginePausedHandler = null;
        }
        if (_engineResumedHandler is not null)
        {
            Engine.Instance.Resumed -= _engineResumedHandler;
            _engineResumedHandler = null;
        }

        GameLoop.Stop();
        GameLoop.Dispose();

        try
        {
            OnShutdown();
        }
        finally
        {
            _keyboardAdapter?.Dispose();
            _keyboardAdapter = null;
            Presenter.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wires the input adapters and pollers to the canvas. The default registers keyboard,
    /// mouse, and touch adapters and applies the canvas focus recipe
    /// (<see cref="GameSurfaceCanvas.EnsureFocus"/>); override to customize.
    /// </summary>
    protected virtual void ConfigureInput()
    {
        RenderSurface.EnsureFocus();

        _keyboardAdapter = new CodeBrixKeyboardAdapter(RenderSurface);
        KeyboardEventPoller.Initialize(_keyboardAdapter);
        MouseEventPoller.Initialize(new CodeBrixMouseAdapter(RenderSurface));
        TouchEventPoller.Initialize(new CodeBrixTouchInputAdapter(RenderSurface, EmulateMouseAsTouch));
    }

    /// <summary>
    /// Gets a value indicating whether primary mouse input should also be delivered as touch
    /// contact ID 0. The default is <c>false</c>, so a desktop click raises mouse events only.
    /// Override and return <c>true</c> for a game that drives everything from the touch stream.
    /// </summary>
    protected virtual bool EmulateMouseAsTouch => false;

    /// <summary>
    /// Gamepads are opt-in: override and attach a gamepad manager here when the game supports
    /// controllers. The default does nothing.
    /// </summary>
    /// <remarks>
    /// With the <c>CodeBrix.Platform.GameEngine.Sdl2</c> package referenced, that is one call:
    /// <code>
    /// protected override void ConfigureGamepads()
    ///     => _gamepads = Engine.Instance.InitializeSdlGamepadManager();
    /// </code>
    /// Nothing further is needed - the per-tic <see cref="InputPump.PollNow"/> this host already
    /// runs refreshes the pads and raises their events. This hook is the Mode-B counterpart to
    /// <c>CodeBrixGameHost.OnConfigureGamepads</c>, and runs after the input adapters are wired
    /// (so the pollers exist) and before <see cref="OnLoadContent"/> (so the game can read
    /// controller availability while loading).
    /// </remarks>
    protected virtual void ConfigureGamepads()
    {
    }

    /// <summary>
    /// Audio is opt-in: override and call <see cref="Audio.AudioSystem.Initialize"/> here
    /// when the game plays sound. The default does nothing.
    /// </summary>
    protected virtual void ConfigureAudio()
    {
    }

    /// <summary>
    /// Load the game's content and configure <see cref="Presenter"/> (required). Runs once,
    /// on the initializing (UI) thread, before the game loop starts.
    /// </summary>
    protected abstract void OnLoadContent();

    /// <summary>
    /// One tic of game logic. Runs on the game-loop thread at the fixed tic rate, after
    /// input has been pumped for this tic.
    /// </summary>
    protected abstract void OnTic();

    /// <summary>
    /// Render the current game state into <paramref name="frameBuffer"/> (the configured
    /// <c>width * height * 4</c> bytes, reused every tic). Runs on the game-loop thread right
    /// after <see cref="OnTic"/>; the buffer is presented when this returns.
    /// </summary>
    /// <param name="frameBuffer">The frame buffer to fill.</param>
    protected abstract void OnRenderFrame(Span<byte> frameBuffer);

    /// <summary>Runs once while the host is disposed, before the adapters are torn down.</summary>
    protected virtual void OnShutdown()
    {
    }

    private void Tic()
    {
        InputPump.PollNow();
        OnTic();
        OnRenderFrame(_frameBuffer);
        Presenter.PresentFrame(_frameBuffer);
    }

    private void OnGameLoopException(Exception exception)
        => Engine.Logger.LogError(exception, "The game loop stopped due to an unhandled exception in a tic.");
}
