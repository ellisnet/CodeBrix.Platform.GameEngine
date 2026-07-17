using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Drawing;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Drawing.Direct.Particles;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
using SkiaSharp;
using static CodeBrix.Platform.GameEngine.Drawing.Direct.TextBlock;

namespace ParticleTest.Game;

/// <summary>
/// Drives the ParticleTest sample: a particle-effects showcase (sparks, rain, a campfire) plus a
/// glowing, pulsing rounded box with wrapped shadowed text that animates upward. This is a port of the
/// Gondwana ParticleTest demo onto a CodeBrix.Platform <see cref="GameSurfaceCanvas"/>.
/// </summary>
public sealed class ParticleTestGame
{
    // The fixed render resolution the hosting page pins (SetRenderResolution) and the
    // campfire's home at the middle of the bottom edge, in that render space.
    private const float RenderWidth = 1280f;
    private const float RenderHeight = 720f;
    private const float CampfireHalfWidth = 90f;
    private const float CampfireTop = 600f;

    private readonly GameSurfaceCanvas _canvas;
    private ParticleSurface _particleSurface;
    private TextBlock _textBlock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleTestGame"/> class.
    /// </summary>
    /// <param name="canvas">The render surface to draw into.</param>
    public ParticleTestGame(GameSurfaceCanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    /// <summary>
    /// Starts the engine and builds the particle scene. Must be called on the UI thread once the
    /// surface has a non-zero size.
    /// </summary>
    public void Start()
    {
        var renderSurface = _canvas.Host;
        var adapter = renderSurface.RenderSurfaceAdapter;

        renderSurface.ViewManager.ConfigureSingleFullView();

        Engine.Instance.CPSCalculated += (cps) =>
        {
            var sb = new StringBuilder()
                .Append("Oh no!!! The wizard doth spray purple slime! (version " + EngineInfo.Version + ")")
                .AppendLine($" There are {_particleSurface?.ActiveParticleCount ?? 0} active particles!!!")
                .AppendLine(cps.ToString());

            _textBlock?.SetText(sb.ToString());
        };

        Engine.Instance.Start(SynchronizationContext.Current);
        Engine.Instance.Configuration.TargetFPS = 90;

        _particleSurface = new ParticleSurface(renderSurface, renderSurface.ViewManager.Views[0], new Rectangle(0, 0, adapter.Width, adapter.Height), null, 10000);
        _particleSurface.CullingMarginX = 1300f;
        _particleSurface.Emitters.Add(GetSparks(adapter.Width, adapter.Height));
        _particleSurface.Emitters.Add(GetRain(adapter.Width));
        _particleSurface.Emitters.Add(GetCampfireSparks(adapter.Width, adapter.Height));
        _particleSurface.Emitters.Add(GetCampfire(adapter.Width, adapter.Height));
        _particleSurface.Emitters.Add(GetCampfireEmbers(adapter.Width, adapter.Height));

        var glowBox = new DirectRectangle(Color.Blue, renderSurface, renderSurface.ViewManager.Views[0], new Rectangle(20, adapter.Height * 7 / 10, adapter.Width - 40, 160), null)
            .SetAlpha(128)
            .SetCornerRadius(6f)
            .SetBorderColor(Color.White)
            .SetFilled(true)
            .SetStrokeWidth(6f)
            .SetStrokeAlign(DirectRectangle.StrokeAlign.Outside)
            .PulseBorder(Color.Lime, Color.Red, 2.0f)
            .SetBlendMode(SKBlendMode.Screen)
            .PulseFill(Color.Blue, Color.Purple, 1.25f);

        glowBox.ZOrder = 1;

        _textBlock = new TextBlock(renderSurface, renderSurface.ViewManager.Views[0], new Rectangle(20, adapter.Height * 7 / 10, adapter.Width - 40, 160), null)
            .SetFont(SKTypeface.FromFamilyName("Papyrus"), 16f, minSize: 14f)
            .SetColors(Color.White, Color.Transparent)
            .SetAlignment(SKTextAlign.Center, VerticalAlign.Center)
            .EnableWrapping()
            .SetMaxLines(6)
            .UseShadow()
            .SetShadow(6, 6, 200, 3.0f)
            .UseOutline();

        _textBlock.ZOrder = 10;

        var composite = new DirectComposite(renderSurface, DirectDrawingMode.View);
        composite.Add(glowBox)
                 .Add(_textBlock);

        composite.Movement.MoveBy(new Vector2(0, -500), 10f, EasingFunctions.EaseInOutQuad);

        // Clicking the campfire (bottom-center) toggles the global engine pause. This hooks
        // the canvas's UI-level pointer event on purpose: engine input pollers stop while
        // paused, but UI-level input keeps flowing — so the same click resumes.
        _canvas.PointerPressed += OnCanvasPointerPressed;
    }

    /// <summary>
    /// Stops the engine. Call when the hosting page is closing.
    /// </summary>
    public void Stop()
    {
        _canvas.PointerPressed -= OnCanvasPointerPressed;
        Engine.Instance.Stop();
    }

    private void OnCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_canvas).Position;

        // Map the click from canvas element coordinates to the pinned 1280x720 render space,
        // across the same aspect-fit letterbox the canvas paints with.
        var rasterScale = (float)(_canvas.XamlRoot?.RasterizationScale ?? 1.0);
        float surfaceWidth = (float)_canvas.ActualWidth * rasterScale;
        float surfaceHeight = (float)_canvas.ActualHeight * rasterScale;
        if (surfaceWidth <= 0f || surfaceHeight <= 0f)
            return;

