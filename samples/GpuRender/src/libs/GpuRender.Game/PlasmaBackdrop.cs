using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.SkiaSharp;
using SkiaSharp;

namespace GpuRender.Game;

/// <summary>
/// A custom view-mode direct drawing that fills its bounds with an animated SkSL plasma
/// (<see cref="SKRuntimeEffect"/>) under a drifting starfield. Because it is an ordinary
/// <see cref="DirectDrawingBase"/>, it participates in direct-drawing Z-ordering — give it a low
/// <see cref="DirectDrawingBase.ZOrder"/> and overlays such as <see cref="TextBlock"/> render on
/// top. On the GpuRendering-OpenGL (GPU) path its <see cref="OnDraw"/> runs on the GL thread with the
/// <c>GRContext</c> current, so the shader executes on the GPU; on CpuRendering the same shader is
/// evaluated by Skia's raster backend on the CPU.
/// </summary>
public sealed class PlasmaBackdrop : DirectDrawingBase
{
    private const int StarCount = 220;

    // Three interfering waves plus a slow radial swirl, palette-cycled.
    private const string PlasmaSksl = @"
uniform float iTime;
uniform float2 iResolution;

half4 main(float2 fragCoord) {
    float2 uv = fragCoord / iResolution;
    float2 p = uv * 2.0 - 1.0;
    p.x *= iResolution.x / iResolution.y;

    float t = iTime * 0.6;
    float v = sin(p.x * 4.0 + t);
    v += sin((p.y + t) * 3.0);
    v += sin((p.x + p.y + t) * 5.0);
    float r = length(p + float2(sin(t * 0.7), cos(t * 0.9)) * 0.4);
    v += sin(r * 8.0 - t * 2.0);
    v *= 0.25;

    float3 col = 0.5 + 0.5 * cos(6.28318 * (v + float3(0.0, 0.33, 0.67)) + t * 0.3);
    col *= 0.55 + 0.45 * smoothstep(1.6, 0.2, r);
    return half4(half3(col), 1.0);
}";

    private readonly SKRuntimeEffect _effect;
    private readonly SKRuntimeEffectUniforms _uniforms;
    private readonly SKPaint _plasmaPaint = new();
    private readonly SKPaint _starPaint = new() { Color = SKColors.White, IsAntialias = true };

    // Star seeds fixed at construction so every frame is a pure function of engine time.
    private readonly float[] _starSeedX = new float[StarCount];
    private readonly float[] _starSeedY = new float[StarCount];
    private readonly float[] _starSpeed = new float[StarCount];
    private readonly float[] _starSize = new float[StarCount];

    /// <summary>
    /// Initializes a new instance of the <see cref="PlasmaBackdrop"/> class covering the given
    /// screen bounds of a view.
    /// </summary>
    /// <param name="renderSurfaceHost">The render-surface host to draw into.</param>
    /// <param name="view">The view this backdrop belongs to.</param>
    /// <param name="screenBounds">The screen-space bounds to fill, in pixels.</param>
    /// <exception cref="InvalidOperationException">Thrown when the SkSL shader fails to compile.</exception>
    public PlasmaBackdrop(RenderSurfaceHostBase renderSurfaceHost, View view, Rectangle screenBounds)
        : base(renderSurfaceHost, DirectDrawingMode.View, null, view, screenBounds, null, "plasma-backdrop")
    {
        _effect = SKRuntimeEffect.CreateShader(PlasmaSksl, out var errors)
            ?? throw new InvalidOperationException($"Plasma shader failed to compile: {errors}");
        _uniforms = new SKRuntimeEffectUniforms(_effect);

        var rng = new Random(20260717);
        for (int i = 0; i < StarCount; i++)
        {
            _starSeedX[i] = (float)rng.NextDouble();
            _starSeedY[i] = (float)rng.NextDouble();
            _starSpeed[i] = 0.02f + (float)rng.NextDouble() * 0.12f;   // fraction of width per second
            _starSize[i] = 0.75f + (float)(rng.NextDouble() * rng.NextDouble()) * 2.25f;
        }
    }

    /// <summary>
    /// Marks the backdrop dirty every engine frame so the CpuRendering (CPU) dirty-rectangle path keeps
    /// animating it; the GpuRendering-OpenGL (GPU) path re-renders the full surface each frame regardless.
    /// </summary>
    /// <param name="tick">The current engine tick.</param>
    public override void Update(long tick)
    {
        base.Update(tick);
        ForceRefresh();
    }

    /// <inheritdoc />
    protected override void OnDraw(BackbufferBase backbuffer, RectangleF destRectScreen)
    {
        var canvas = backbuffer.Canvas;
        float w = destRectScreen.Width;
        float h = destRectScreen.Height;
        if (w <= 0f || h <= 0f)
            return;

        float time = (float)Engine.Instance.TotalSecondsEngineRunning;
        var rect = destRectScreen.ToSKRect();

        // 1) Plasma: one rect through the SkSL shader.
        _uniforms["iTime"] = time;
        _uniforms["iResolution"] = new[] { w, h };
        using (var shader = _effect.ToShader(_uniforms))
        {
            _plasmaPaint.Shader = shader;
            canvas.DrawRect(rect, _plasmaPaint);
            _plasmaPaint.Shader = null;
        }

        // 2) Starfield: positions derived from time — faster stars are brighter and drift
        //    further per second, wrapping across the width.
        for (int i = 0; i < StarCount; i++)
        {
            float x = rect.Left + (_starSeedX[i] + time * _starSpeed[i]) % 1f * w;
            float y = rect.Top + _starSeedY[i] * h;
            byte alpha = (byte)(90 + (_starSpeed[i] / 0.14f) * 165f);
            _starPaint.Color = _starPaint.Color.WithAlpha(alpha);
            canvas.DrawCircle(x, y, _starSize[i], _starPaint);
        }
    }
}
