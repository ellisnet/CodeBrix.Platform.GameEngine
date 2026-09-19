using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Assets.Models;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// Pre-renders a glTF model of a Kenney pack into one engine <see cref="Tilesheet"/> of sprite
/// frames, which is how a two-dimensional game uses three-dimensional art.
/// </summary>
/// <remarks>
/// <para>
/// The produced sheet follows the output layout contract documented on
/// <see cref="ModelRenderOptions"/> exactly: one uniform-grid region per rendered animation named
/// after that animation, plus a <see cref="ModelRenderOptions.RestPoseRegionName"/> region when
/// <see cref="ModelRenderOptions.IncludeRestPose"/> is set; columns are frames, rows are camera
/// directions, every cell is <see cref="ModelRenderOptions.FrameSize"/>, and one common camera fit
/// is shared by the whole sheet so the model never appears to breathe from cell to cell.
/// </para>
/// <para>
/// Materialization is IDEMPOTENT by key: when the engine's tilesheet registry already holds the key,
/// that sheet is returned untouched. Re-registering would dispose the sheet the game is already
/// drawing with, so the first materialization of a key wins and a caller that wants a second
/// variant asks for it under another key.
/// </para>
/// </remarks>
internal sealed class ModelTilesheetMaterializer
{
    private readonly object _gate = new();
    private readonly GltfModelReader _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelTilesheetMaterializer"/> class.
    /// </summary>
    /// <param name="reader">The glTF reader to read models and bake animations through. It is shared
    /// with the model-data route, so a model read for either route is parsed once.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null.</exception>
    internal ModelTilesheetMaterializer(GltfModelReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <summary>
    /// Renders a glTF asset into a tilesheet and registers it under a key.
    /// </summary>
    /// <param name="entry">The catalogued asset to render; its kind must be
    /// <see cref="GameAssetKind.Model3D"/> in glTF form.</param>
    /// <param name="key">The registry key to register the sheet under.</param>
    /// <param name="options">
    /// The tilesheet options, whose <see cref="TilesheetMaterializeOptions.ModelRender"/> steers the
    /// camera, the animations and the shading, or <see langword="null"/> for the defaults - an
    /// eight-direction sheet of the model's rest pose.
    /// </param>
    /// <returns>
    /// The registered sheet, which is the one already registered under <paramref name="key"/> when
    /// there was one.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is null, empty or whitespace; when an animation the options
    /// named is not one the asset offers; when the cell size or direction count is not positive; or
    /// when the sheet would exceed <see cref="ModelSheetLayout.MaxDimension"/> pixels, in which case
    /// the message says what to reduce.
    /// </exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a glTF model -
    /// the <c>.fbx</c>, <c>.obj</c>, <c>.mtl</c>, <c>.dae</c> and <c>.stl</c> copies Kenney ships
    /// beside it are catalogued for discovery only.</exception>
    /// <exception cref="System.IO.FileNotFoundException">Thrown when the model references a buffer or
    /// texture that does not resolve inside the pack; the message names the unresolved path.</exception>
    /// <exception cref="System.IO.InvalidDataException">Thrown when the asset is not a loadable glTF
    /// document, or carries no triangle geometry.</exception>
    internal Tilesheet Materialize(
        KenneyAssetEntry entry, string key, TilesheetMaterializeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        GltfModelReader.EnsureGltfModel(entry, key);

        ModelRenderOptions renderOptions = options?.ModelRender ?? new ModelRenderOptions();

        lock (_gate)
        {
            if (TilesheetRegistry.Instance.TryGet(key, out Tilesheet? existing) && existing is not null)
            {
                return existing;
            }

            GameModel model = ReadModel(entry, renderOptions, out List<GameModelAnimationClip> clips);
            ModelSheetLayout layout = ModelSheetLayout.Create(renderOptions, clips);
            ModelSpriteRenderer renderer = new(renderOptions);
            ModelCameraFit fit = renderer.ComputeFit(model, clips);

            SKBitmap bitmap = Render(renderer, layout, model, fit);

            //The sheet takes ownership of the bitmap and disposes it with itself
            Tilesheet sheet = TilesheetRegistry.Instance.LoadFromBitmap(key, bitmap);

            foreach (ModelSheetRegion region in layout.Regions)
            {
                sheet.AddRegion(region.Name, region.Area, layout.FrameSize);
            }

            return sheet;
        }
    }

    private GameModel ReadModel(
        KenneyAssetEntry entry,
        ModelRenderOptions renderOptions,
        out List<GameModelAnimationClip> clips)
    {
        IReadOnlyList<string>? requested = renderOptions.AnimationNames;

        if (requested is null || requested.Count == 0)
        {
            clips = [];

            return _reader.Read(entry);
        }

        //The frame rate a sheet is sampled at is deliberately lower than the model-data default,
        //  because every frame costs a cell of sheet area
        GameModel model = _reader.Read(
            entry,
            new ModelMaterializeOptions
            {
                AnimationNames = requested,
                AnimationFramesPerSecond = Math.Max(renderOptions.AnimationFramesPerSecond, 1),
            });

        clips = new List<GameModelAnimationClip>(model.Animations.Count);

        //The clips are taken in the order the options asked for them, not in the model's own order,
        //  so a caller can predict which region sits where in the sheet
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in requested)
        {
            if (!seen.Add(name)) { continue; }

            if (model.TryGetAnimation(name, out GameModelAnimationClip? clip)) { clips.Add(clip); }
        }

        return model;
    }

    private static SKBitmap Render(
        ModelSpriteRenderer renderer, ModelSheetLayout layout, GameModel model, in ModelCameraFit fit)
    {
        SKBitmap bitmap = new(
            new SKImageInfo(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

        try
        {
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Transparent);

            Size frameSize = layout.FrameSize;

            foreach (ModelSheetRegion region in layout.Regions)
            {
                for (int row = 0; row < region.Rows; row++)
                {
                    for (int column = 0; column < region.Columns; column++)
                    {
                        GameModelAnimationFrame? frame = region.Clip is null
                            ? null
                            : region.Clip.Frames[column];

                        float left = region.Area.X + (column * frameSize.Width);
                        float top = region.Area.Y + (row * frameSize.Height);

                        renderer.RenderInto(
                            canvas,
                            new SKRect(left, top, left + frameSize.Width, top + frameSize.Height),
                            model,
                            frame,
                            row,
                            fit);
                    }
                }
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }
}
