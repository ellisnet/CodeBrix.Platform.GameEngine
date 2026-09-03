using System;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Logging;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Timers;
using Microsoft.Extensions.Logging;
using SkiaSharp;

using EngineTimer = CodeBrix.Platform.GameEngine.Timers.Timer;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct; //CodeBrix (not from Gondwana)

/// <summary>
/// A self-running, view-sized splash overlay: it fades a single image in, holds it on screen while
/// optional start-up work runs, fades it out, disposes itself, and only then reports completion.
/// </summary>
/// <remarks>
/// <para>
/// The overlay is a <see cref="DirectComposite"/> in <see cref="DirectDrawingMode.View"/> holding one
/// <see cref="DirectImage"/> that covers the whole viewport (<see cref="DirectImage.ScaleMode.Fit"/>,
/// <see cref="DirectDrawingBase.ZOrder"/> of <see cref="int.MaxValue"/> so it draws above everything else).
/// It advances through <see cref="SplashPhase.FadingIn"/>, <see cref="SplashPhase.Holding"/>,
/// <see cref="SplashPhase.FadingOut"/> and finally <see cref="SplashPhase.Completed"/>.
/// </para>
/// <para>
/// The hold phase ends when BOTH the configured hold duration has elapsed AND any supplied hold work
/// has finished. Supplying work that runs longer than the hold duration therefore keeps the splash on
/// screen until that work completes, which is the usual way to hide asset loading behind the splash.
/// </para>
/// <para>
/// Callback threading: the hold callbacks and <c>onSplashCompleted</c> are raised through
/// <see cref="Engine.EngineDispatcher"/>, so they run on the engine thread. <c>onSplashCompleted</c>
/// is raised AFTER the fade-out has finished and after the overlay has disposed itself, which makes it
/// the natural place to start the game's real visuals and music.
/// </para>
/// <para>
/// Pause safety: the hold delay uses an engine <see cref="EngineTimer"/> and the fades use the standard
/// direct-drawing update, both of which have their time baselines shifted when the engine resumes from
/// a pause. A splash therefore does not "jump" to the end when the game is paused and resumed.
/// </para>
/// <para>
/// Exceptions thrown by any supplied callback are logged and swallowed; they never strand the splash on
/// screen.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var imageStream = File.OpenRead(splashImagePath);
///
/// var view = host.ViewManager.Views[0];
/// var splash = SplashOverlay.TryCreate(imageStream, host, view, onSplashCompleted: BeginPostSplashStartup);
///
/// if (splash is null)
///     BeginPostSplashStartup();   // no splash image - start straight away
/// </code>
/// </example>
public sealed class SplashOverlay : DirectComposite
{
    /// <summary>
    /// The stages a <see cref="SplashOverlay"/> moves through, in order.
    /// </summary>
    public enum SplashPhase
    {
        /// <summary>The overlay exists but its fade-in has not started yet.</summary>
        Hidden,

        /// <summary>The image is fading up from fully transparent to fully opaque.</summary>
        FadingIn,

        /// <summary>The image is fully opaque; the hold delay and any hold work are running.</summary>
        Holding,

        /// <summary>The image is fading down to fully transparent.</summary>
        FadingOut,

        /// <summary>The fade-out has finished and the overlay has disposed itself.</summary>
        Completed
    }

    /// <summary>The nickname prefix used when the caller does not supply one.</summary>
    public const string DefaultNicknamePrefix = "splash-overlay";

    private static ILogger<SplashOverlay> Logger => EngineLogger.GetLogger<SplashOverlay>();

    private readonly Action? _onHolding;
    private readonly Func<Task>? _onHoldingAsync;
    private readonly Viewport _viewport;

    private Action? _onSplashCompleted;
    private EngineTimer? _holdTimer;
    private bool _holdDurationElapsed;
    private bool _holdWorkCompleted;
    private bool _disposed;

    #region Construction

