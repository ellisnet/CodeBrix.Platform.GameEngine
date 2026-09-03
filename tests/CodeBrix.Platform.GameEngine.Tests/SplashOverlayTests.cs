using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

using EngineTimer = CodeBrix.Platform.GameEngine.Timers.Timer;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the port-native <see cref="SplashOverlay"/>: the fade-in / hold / fade-out sequence, the
/// engine-thread callbacks and their ordering, the hold phase waiting for both the engine timer and any
/// supplied work, self-disposal, and the "no image, no splash" fallback that lets a game start anyway.
/// </summary>
/// <remarks>
/// The engine dispatcher is bound to the test thread so posted callbacks run inline, exactly as they do
/// on a live engine thread. The assembly runs serially, so that global binding is safe here.
/// </remarks>
public class SplashOverlayTests : IDisposable
{
    private readonly List<IDisposable> _created = new();
    private long _tick;

    /// <summary>
    /// Binds the process-global engine dispatcher to this thread and clears the timer registry, so a
    /// splash created by a test behaves as it would inside a running engine cycle.
    /// </summary>
    public SplashOverlayTests()
    {
        Dispatcher.BindToCurrentThread();
        EngineTimer.ClearAll();
        EngineTimer.PausedAll = false;
    }

    /// <summary>Disposes everything this fixture registered with the process-global registries.</summary>
    public void Dispose()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            try
            {
                _created[i].Dispose();
            }
            catch (ObjectDisposedException)
            {
                // A test that disposed its own overlay is the normal path here.
            }
        }

        _created.Clear();
        EngineTimer.ClearAll();
        EngineTimer.PausedAll = false;
        GC.SuppressFinalize(this);
    }

    private static EngineDispatcher Dispatcher => (EngineDispatcher)Engine.Instance.EngineDispatcher;

    private TestRenderSurfaceHost NewHost(Rectangle viewBounds)
    {
        var host = new TestRenderSurfaceHost();
        _created.Add(host);
        host.ViewManager.AddView(viewBounds, zOrder: 0);
        return host;
    }

    private static View ViewOf(TestRenderSurfaceHost host, Rectangle viewBounds) =>
        host.ViewManager.Views.Single(view => view.Viewport.TargetRectPx == viewBounds);

    private static MemoryStream NewPngStream(SKColor color, int size = 8)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(color);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(data.ToArray());
    }

    private static long TicksForSeconds(double seconds) =>
        (long)(seconds * HighResTimer.TicksPerSecond);

    /// <summary>
    /// Drives the splash image's update clock forward far enough to carry any active fade past its
    /// duration. The tick is kept strictly increasing across calls, because an update whose tick is not
    /// ahead of the previous one is ignored.
    /// </summary>
    private void AdvanceFade(SplashOverlay splash, double seconds)
    {
        _tick = Math.Max(_tick, HighResTimer.GetCurrentTick()) + TicksForSeconds(seconds);
        splash.Image.Update(_tick);
    }

    [Fact]
    public void TryCreate_returns_null_and_does_not_throw_for_an_undecodable_stream()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        //Act
        var splash = SplashOverlay.TryCreate(stream, host, ViewOf(host, viewBounds));

        //Assert
        splash.Should().BeNull();
    }

    [Fact]
    public void TryCreate_returns_null_for_a_missing_image_file()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        string missingPath = Path.Combine(Path.GetTempPath(), $"no-such-splash-{Guid.NewGuid():N}.png");

        //Act
        var splash = SplashOverlay.TryCreate(missingPath, host, ViewOf(host, viewBounds));

        //Assert
        splash.Should().BeNull();
    }

    [Fact]
    public void TryCreate_covers_the_viewport_and_starts_transparent_on_top()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 48);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);

        //Act
        var splash = SplashOverlay.TryCreate(stream, host, ViewOf(host, viewBounds), nickname: "splash-covers");
        _created.Add(splash!);

        //Assert
        splash.Should().NotBeNull();
        splash!.Phase.Should().Be(SplashOverlay.SplashPhase.FadingIn);
        splash.Image.ScreenBounds.Should().Be(new Rectangle(0, 0, 64, 48));
        splash.Image.ZOrder.Should().Be(int.MaxValue);
        splash.Image.Opacity.Should().Be(0f);
        splash.Mode.Should().Be(DirectDrawingMode.View);
    }

    [Fact]
    public void TryCreate_rejects_a_negative_duration()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);

        //Act
        var act = () => SplashOverlay.TryCreate(stream, host, ViewOf(host, viewBounds), holdSeconds: -1f);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_splash_sequence_holds_then_fades_out_and_reports_completion_last()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);
        var order = new List<string>();

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.2f,
            fadeOutSeconds: 0.1f,
            onHolding: () => order.Add("holding"),
            onSplashCompleted: () => order.Add("completed"),
            nickname: "splash-sequence");

        _created.Add(splash!);
        splash.Should().NotBeNull();

        //Act - the fade-in completes inside the direct-drawing update, which starts the hold phase.
        AdvanceFade(splash!, 0.5);

        //Assert - holding, with the callback already run and the hold timer registered
        splash!.Phase.Should().Be(SplashOverlay.SplashPhase.Holding);
        order.Should().ContainSingle();
        order[0].Should().Be("holding");
        splash.Image.Opacity.Should().Be(1f);

        //Act - the engine hold timer elapses, which starts the fade-out
        EngineTimer.RaiseTimerEvents(TimerType.PostCycle, HighResTimer.GetCurrentTick() + TicksForSeconds(1));

        //Assert
        splash.Phase.Should().Be(SplashOverlay.SplashPhase.FadingOut);
        order.Count.Should().Be(1);

        //Act - the fade-out completes
        AdvanceFade(splash, 0.5);

        //Assert - completed last, after the overlay disposed itself
        splash.Phase.Should().Be(SplashOverlay.SplashPhase.Completed);
        order.Count.Should().Be(2);
        order[1].Should().Be("completed");
        DirectDrawingManager.Instance.GetDirectDrawing("splash-sequence").Should().BeNull();
    }

    [Fact]
    public void the_hold_phase_uses_a_single_shot_engine_timer_so_a_pause_cannot_shorten_it()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.5f,
            fadeOutSeconds: 0.1f,
            nickname: "splash-timer");

        _created.Add(splash!);

        //Act
        AdvanceFade(splash!, 0.5);

        //Assert - an engine timer (which the engine rebaselines on resume), not a wall-clock delay
        splash!.Phase.Should().Be(SplashOverlay.SplashPhase.Holding);
        EngineTimer.Count.Should().Be(1);

        var holdTimer = EngineTimer.Get(EngineTimer.TimerIDs.Single());
        holdTimer.Type.Should().Be(TimerType.PostCycle);
        holdTimer.Cycles.Should().Be(TimerCycles.Once);

        //Act - a paused engine pushes the timer's baseline forward instead of firing it
        EngineTimer.PausedAll = true;
        EngineTimer.RaiseTimerEvents(TimerType.PostCycle, HighResTimer.GetCurrentTick() + TicksForSeconds(5));

        //Assert
        splash.Phase.Should().Be(SplashOverlay.SplashPhase.Holding);
    }

    [Fact]
    public void the_hold_phase_waits_for_asynchronous_work_to_finish()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.05f,
            fadeOutSeconds: 0.1f,
            onHoldingAsync: () => work.Task,
            nickname: "splash-async");

        _created.Add(splash!);

        AdvanceFade(splash!, 0.5);
        EngineTimer.RaiseTimerEvents(TimerType.PostCycle, HighResTimer.GetCurrentTick() + TicksForSeconds(1));

        //Assert - the hold duration elapsed, but the work has not
        splash!.Phase.Should().Be(SplashOverlay.SplashPhase.Holding);

        //Act - finishing the work posts the continuation back to the engine thread
        work.SetResult();

        bool reachedFadeOut = SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.Drain();
                return splash.Phase == SplashOverlay.SplashPhase.FadingOut;
            },
            TimeSpan.FromSeconds(10));

        //Assert
        reachedFadeOut.Should().BeTrue();
    }

    [Fact]
    public void a_hold_callback_that_throws_does_not_strand_the_splash()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);
        bool completed = false;

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.05f,
            fadeOutSeconds: 0.1f,
            onHolding: () => throw new InvalidOperationException("hold work failed"),
            onSplashCompleted: () => completed = true,
            nickname: "splash-throwing");

        _created.Add(splash!);

        //Act
        AdvanceFade(splash!, 0.5);
        EngineTimer.RaiseTimerEvents(TimerType.PostCycle, HighResTimer.GetCurrentTick() + TicksForSeconds(1));
        AdvanceFade(splash!, 0.5);

        //Assert
        splash!.Phase.Should().Be(SplashOverlay.SplashPhase.Completed);
        completed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_before_the_fade_out_cancels_the_completion_callback()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);
        bool completed = false;

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.5f,
            fadeOutSeconds: 0.1f,
            onSplashCompleted: () => completed = true,
            nickname: "splash-cancelled");

        _created.Add(splash!);
        AdvanceFade(splash!, 0.5);

        //Act
        splash!.Dispose();
        splash.Dispose();
        EngineTimer.RaiseTimerEvents(TimerType.PostCycle, HighResTimer.GetCurrentTick() + TicksForSeconds(5));
        AdvanceFade(splash, 0.5);

        //Assert - the hold timer went with it, and the callback never ran
        completed.Should().BeFalse();
        EngineTimer.Count.Should().Be(0);
        DirectDrawingManager.Instance.GetDirectDrawing("splash-cancelled").Should().BeNull();
    }

    [Fact]
    public void a_viewport_resize_restretches_the_splash()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 64, 64);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(SKColors.Fuchsia);
        View view = ViewOf(host, viewBounds);

        var splash = SplashOverlay.TryCreate(stream, host, view, nickname: "splash-resize");
        _created.Add(splash!);

        //Act
        view.Viewport.Resize(120, 90);

        //Assert
        splash!.Image.ScreenBounds.Should().Be(new Rectangle(0, 0, 120, 90));
    }

    [Fact]
    public void Draw_paints_the_faded_in_image_across_the_view()
    {
        //Arrange
        var viewBounds = new Rectangle(0, 0, 32, 32);
        var host = NewHost(viewBounds);
        using var stream = NewPngStream(new SKColor(20, 40, 200));
        using var backbuffer = new BitmapBackbuffer(32, 32);

        var splash = SplashOverlay.TryCreate(
            stream,
            host,
            ViewOf(host, viewBounds),
            fadeInSeconds: 0.1f,
            holdSeconds: 0.5f,
            fadeOutSeconds: 0.1f,
            nickname: "splash-draw");

        _created.Add(splash!);
        AdvanceFade(splash!, 0.5);

        //Act
        backbuffer.Canvas.Clear(SKColors.White);
        splash!.Image.Draw(backbuffer, new RectangleF(0f, 0f, 32f, 32f));

        //Assert - the fully opaque splash covers the middle of the view
        using SKImage snapshot = backbuffer.Snapshot();
        using SKBitmap result = SKBitmap.FromImage(snapshot);
        SKColor center = result.GetPixel(16, 16);

        center.Blue.Should().BeGreaterThan(center.Red);
        center.Blue.Should().BeGreaterThan(150);
    }
}
