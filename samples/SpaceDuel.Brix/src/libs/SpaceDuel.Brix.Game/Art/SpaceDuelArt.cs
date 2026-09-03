using System;
using System.IO;
using System.Reflection;
using CodeBrix.Platform.GameEngine.SkiaSharp;
using SkiaSharp;

namespace SpaceDuel.Brix.Art;

/// <summary>
/// Supplies every bitmap the space duel draws with: the ship sprite sheet (an embedded PNG, with a
/// procedural stand-in when that resource is not present), the procedurally painted effects sheet
/// (two star densities plus the laser bolt), and the procedurally drawn title splash.
/// </summary>
/// <remarks>
/// Nothing here touches the file system, so the game runs from a single assembly with no content
/// files to deploy.
/// </remarks>
public static class SpaceDuelArt
{
    /// <summary>The edge length, in pixels, of one frame on the ship sprite sheet.</summary>
    public const int ShipFrameSize = 512;

    /// <summary>The edge length, in pixels, of one frame on the effects sheet.</summary>
    public const int EffectsFrameSize = 64;

    /// <summary>The effects-sheet column holding the sparse, dim star tile used by the far layer.</summary>
    public const int FarStarFrame = 0;

    /// <summary>The effects-sheet column holding the brighter star tile used by the near layer.</summary>
    public const int NearStarFrame = 1;

    /// <summary>The effects-sheet column holding the laser bolt.</summary>
    public const int LaserFrame = 2;

    /// <summary>
    /// The manifest resource name of the ship sprite sheet. It is pinned in the project file, so it
    /// does not move if the library's root namespace changes.
    /// </summary>
    public const string ShipResourceName = "SpaceDuel.Brix.Game.Assets.ships.png";

    private const int SplashWidth = 1280;
    private const int SplashHeight = 420;

    /// <summary>
    /// Loads the ship sprite sheet: a 2 x 2 grid of <see cref="ShipFrameSize"/>-pixel frames.
    /// </summary>
    /// <returns>
    /// The decoded embedded sheet, or a procedurally painted stand-in of the same shape when the
    /// embedded resource is missing or cannot be decoded.
    /// </returns>
    public static SKBitmap LoadShipBitmap()
    {
        Assembly assembly = typeof(SpaceDuelArt).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(ShipResourceName);

        if (stream is null)
            return CreateFallbackShipBitmap();

        return SKBitmap.Decode(stream) ?? CreateFallbackShipBitmap();
    }

    /// <summary>
    /// Paints the effects sheet: three <see cref="EffectsFrameSize"/>-pixel frames in one row,
    /// holding the far stars, the near stars and the laser bolt.
    /// </summary>
    /// <returns>A newly allocated bitmap; the caller hands it to the tilesheet registry.</returns>
    public static SKBitmap CreateEffectsBitmap()
    {
        var bitmap = new SKBitmap(
            EffectsFrameSize * 3,
            EffectsFrameSize,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        DrawFarStars(canvas, FrameLeft(FarStarFrame));
        DrawNearStars(canvas, FrameLeft(NearStarFrame));
        DrawLaser(canvas, FrameLeft(LaserFrame));

        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Paints the title card shown while the game starts up and returns it as an encoded PNG stream,
    /// ready for the engine's splash overlay. The art is drawn in code, so the sample carries no
    /// third-party logo.
    /// </summary>
    /// <returns>A seekable stream positioned at the start of the encoded image.</returns>
    public static Stream CreateTitleSplashStream()
    {
        using var bitmap = new SKBitmap(
            SplashWidth,
            SplashHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            DrawTitleCard(canvas);
            canvas.Flush();
        }

        return new MemoryStream(bitmap.EncodeBitmapToBytes());
    }

    private static int FrameLeft(int frame) => frame * EffectsFrameSize;

    private static void DrawFarStars(SKCanvas canvas, int x)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(138, 177, 220, 185)
        };

        canvas.DrawCircle(x + 9, 11, 1f, paint);
        canvas.DrawCircle(x + 47, 22, 0.8f, paint);
        canvas.DrawCircle(x + 29, 51, 1.1f, paint);
    }

