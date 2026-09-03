using System;
using System.Drawing;
using System.Threading;
using CodeBrix.Platform.GameEngine.Effects;
using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the presentation-effect manager that every render surface host owns: lifecycle,
/// per-channel replacement, cancellation, the view-versus-layer target rules, and the port-only
/// pause rebaseline that keeps a paused span from bursting a running effect to completion.
/// </summary>
public class EffectsManagerTests
{
    private const float Tolerance = 0.001f;

    [Fact]
    public void Run_fade_out_on_a_view_advances_and_completes()
    {
        //Arrange
        using var host = CreateHost(out View view, out _);
        host.Scene.FullRefreshNeeded = false;

        //Act
        var effect = host.Effects.Run(view, new FadeOutEffect(1f));

        //Assert
        effect.Status.Should().Be(EffectStatus.Running);
        host.Effects.ActiveEffects.Should().ContainSingle();
        host.Scene.FullRefreshNeeded.Should().BeTrue();

        //Act
        host.Effects.Advance(0.5f);

        //Assert
        view.EffectOpacity.Should().BeApproximately(0.5f, Tolerance);
        effect.Progress.Should().BeApproximately(0.5f, Tolerance);

        //Act
        host.Effects.Advance(0.5f);

        //Assert
        effect.Status.Should().Be(EffectStatus.Completed);
        view.EffectOpacity.Should().Be(0f);
        host.Effects.ActiveEffects.Should().BeEmpty();
    }

    [Fact]
    public void Run_fade_out_on_a_scene_layer_does_not_change_view_opacity()
    {
        //Arrange
        using var host = CreateHost(out View view, out SceneLayer layer);

        //Act
        host.Effects.Run(layer, new FadeOutEffect(1f));
        host.Effects.Advance(0.25f);

        //Assert
        view.EffectOpacity.Should().Be(1f);
        layer.EffectOpacity.Should().BeApproximately(0.75f, Tolerance);
    }

    [Fact]
    public void Run_replaces_an_effect_on_the_same_target_and_channel()
    {
        //Arrange
        using var host = CreateHost(out View view, out _);
        var first = host.Effects.Run(view, new FadeOutEffect(1f));
        host.Effects.Advance(0.25f);

        //Act
        var replacement = host.Effects.Run(view, new FadeInEffect(1f));

        //Assert - the replacement continues from the value the first effect left behind.
        first.Status.Should().Be(EffectStatus.Cancelled);
        replacement.Status.Should().Be(EffectStatus.Running);
        host.Effects.ActiveEffects.Should().ContainSingle();
        view.EffectOpacity.Should().BeApproximately(0.75f, Tolerance);

        //Act
        host.Effects.Advance(0.5f);

        //Assert
        view.EffectOpacity.Should().BeApproximately(0.875f, Tolerance);
    }

    [Fact]
    public void Run_composes_effects_on_compatible_channels_for_one_target()
    {
        //Arrange
        using var host = CreateHost(out View view, out _);

        //Act
        host.Effects.Run(view, new FadeOutEffect(1f));
        host.Effects.Run(
            view,
            new SlideOutEffect(
                EffectDirection.FromLeftToRight,
                1f,
                EasingKind.Linear));

        //Assert - opacity and transform are separate channels, so both run.
        host.Effects.ActiveEffects.Count.Should().Be(2);

        //Act
        host.Effects.Advance(0.5f);

        //Assert
        view.EffectOpacity.Should().BeApproximately(0.5f, Tolerance);
        view.EffectOffsetFactor.X.Should().BeApproximately(0.5f, Tolerance);
    }

    [Fact]
    public void Cancel_restores_the_state_from_before_the_effect()
    {
        //Arrange
        using var host = CreateHost(out View view, out _);
        var effect = host.Effects.Run(
            view,
            new SlideOutEffect(
                EffectDirection.FromTopToBottom,
                1f,
                EasingKind.Linear));
        host.Effects.Advance(0.5f);

        //Act
        effect.Cancel();

        //Assert
        effect.Status.Should().Be(EffectStatus.Cancelled);
        view.EffectOffsetFactor.Should().Be(PointF.Empty);
        host.Effects.ActiveEffects.Should().BeEmpty();
    }

    [Fact]
    public void Run_slide_changes_presentation_coordinates_without_changing_world_state()
    {
        //Arrange
        using var host = CreateHost(out View view, out SceneLayer layer);
        PointF world = new(20f, 10f);

        //Act
        host.Effects.Run(
            view,
            new SlideOutEffect(
                EffectDirection.FromLeftToRight,
                1f,
                EasingKind.Linear));
        host.Effects.Advance(0.5f);

        PointF screen = view.WorldPxToScreenPx(layer, world);
        PointF roundTrip = view.ScreenPxToWorldPx(layer, screen);

        //Assert - the view is offset by half its 200 px width, and the transform still round-trips.
        screen.X.Should().BeApproximately(120f, Tolerance);
        roundTrip.X.Should().BeApproximately(world.X, Tolerance);
        roundTrip.Y.Should().BeApproximately(world.Y, Tolerance);
        view.Camera.PositionPx.Should().Be(PointF.Empty);
        layer.OriginPx.Should().Be(Point.Empty);
    }

    [Fact]
    public void Run_fill_and_erase_update_the_reveal_state()
    {
        //Arrange
        using var host = CreateHost(out _, out SceneLayer layer);

        //Act
        host.Effects.Run(
            layer,
            new FillEffect(EffectDirection.FromRightToLeft, 1f));

        //Assert
        layer.EffectReveal.Should().Be(0f);

        //Act
        host.Effects.Advance(1f);

        //Assert
        layer.EffectReveal.Should().Be(1f);

        //Act
        host.Effects.Run(
            layer,
            new EraseEffect(EffectDirection.FromTopToBottom, 1f));
        host.Effects.Advance(0.5f);

        //Assert
        layer.EffectReveal.Should().BeApproximately(0.5f, Tolerance);
        layer.EffectRevealDirection.Should().Be(EffectDirection.FromTopToBottom);
    }

