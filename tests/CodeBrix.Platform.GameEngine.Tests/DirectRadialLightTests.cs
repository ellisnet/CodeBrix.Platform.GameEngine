using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the port-only pause contract on <see cref="DirectRadialLight"/>: the flicker animation
/// keeps its own tick baseline on top of the base class's update baseline, so it has to be shifted
/// on resume like every other "last tick" in the engine.
/// </summary>
public class DirectRadialLightTests
{
    [Fact]
    public void ShiftTimeBaselineForResume_keeps_the_flicker_phase_from_jumping_across_a_pause()
    {
        //Arrange - two identical flickering torches; only one of them gets the resume shift
        using var host = new TestRenderSurfaceHost();
        var layer = host.Scene.AddLayer(2, 2, 32, 32);
        using var shifted = NewFlickerLight(host, layer);
        using var unshifted = NewFlickerLight(host, layer);

        var startTick = HighResTimer.GetCurrentTick();
        shifted.Update(startTick);
        unshifted.Update(startTick);

        shifted.EffectiveIntensity.Should().Be(0.5f);
        unshifted.EffectiveIntensity.Should().Be(0.5f);

        var pausedTicks = HighResTimer.TicksPerSecond * 5;
        var resumeTick = startTick + pausedTicks;
        var firstResumedTick = resumeTick + HighResTimer.TicksPerSecond / 100; // 10 ms after resume

        //Act
        shifted.ShiftTimeBaselineForResume(pausedTicks, resumeTick);
        shifted.Update(firstResumedTick);
        unshifted.Update(firstResumedTick);

        //Assert - the shifted light sees only the 10 ms it really ran, which is under one 12 Hz
        //flicker interval, so its phase (and therefore its intensity) has not moved. The unshifted
        //light sees the whole 5-second pause as one delta and bursts the flicker forward.
        shifted.EffectiveIntensity.Should().Be(0.5f);
        (Math.Abs(unshifted.EffectiveIntensity - 0.5f) > 0.01f).Should().BeTrue();
    }

    private static DirectRadialLight NewFlickerLight(TestRenderSurfaceHost host, SceneLayer layer)
    {
        return new DirectRadialLight(
            Color.FromArgb(180, 255, 190, 80),
            host,
            layer,
            new PointF(16f, 16f),
            8f)
        {
            BlendMode = SKBlendMode.Screen,
            Intensity = 0.5f,
            FlickerAmount = 0.5f,
            FlickerRefreshHz = 12,
            FlickerEnabled = true
        };
    }
}