    private SplashOverlay(DirectImage image,
                          float fadeInSeconds,
                          float holdSeconds,
                          float fadeOutSeconds,
                          Action? onHolding,
                          Func<Task>? onHoldingAsync,
                          Action? onSplashCompleted,
                          string nickname)
        : base(image.RenderSurfaceHost, DirectDrawingMode.View, PointF.Empty, nickname)
    {
        Image = image;
        FadeInSeconds = fadeInSeconds;
        HoldSeconds = holdSeconds;
        FadeOutSeconds = fadeOutSeconds;

        _onHolding = onHolding;
        _onHoldingAsync = onHoldingAsync;
        _onSplashCompleted = onSplashCompleted;

        Phase = SplashPhase.Hidden;

        Add(image, keepCurrentOffset: false, explicitLocalOffsetPx: Vector2.Zero);

        _viewport = image.View!.Viewport;
        _viewport.TargetRectChanged += OnViewportTargetRectChanged;

        BeginFadeIn();
    }

    /// <summary>
    /// Attempts to create and start a splash overlay from an image file on disk.
    /// </summary>
    /// <param name="imagePath">The full path of the splash image file.</param>
    /// <param name="host">The render surface host that owns the view.</param>
    /// <param name="view">The view the splash covers.</param>
    /// <param name="fadeInSeconds">How long the fade-in takes, in seconds. Must not be negative.</param>
    /// <param name="holdSeconds">
    /// The minimum time the fully opaque image stays on screen, in seconds. Must not be negative;
    /// zero means the fade-out may begin as soon as any hold work has finished.
    /// </param>
    /// <param name="fadeOutSeconds">How long the fade-out takes, in seconds. Must not be negative.</param>
    /// <param name="onHolding">Optional synchronous work run on the engine thread when the hold phase starts.</param>
    /// <param name="onHoldingAsync">Optional asynchronous work started on the engine thread when the hold phase starts.</param>
    /// <param name="onSplashCompleted">Optional callback raised on the engine thread after the fade-out completes.</param>
    /// <param name="nickname">An optional diagnostic nickname for the overlay and its image.</param>
    /// <returns>
    /// The started overlay, or <see langword="null"/> when the file is missing or cannot be decoded as an
    /// image (a warning is logged and the caller should continue without a splash).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration is negative.</exception>
    public static SplashOverlay? TryCreate(string imagePath,
                                           RenderSurfaceHostBase host,
                                           View view,
                                           float fadeInSeconds = 0.45f,
                                           float holdSeconds = 3f,
                                           float fadeOutSeconds = 0.45f,
                                           Action? onHolding = null,
                                           Func<Task>? onHoldingAsync = null,
                                           Action? onSplashCompleted = null,
                                           string? nickname = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            Logger.LogWarning("No splash overlay was created: the image file '{ImagePath}' does not exist.", imagePath);
            return null;
        }