        float fitScale = Math.Min(surfaceWidth / RenderWidth, surfaceHeight / RenderHeight);
        float offsetX = (surfaceWidth - RenderWidth * fitScale) * 0.5f;
        float offsetY = (surfaceHeight - RenderHeight * fitScale) * 0.5f;
        float bufferX = ((float)position.X * rasterScale - offsetX) / fitScale;
        float bufferY = ((float)position.Y * rasterScale - offsetY) / fitScale;

        bool onCampfire = Math.Abs(bufferX - RenderWidth / 2f) <= CampfireHalfWidth
            && bufferY >= CampfireTop && bufferY <= RenderHeight + 5f;
        if (!onCampfire)
            return;

        if (Engine.Instance.IsPaused)
            Engine.Instance.Resume();
        else
            Engine.Instance.Pause();

        Console.WriteLine($"[ParticleTest] Campfire clicked -> {(Engine.Instance.IsPaused ? "PAUSED" : "RESUMED")}");
    }

    private ParticleEmitter GetSparks(float width, float height)
    {
        return new ParticleEmitter
        {
            Position = new PointF(width / 2, height),
            EmitRate = 400,
            LifeRange = (0.5f, 2.0f),
            VelocityRangeX = (-150f, 150f),
            VelocityRangeY = (-300f, -200f),
            SizeRange = (0.1f, 3f),
            Color = SKColors.BlueViolet
        };
    }

    private ParticleEmitter GetRain(float w)
    {
        var rng = new Random();

        return new ParticleEmitter
        {
            Position = new PointF(0f, 0f),
            EmitRate = 800f,
            LifeRange = (1.0f, 1.5f),
            VelocityRangeX = (-10f, 10f),
            VelocityRangeY = (500f, 700f),
            SizeRange = (1f, 2f),
            Color = new SKColor(120, 160, 255, 180),

            OnSpawn = (ref Particle p) =>
            {
                p.X = (float)(rng.NextDouble() * w);
                p.Y = -4f;
                p.VX += (float)(rng.NextDouble() * 20f - 10f);
            }
        };
    }

    private ParticleEmitter GetCampfire(float width, float height)
    {
        var rng = new Random();

        return new ParticleEmitter
        {
            Position = new PointF(width / 2f, height),
            JitterX = 18f,
            JitterY = 6f,
            EmitRate = 90,
            LifeRange = (0.35f, 0.8f),
            VelocityRangeX = (-20f, 20f),
            VelocityRangeY = (-140f, -70f),
            SizeRange = (6f, 14f),
            GravityY = -60f,
            Color = new SKColor(255, 150, 60, 240),

            OnSpawn = (ref Particle p) =>
            {
                p.VX += (float)(rng.NextDouble() * 30f - 15f);

                int r = rng.Next(4);
                switch (r)
                {
                    case 0:
                        p.Color = new SKColor(255, 90, 20, 240);
                        break;
                    case 1:
                        p.Color = new SKColor(255, 140, 40, 240);
                        break;
                    case 2:
                        p.Color = new SKColor(255, 190, 60, 240);
                        break;
                    default:
                        p.Color = new SKColor(255, 230, 120, 240);
                        p.Size *= 0.7f;
                        break;
                }

                byte alpha = (byte)(200 + rng.Next(55));
                p.Color = p.Color.WithAlpha(alpha);
            }
        };
    }

    private ParticleEmitter GetCampfireSparks(float width, float height)
    {
        return new ParticleEmitter
        {
            Position = new PointF(width / 2f, height),
            JitterX = 12f,
            JitterY = 6f,
            EmitRate = 12,
            LifeRange = (0.6f, 1.4f),
            VelocityRangeX = (-35f, 35f),
            VelocityRangeY = (-180f, -120f),
            SizeRange = (2f, 4f),
            Color = new SKColor(255, 210, 120, 255),
            GravityY = -20f
        };
    }

    private ParticleEmitter GetCampfireEmbers(float width, float height)
    {
        var rng = new Random();

        return new ParticleEmitter
        {
            Position = new PointF(width / 2f, height - 4f),
            JitterX = 14f,
            JitterY = 4f,
            EmitRate = 45f,
            LifeRange = (0.25f, 0.7f),
            VelocityRangeX = (-12f, 12f),
            VelocityRangeY = (-35f, -10f),
            SizeRange = (3f, 7f),
            GravityY = -10f,
            Color = new SKColor(255, 120, 40, 180),

            OnSpawn = (ref Particle p) =>
            {
                int roll = rng.Next(5);
                switch (roll)
                {
                    case 0:
                        p.Color = new SKColor(255, 80, 20, (byte)(160 + rng.Next(60)));
                        break;
                    case 1:
                    case 2:
                        p.Color = new SKColor(255, 110, 30, (byte)(170 + rng.Next(60)));
                        break;
                    case 3:
                        p.Color = new SKColor(255, 150, 50, (byte)(180 + rng.Next(50)));
                        break;
                    default:
                        p.Color = new SKColor(255, 200, 80, (byte)(140 + rng.Next(40)));
                        p.Size *= 0.8f;
                        break;
                }

                p.VX += (float)(rng.NextDouble() * 10f - 5f);
            }
        };
    }
}
