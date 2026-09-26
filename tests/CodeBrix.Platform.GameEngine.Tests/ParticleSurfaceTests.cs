using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Timers;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the particle tint defect fixed upstream after the vendored baseline: the tint maths
/// assumed every particle's base colour was fully opaque, so a particle created with a translucent
/// colour rendered opaque.
/// </summary>
public class ParticleSurfaceTests : IDisposable
{
    private readonly FakeRenderSurfaceAdapter _adapter = new();
    private readonly RenderSurfaceHost<BitmapBackbuffer> _host;
    private readonly ParticleSurface _surface;

    /// <summary>Builds the minimal host a view-mode particle surface needs.</summary>
    public ParticleSurfaceTests()
    {
        _host = new RenderSurfaceHost<BitmapBackbuffer>(_adapter);
        _host.ViewManager.ConfigureSingleFullView();
        _surface = new ParticleSurface(_host, _host.ViewManager.Views[0], new Rectangle(0, 0, 64, 64));
    }

    /// <summary>Disposes the particle surface and its host so nothing survives into a later test.</summary>
    public void Dispose()
    {
        _surface.Dispose();
        _host.Dispose();
        _adapter.Dispose();
        GC.SuppressFinalize(this);
    }

    private SKColor ApplyTint(SKColor color, byte lifeAlpha, SKColor tint)
    {
        var method = typeof(ParticleSurface).GetMethod(
            "ApplyTint",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (SKColor)method.Invoke(_surface, new object[] { color, lifeAlpha, tint })!;
    }

    [Fact]
    public void ApplyTint_carries_the_particle_base_alpha_into_the_result()
    {
        //Arrange - a half-transparent particle colour, no life fade, an opaque white tint.
        var particleColor = new SKColor(200, 100, 50, 128);
        var tint = new SKColor(255, 255, 255, 255);

        //Act
        var result = ApplyTint(particleColor, lifeAlpha: 255, tint);

        //Assert - the base alpha used to be ignored, producing 255 here.
        result.Alpha.Should().Be(128);
        result.Red.Should().Be(200);
        result.Green.Should().Be(100);
        result.Blue.Should().Be(50);
    }

    [Fact]
    public void ApplyTint_keeps_an_opaque_particle_opaque()
    {
        //Arrange
        var particleColor = new SKColor(10, 20, 30, 255);
        var tint = new SKColor(255, 255, 255, 255);

        //Act
        var result = ApplyTint(particleColor, lifeAlpha: 255, tint);

        //Assert
        result.Alpha.Should().Be(255);
    }

    [Fact]
    public void ApplyTint_multiplies_the_base_alpha_by_the_life_fade_and_the_tint_alpha()
    {
        //Arrange - 128 base * 128 life * 128 tint, all over 255*255.
        var particleColor = new SKColor(255, 255, 255, 128);
        var tint = new SKColor(255, 255, 255, 128);

        //Act
        var result = ApplyTint(particleColor, lifeAlpha: 128, tint);

        //Assert
        result.Alpha.Should().Be((byte)((128 * 128 * 128) / (255 * 255)));
    }

    [Fact]
    public void the_gpu_tier_paints_a_copy_taken_at_the_last_update_and_the_cpu_tier_the_live_particles()
    {
        //Arrange - one still, long-lived particle in the top-left quarter
        _surface.Burst(StillEmitter(16, 16), 1);
        using var gpu = new GpuBackbuffer(64, 64);
        var tick = HighResTimer.GetCurrentTick() + HighResTimer.TicksPerSecond;

        //Act - the first GPU-tier paint only starts the copying; the next update publishes the particle;
        //  a burst after that update reaches the CPU tier at once and the GPU tier only with the next update
        var gpuFirstFrame = AlphaAt(gpu, 16, 16);
        var cpuFirstFrame = AlphaAt(new BitmapBackbuffer(64, 64), 16, 16);
        _surface.Update(tick);
        var gpuAfterUpdate = AlphaAt(gpu, 16, 16);
        _surface.Burst(StillEmitter(48, 48), 1);
        var gpuNewBurstBeforeUpdate = AlphaAt(gpu, 48, 48);
        var cpuNewBurstBeforeUpdate = AlphaAt(new BitmapBackbuffer(64, 64), 48, 48);
        _surface.Update(tick + (HighResTimer.TicksPerSecond / 60));
        var gpuNewBurstAfterUpdate = AlphaAt(gpu, 48, 48);

        //Assert
        gpuFirstFrame.Should().Be(0);
        cpuFirstFrame.Should().BeGreaterThan(0);
        gpuAfterUpdate.Should().BeGreaterThan(0);
        gpuNewBurstBeforeUpdate.Should().Be(0);
        cpuNewBurstBeforeUpdate.Should().BeGreaterThan(0);
        gpuNewBurstAfterUpdate.Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_gpu_tier_draw_survives_updates_and_bursts_on_another_thread()
    {
        //Arrange - the engine thread bursts, integrates and compacts while a second thread paints the way the
        //  UI thread does on the GPU tier
        var emitter = new ParticleEmitter { Position = new PointF(32, 32), LifeRange = (0.01f, 0.2f), SizeRange = (2f, 4f) };
        Exception? failure = null;
        var stop = 0;
        var painter = new Thread(() =>
        {
            using var gpu = new GpuBackbuffer(64, 64);
            var until = DateTime.UtcNow.AddSeconds(1);

            try
            {
                while (DateTime.UtcNow < until && failure is null)
                    _surface.Draw(gpu, new RectangleF(0, 0, 64, 64));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Volatile.Write(ref stop, 1);
            }
        });

        //Act
        var tick = HighResTimer.GetCurrentTick();
        painter.Start();

        while (Volatile.Read(ref stop) == 0)
        {
            tick += HighResTimer.TicksPerSecond / 60;
            _surface.Burst(emitter, 50);
            _surface.Update(tick);
        }

        painter.Join();

        //Assert
        failure.Should().BeNull();
    }

    private static ParticleEmitter StillEmitter(float x, float y) => new()
    {
        Position = new PointF(x, y),
        LifeRange = (100f, 100f),
        VelocityRangeX = (0f, 0f),
        VelocityRangeY = (0f, 0f),
        SizeRange = (4f, 4f),
        GravityX = 0f,
        GravityY = 0f,
        Color = SKColors.Red
    };

    private byte AlphaAt(BackbufferBase backbuffer, int x, int y)
    {
        try
        {
            backbuffer.Canvas.Clear(SKColors.Transparent);
            _surface.Draw(backbuffer, new RectangleF(0, 0, 64, 64));

            using var snapshot = backbuffer.Snapshot();
            using var bitmap = SKBitmap.FromImage(snapshot);
            return bitmap.GetPixel(x, y).Alpha;
        }
        finally
        {
            if (backbuffer is BitmapBackbuffer)
                backbuffer.Dispose();
        }
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(64, 64) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