        try
        {
            using FileStream imageStream = File.OpenRead(imagePath);

            return TryCreate(imageStream,
                             host,
                             view,
                             fadeInSeconds,
                             holdSeconds,
                             fadeOutSeconds,
                             onHolding,
                             onHoldingAsync,
                             onSplashCompleted,
                             nickname);
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "No splash overlay was created: the image file '{ImagePath}' could not be read.", imagePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "No splash overlay was created: the image file '{ImagePath}' could not be read.", imagePath);
            return null;
        }
    }

    /// <summary>
    /// Attempts to create and start a splash overlay from an image stream.
    /// </summary>
    /// <param name="imageStream">
    /// A stream positioned at the start of an encoded image. The stream is only read during this call;
    /// the caller keeps ownership of it.
    /// </param>
    /// <param name="host">The render surface host that owns the view.</param>
    /// <param name="view">The view the splash covers.</param>
    /// <param name="fadeInSeconds">How long the fade-in takes, in seconds. Must not be negative.</param>
    /// <param name="holdSeconds">
    /// The minimum time the fully opaque image stays on screen, in seconds. Must not be negative;
    /// zero means the fade-out may begin as soon as any hold work has finished.
    /// </param>
    /// <param name="fadeOutSeconds">How long the fade-out takes, in seconds. Must not be negative.</param>
    /// <param name="onHolding">Optional synchronous work run on the engine thread when the hold phase starts.</param>
    /// <param name="onHoldingAsync">Optional asynchronous work started on the engine thread when the hold phase starts.</param>
    /// <param name="onSplashCompleted">Optional callback raised on the engine thread after the fade-out completes.</param>
    /// <param name="nickname">An optional diagnostic nickname for the overlay and its image.</param>
    /// <returns>
    /// The started overlay, or <see langword="null"/> when the host has no views or the stream does not
    /// decode to an image (a warning is logged and the caller should continue without a splash).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="imageStream"/>, <paramref name="host"/> or <paramref name="view"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A duration is negative.</exception>
    public static SplashOverlay? TryCreate(Stream imageStream,
                                           RenderSurfaceHostBase host,
                                           View view,
                                           float fadeInSeconds = 0.45f,
                                           float holdSeconds = 3f,
                                           float fadeOutSeconds = 0.45f,
                                           Action? onHolding = null,
                                           Func<Task>? onHoldingAsync = null,
                                           Action? onSplashCompleted = null,
                                           string? nickname = null)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);

        ArgumentOutOfRangeException.ThrowIfNegative(fadeInSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(holdSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(fadeOutSeconds);

        if (host.ViewManager.Views.Count == 0)
        {
            Logger.LogWarning("No splash overlay was created: the render surface host has no views.");
            return null;
        }

        SKImage? sourceImage = DecodeImage(imageStream);

        if (sourceImage is null)
            return null;

        string overlayNickname = string.IsNullOrWhiteSpace(nickname)
            ? $"{DefaultNicknamePrefix}-{Guid.NewGuid():N}"
            : nickname!;

        Rectangle viewport = view.Viewport.TargetRectPx;
        var screenBounds = new Rectangle(0, 0, viewport.Width, viewport.Height);

        DirectImage image;

        try
        {
            image = new DirectImage(sourceImage, host, view, screenBounds, $"{overlayNickname}-image")
                .SetScaleMode(DirectImage.ScaleMode.Fit);
        }
        catch
        {
            sourceImage.Dispose();
            throw;
        }

        // The image does not own the decoded bitmap, so the overlay releases it with the image.
        image.Disposing += (_, _) => sourceImage.Dispose();

        image.ZOrder = int.MaxValue;
        image.Opacity = 0f;

        return new SplashOverlay(image,
                                 fadeInSeconds,
                                 holdSeconds,
                                 fadeOutSeconds,
                                 onHolding,
                                 onHoldingAsync,
                                 onSplashCompleted,
                                 overlayNickname);
    }

    private static SKImage? DecodeImage(Stream imageStream)
    {
        try
        {
            using SKBitmap? bitmap = SKBitmap.Decode(imageStream);

            if (bitmap is null)
            {
                Logger.LogWarning("No splash overlay was created: the supplied stream could not be decoded as an image.");
                return null;
            }

            SKImage? image = SKImage.FromBitmap(bitmap);

            if (image is null)
                Logger.LogWarning("No splash overlay was created: the decoded splash image could not be prepared for drawing.");

            return image;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "No splash overlay was created: the supplied stream could not be decoded as an image.");
            return null;
        }
    }

    #endregion Construction

    #region Properties

    /// <summary>
    /// Gets the image drawn by this overlay. It is disposed together with the overlay.
    /// </summary>
    public DirectImage Image { get; }

    /// <summary>
    /// Gets the stage the splash sequence has reached.
    /// </summary>
    public SplashPhase Phase { get; private set; }

    /// <summary>
    /// Gets the configured fade-in duration in seconds.
    /// </summary>
    public float FadeInSeconds { get; }

    /// <summary>
    /// Gets the configured minimum hold duration in seconds.
    /// </summary>
    public float HoldSeconds { get; }

    /// <summary>
    /// Gets the configured fade-out duration in seconds.
    /// </summary>
    public float FadeOutSeconds { get; }

    #endregion Properties

    #region Sequence

    private void BeginFadeIn()
    {
        Phase = SplashPhase.FadingIn;

        Image.FadeToCompleted += OnFadeInCompleted;
        Image.FadeIn(FadeInSeconds);
    }

    private void OnFadeInCompleted(object? sender, DirectDrawingBase drawing)
    {
        Image.FadeToCompleted -= OnFadeInCompleted;

        if (_disposed)
            return;

        Phase = SplashPhase.Holding;

        BeginHold();
    }

    private void BeginHold()
    {
        // A hold shorter than one high-resolution tick cannot be timed, so it counts as already elapsed.
        if (HoldSeconds * HighResTimer.TicksPerSecond >= 1d)
        {
            _holdTimer = EngineTimer.Add(TimerType.PostCycle, TimerCycles.Once, HoldSeconds);
            _holdTimer.Tick += OnHoldTimerElapsed;
        }
        else
        {
            _holdDurationElapsed = true;
        }

        // The engine dispatcher runs this inline when the caller is already the engine thread, which is
        // the normal case (the fade completes inside the engine's direct-drawing update).
        Engine.Instance.EngineDispatcher.Post(RunHoldWork);
    }

    private void RunHoldWork()
    {
        if (_disposed)
            return;

        if (_onHolding is not null)
        {
            try
            {
                _onHolding();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "A splash overlay hold callback threw an exception.");
            }
        }

        if (_onHoldingAsync is null)
        {
            CompleteHoldWork();
            return;
        }

        Task holdTask;

        try
        {
            holdTask = _onHoldingAsync() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "A splash overlay asynchronous hold callback threw an exception.");
            CompleteHoldWork();
            return;
        }

        if (holdTask.IsCompleted)
        {
            LogHoldTaskFailure(holdTask);
            CompleteHoldWork();
            return;
        }

        holdTask.ContinueWith(
            static (task, state) =>
            {
                var overlay = (SplashOverlay)state!;

                LogHoldTaskFailure(task);
                Engine.Instance.EngineDispatcher.Post(overlay.CompleteHoldWork);
            },
            this,
            TaskScheduler.Default);
    }

    private static void LogHoldTaskFailure(Task holdTask)
    {
        if (holdTask.IsFaulted && holdTask.Exception is not null)
            Logger.LogError(holdTask.Exception, "A splash overlay asynchronous hold callback failed.");
    }

    private void CompleteHoldWork()
    {
        if (_holdWorkCompleted)
            return;

        _holdWorkCompleted = true;

        TryBeginFadeOut();
    }

    private void OnHoldTimerElapsed()
    {
        _holdDurationElapsed = true;

        DisposeHoldTimer();
        TryBeginFadeOut();
    }

    private void TryBeginFadeOut()
    {
        if (_disposed || Phase != SplashPhase.Holding)
            return;

        if (!_holdDurationElapsed || !_holdWorkCompleted)
            return;

        Phase = SplashPhase.FadingOut;

        Image.FadeToCompleted += OnFadeOutCompleted;
        Image.FadeOut(FadeOutSeconds);
    }

    private void OnFadeOutCompleted(object? sender, DirectDrawingBase drawing)
    {
        Image.FadeToCompleted -= OnFadeOutCompleted;

        if (_disposed)
            return;

        Phase = SplashPhase.Completed;

        Action? completed = _onSplashCompleted;
        _onSplashCompleted = null;

        Dispose();

        if (completed is null)
            return;

        Engine.Instance.EngineDispatcher.Post(() =>
        {
            try
            {
                completed();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "A splash overlay completion callback threw an exception.");
            }
        });
    }

    private void OnViewportTargetRectChanged(ViewportResizedEventArgs args)
    {
        if (_disposed)
            return;

        // The splash always covers the whole viewport, so a resize re-stretches it in place.
        Image.ScreenBounds = new Rectangle(0, 0, args.NewRect.Width, args.NewRect.Height);
    }

    private void DisposeHoldTimer()
    {
        if (_holdTimer is null)
            return;

        _holdTimer.Tick -= OnHoldTimerElapsed;
        _holdTimer.Dispose();
        _holdTimer = null;
    }

    #endregion Sequence

    #region Disposal

    /// <summary>
    /// Stops the splash sequence, releases the hold timer and the decoded image, and disposes the overlay.
    /// </summary>
    /// <remarks>
    /// Disposing the overlay before the fade-out has finished cancels the sequence: the
    /// <c>onSplashCompleted</c> callback supplied to <see cref="TryCreate(Stream, RenderSurfaceHostBase, View, float, float, float, Action, Func{Task}, Action, string)"/>
    /// is NOT raised. Calling this method more than once is safe.
    /// </remarks>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _viewport.TargetRectChanged -= OnViewportTargetRectChanged;

        Image.FadeToCompleted -= OnFadeInCompleted;
        Image.FadeToCompleted -= OnFadeOutCompleted;

        DisposeHoldTimer();

        base.Dispose();
    }

    #endregion Disposal
}