    private static void DrawNearStars(SKCanvas canvas, int x)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(245, 249, 255, 235)
        };

        canvas.DrawCircle(x + 14, 17, 1.8f, paint);
        canvas.DrawCircle(x + 51, 43, 1.3f, paint);

        paint.Color = new SKColor(115, 210, 242, 210);
        canvas.DrawCircle(x + 34, 8, 1.5f, paint);
    }

    private static void DrawLaser(SKCanvas canvas, int x)
    {
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(55, 226, 255, 90)
        };

        canvas.DrawRoundRect(
            new SKRect(x + 26, 7, x + 38, 57),
            6,
            6,
            glow);

        using var core = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(210, 252, 255, 255)
        };

        canvas.DrawRoundRect(
            new SKRect(x + 30, 9, x + 34, 55),
            2,
            2,
            core);
    }

    private static void DrawTitleCard(SKCanvas canvas)
    {
        var panel = new SKRect(20, 20, SplashWidth - 20, SplashHeight - 20);

        using (var backdrop = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Fill,
                   Color = new SKColor(6, 14, 32, 232)
               })
        {
            canvas.DrawRoundRect(panel, 26f, 26f, backdrop);
        }

        using (var border = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 3f,
                   Color = new SKColor(82, 207, 236, 235)
               })
        {
            canvas.DrawRoundRect(panel, 26f, 26f, border);
        }

        // A short arc of "stars" so the card reads as space even before the scene appears.
        var random = new Random(4271);

        using (var star = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Fill,
                   Color = new SKColor(190, 216, 245, 150)
               })
        {
            for (int index = 0; index < 90; index++)
            {
                float starX = panel.Left + 12f + (float)random.NextDouble() * (panel.Width - 24f);
                float starY = panel.Top + 12f + (float)random.NextDouble() * (panel.Height - 24f);
                canvas.DrawCircle(starX, starY, 0.7f + (float)random.NextDouble() * 1.4f, star);
            }
        }

        SKTypeface typeface = SKTypeface.Default;

        using (var titleFont = new SKFont(typeface, 108f))
        using (var glow = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Fill,
                   Color = new SKColor(55, 226, 255, 120)
               })
        using (var title = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Fill,
                   Color = new SKColor(238, 250, 255, 255)
               })
        {
            canvas.DrawText("SPACE DUEL", SplashWidth * 0.5f + 4f, 214f, SKTextAlign.Center, titleFont, glow);
            canvas.DrawText("SPACE DUEL", SplashWidth * 0.5f, 210f, SKTextAlign.Center, titleFont, title);
        }

        using (var subtitleFont = new SKFont(typeface, 34f))
        using (var subtitle = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Fill,
                   Color = new SKColor(140, 205, 232, 235)
               })
        {
            canvas.DrawText("A CodeBrix.Platform.GameEngine sample", SplashWidth * 0.5f, 276f,
                SKTextAlign.Center, subtitleFont, subtitle);

            canvas.DrawText("A/D turn   W thrust   Space fire   R restart", SplashWidth * 0.5f, 330f,
                SKTextAlign.Center, subtitleFont, subtitle);
        }
    }

    private static SKBitmap CreateFallbackShipBitmap()
    {
        var bitmap = new SKBitmap(
            ShipFrameSize * 2,
            ShipFrameSize * 2,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        DrawFallbackShip(canvas, 0, 0, new SKColor(120, 205, 245, 255), new SKColor(232, 248, 255, 255));
        DrawFallbackShip(canvas, 1, 0, new SKColor(244, 128, 96, 255), new SKColor(255, 226, 205, 255));
        DrawFallbackShip(canvas, 0, 1, new SKColor(180, 148, 245, 255), new SKColor(238, 228, 255, 255));
        DrawFallbackShip(canvas, 1, 1, new SKColor(126, 226, 158, 255), new SKColor(226, 255, 236, 255));

        canvas.Flush();
        return bitmap;
    }

    private static void DrawFallbackShip(SKCanvas canvas, int column, int row, SKColor hull, SKColor trim)
    {
        float left = column * ShipFrameSize;
        float top = row * ShipFrameSize;
        float centerX = left + ShipFrameSize * 0.5f;

        // The sheet's frames point "up"; the engine rotates the sprite to the ship's heading.
        using var body = new SKPathBuilder();
        body.MoveTo(centerX, top + ShipFrameSize * 0.09f);
        body.LineTo(left + ShipFrameSize * 0.83f, top + ShipFrameSize * 0.86f);
        body.LineTo(centerX, top + ShipFrameSize * 0.70f);
        body.LineTo(left + ShipFrameSize * 0.17f, top + ShipFrameSize * 0.86f);
        body.Close();

        using SKPath path = body.Snapshot();

        using var hullPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = hull
        };

        canvas.DrawPath(path, hullPaint);

        using var trimPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10f,
            Color = trim
        };

        canvas.DrawPath(path, trimPaint);

        using var canopy = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = trim.WithAlpha(210)
        };

        canvas.DrawCircle(centerX, top + ShipFrameSize * 0.42f, ShipFrameSize * 0.09f, canopy);
    }
}
