using System;
using System.Drawing;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
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