    [Fact]
    public void Run_earthquake_is_view_only_and_resets_its_offset_at_completion()
    {
        //Arrange
        using var host = CreateHost(out View view, out SceneLayer layer);
        var effect = host.Effects.Run(
            view,
            new EarthquakeEffect(1f, intensityPx: 10f, randomSeed: 7));

        //Act
        host.Effects.Advance(0.25f);

        //Assert
        view.EffectOffsetPx.Should().NotBe(PointF.Empty);

        Action runOnLayer = () => host.Effects.Run(layer, new EarthquakeEffect(1f));
        runOnLayer.Should().Throw<ArgumentException>();

        //Act
        host.Effects.Advance(0.75f);

        //Assert
        effect.Status.Should().Be(EffectStatus.Completed);
        view.EffectOffsetPx.Should().Be(PointF.Empty);
    }

    [Fact]
    public void Run_zoom_delegates_the_animation_to_the_viewport_without_advancing_it_twice()
    {
        //Arrange
        using var host = CreateHost(out View view, out _);
        var effect = host.Effects.Run(view, new ZoomInEffect(2f, 1f));

        //Act - the effect owns lifecycle only; the viewport's own animator moves the zoom.
        host.Effects.Advance(0.5f);

        //Assert
        view.Viewport.Zoom.Should().Be(1f);
        view.Viewport.IsZoomAnimating.Should().BeTrue();

        //Act
        view.Update(0.5f);

        //Assert
        view.Viewport.Zoom.Should().BeGreaterThan(1f);
        view.Viewport.Zoom.Should().BeLessThanOrEqualTo(2f);
        effect.Status.Should().Be(EffectStatus.Running);

        //Act
        host.Effects.Advance(0.5f);

        //Assert
        effect.Status.Should().Be(EffectStatus.Completed);
        view.Viewport.Zoom.Should().Be(2f);
        view.Viewport.IsZoomAnimating.Should().BeFalse();
    }

    [Fact]
    public void Run_rejects_a_target_owned_by_another_host()
    {
        //Arrange
        using var host = CreateHost(out _, out _);
        using var other = CreateHost(out View otherView, out _);

        //Act
        Action runForeignView = () => host.Effects.Run(otherView, new FadeOutEffect(1f));

        //Assert
        runForeignView.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShiftTimeBaselineForResume_keeps_a_running_effect_from_bursting_across_a_pause()
    {
        //Arrange - two identical hosts driven by the same ticks; only one is rebaselined.
        using var shifted = CreateHost(out View shiftedView, out _);
        using var unshifted = CreateHost(out View unshiftedView, out _);

        long startTick = HighResTimer.GetCurrentTick();
        shifted.Effects.Update(startTick);
        unshifted.Effects.Update(startTick);

        var shiftedEffect = shifted.Effects.Run(shiftedView, new FadeOutEffect(1f));
        var unshiftedEffect = unshifted.Effects.Run(unshiftedView, new FadeOutEffect(1f));

        long pausedTicks = HighResTimer.TicksPerSecond * 5;
        long resumeTick = startTick + pausedTicks;

        //Act
        shifted.Effects.ShiftTimeBaselineForResume(pausedTicks, resumeTick);
        shifted.Effects.Update(resumeTick);
        unshifted.Effects.Update(resumeTick);

        //Assert - the rebaselined host sees no elapsed time; the other one burns the whole pause.
        shiftedEffect.Status.Should().Be(EffectStatus.Running);
        shiftedEffect.Progress.Should().BeApproximately(0f, Tolerance);
        shiftedView.EffectOpacity.Should().BeApproximately(1f, Tolerance);

        unshiftedEffect.Status.Should().Be(EffectStatus.Completed);
        unshiftedView.EffectOpacity.Should().Be(0f);
    }

    [Fact]
    public void ShiftTimeBaselineForResume_is_wired_into_the_engine_pause_and_resume()
    {
        //Arrange - a real host so the engine's pause capture has a backbuffer to snapshot.
        using var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene, limitCameraToWorldBoundPx: false);
        View view = host.ViewManager.Views[0];

        host.Effects.Update(HighResTimer.GetCurrentTick());
        var effect = host.Effects.Run(view, new FadeOutEffect(0.5f));

        try
        {
            //Act - a pause far longer than the effect, then one foreground update after resume.
            Engine.Instance.Pause();
            Thread.Sleep(300);
            Engine.Instance.Resume();
            host.Effects.Update(HighResTimer.GetCurrentTick());

            //Assert - the effect saw only the sliver of time since the resume.
            effect.Status.Should().Be(EffectStatus.Running);
            effect.Progress.Should().BeLessThan(0.1f);
            view.EffectOpacity.Should().BeGreaterThan(0.9f);
        }
        finally
        {
            if (Engine.Instance.IsPaused)
                Engine.Instance.Resume();
        }
    }

    /// <summary>Creates a host with one 200 x 100 view and one 10 x 10 layer of 32 px tiles.</summary>
    private static TestRenderSurfaceHost CreateHost(out View view, out SceneLayer layer)
    {
        var host = new TestRenderSurfaceHost();
        layer = host.Scene.AddLayer(10, 10, width: 32, height: 32);
        host.ViewManager.AddView(new Rectangle(0, 0, 200, 100));
        view = host.ViewManager.Views[0];
        return host;
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(200, 100) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
