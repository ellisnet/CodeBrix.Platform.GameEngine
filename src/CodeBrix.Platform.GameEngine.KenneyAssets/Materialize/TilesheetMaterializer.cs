using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Turns the picture assets of a Kenney bundle - a loose image, a sprite atlas or an SVG vector - into
/// tilesheets registered in the engine's <see cref="TilesheetRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Three routes, one per asset kind, all reached through
/// <see cref="MaterializeTilesheet(KenneyAssetEntry, string, TilesheetMaterializeOptions?)"/>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A loose IMAGE becomes one sheet whose <c>default</c> region holds the whole image as a single tile,
/// so the image is addressed as <c>sheet[0, 0]</c>. Asking for a tile size adds a second region, named
/// <see cref="GridRegionName"/>, that lays a uniform grid over the same image.
/// </description></item>
/// <item><description>
/// A SPRITE ATLAS becomes one sheet holding its sheet image, plus one single-tile region per frame
/// named after the frame, so a frame is addressed as <c>sheet["ballBlue", 0, 0]</c>. The sheet's
/// <c>default</c> region is left as the engine created it, covering the whole sheet image with no tile
/// size: the frames are what a caller wants, and giving the default region a tile size would copy the
/// whole sheet image a second time.
/// </description></item>
/// <item><description>
/// A VECTOR is rasterized at the size the options ask for and becomes one sheet, addressed the same way
/// as a loose image.
/// </description></item>
/// </list>
/// <para>
/// Materializing is IDEMPOTENT by registry key: a key already in the registry is handed back as it is,
/// because registering over a key disposes the tilesheet already registered there and every frame
/// handed out from it. A caller that wants a second variant of one asset - a different tile size, say -
/// asks for it under its own key with <see cref="TilesheetMaterializeOptions.RegisterAs"/>.
/// </para>
/// <para>
/// One instance is safe to use from several threads. Check-then-load is serialized, so two threads
/// asking for the same key at once get the same tilesheet rather than two sheets and a disposed one.
/// </para>
/// </remarks>
internal sealed class TilesheetMaterializer
{
    /// <summary>
    /// The name of the extra region added to a loose image when
    /// <see cref="TilesheetMaterializeOptions.TileSize"/> asks for a uniform grid. The whole-image
    /// <c>default</c> region stays, so both addressings work on the same sheet.
    /// </summary>
    internal const string GridRegionName = "grid";

    //The extensions worth stripping from an atlas frame name. Kenney writes frame names as file names
    //  ("ballBlue.png"), but a frame name can also carry a dot that is not an extension, so only a
    //  known image extension is removed
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "bmp", "webp" };

    private readonly object _gate = new();
    private readonly List<string> _warnings = [];

    /// <summary>
    /// Gets the warnings recorded while materializing, oldest first, each one naming the registry key
    /// it belongs to.
    /// </summary>
    /// <remarks>
    /// These are the things a caller should know about but that do not stop an asset from being usable:
    /// two atlas frames whose names collide, a frame rectangle that falls outside its sheet image, a
    /// tile size that leaves no whole tile. The provider surfaces them alongside the warnings its pack
    /// index recorded. Each read returns a snapshot.
    /// </remarks>
    internal IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate)
            {
                return _warnings.ToArray();
            }
        }
    }

    /// <summary>
    /// Works out the region name an atlas frame is registered under.
    /// </summary>
    /// <param name="frameName">The frame name as the atlas document writes it.</param>
    /// <param name="stripFrameExtension">Whether an image file extension carried in the name is removed.</param>
    /// <returns>The region name, which is the frame name with an image extension removed when asked for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="frameName"/> is null.</exception>
    /// <remarks>
    /// Only a known image extension is removed, so a frame called <c>hud_1.5x.png</c> becomes
    /// <c>hud_1.5x</c> rather than <c>hud_1</c>. A name that is nothing but an extension keeps its name,
    /// since an empty region name is not addressable.
    /// </remarks>
    internal static string RegionNameForFrame(string frameName, bool stripFrameExtension = true)
    {
        ArgumentNullException.ThrowIfNull(frameName);

        if (!stripFrameExtension) { return frameName; }

        string extension = KenneyArchivePath.GetExtension(frameName);

        if (extension.Length == 0 || !ImageExtensions.Contains(extension)) { return frameName; }

        string stripped = KenneyArchivePath.RemoveExtension(frameName);

        return string.IsNullOrWhiteSpace(stripped) ? frameName : stripped;
    }

    /// <summary>
    /// Materializes an image, sprite atlas or vector asset as a tilesheet and registers it.
    /// </summary>
    /// <param name="entry">The catalogued asset to materialize.</param>
    /// <param name="key">The asset's key, which is also the key the tilesheet is registered under unless <see cref="TilesheetMaterializeOptions.RegisterAs"/> names another one.</param>
    /// <param name="options">The grid, collision, frame-naming and rasterization options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The registered tilesheet, which is the one already registered under the key when there is one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a vector's raster size or scale is not positive.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not an image, a sprite atlas or an SVG vector.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a sprite atlas's sheet image cannot be found in the pack; the message and <see cref="FileNotFoundException.FileName"/> carry the path that could not be resolved.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes cannot be decoded as a picture.</exception>
    internal Tilesheet MaterializeTilesheet(
        KenneyAssetEntry entry, string key, TilesheetMaterializeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        //The kind is checked before the registry, so a kind this materializer cannot draw is always
        //  refused - even when something else is already registered under the key it was asked for
        if (!IsSupportedKind(entry.Kind))
        {
            throw new UnsupportedGameAssetException(entry.Kind, key);
        }

        string registryKey = string.IsNullOrWhiteSpace(options?.RegisterAs)
            ? key
            : options.RegisterAs;

        lock (_gate)
        {
            if (TilesheetRegistry.Instance.TryGet(registryKey, out Tilesheet? registered)
                && registered is not null)
            {
                return registered;
            }

            return entry.Kind switch
            {
                GameAssetKind.Image => MaterializeImage(entry, registryKey, options),
                GameAssetKind.SpriteAtlas => MaterializeSpriteAtlas(entry, registryKey, options),
                GameAssetKind.Vector => MaterializeVector(entry, registryKey, options),
                _ => throw new UnsupportedGameAssetException(entry.Kind, key),
            };
        }
    }

    private static bool IsSupportedKind(GameAssetKind kind) =>
        kind is GameAssetKind.Image or GameAssetKind.SpriteAtlas or GameAssetKind.Vector;

    private Tilesheet MaterializeImage(
        KenneyAssetEntry entry, string registryKey, TilesheetMaterializeOptions? options)
    {
        Tilesheet sheet = LoadSheet(entry, entry.Path, registryKey);

        try
        {
            ApplyWholeImageDefaultRegion(sheet, options);
            AddGridRegion(sheet, registryKey, options);
        }
        catch
        {
            Withdraw(sheet, registryKey);
            throw;
        }

        return sheet;
    }

    private Tilesheet MaterializeSpriteAtlas(
        KenneyAssetEntry entry, string registryKey, TilesheetMaterializeOptions? options)
    {
        SpriteAtlasDocument atlas = entry.SpriteAtlas
            ?? throw new InvalidDataException(
                $"The sprite atlas '{entry.Path}' in '{entry.Pack.SourcePath}' carries no parsed " +
                "document, so its frames are unknown.");

        Tilesheet sheet = LoadSheet(entry, ResolveAtlasImagePath(entry, atlas), registryKey);

        try
        {
            AddAtlasFrameRegions(sheet, registryKey, atlas, options);
        }
        catch
        {
            Withdraw(sheet, registryKey);
            throw;
        }

        return sheet;
    }

    private Tilesheet MaterializeVector(
        KenneyAssetEntry entry, string registryKey, TilesheetMaterializeOptions? options)
    {
        if (!string.Equals(entry.Extension, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedGameAssetException(entry.Kind, registryKey);
        }

        SKBitmap bitmap;

        using (Stream stream = entry.Open())
        {
            bitmap = SvgRasterizer.Rasterize(
                stream, options?.VectorRasterSize, options?.VectorScale, entry.Path);
        }

        Tilesheet sheet;

        try
        {
            //The tilesheet takes ownership of the bitmap and disposes it with itself
            sheet = TilesheetRegistry.Instance.LoadFromBitmap(registryKey, bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        try
        {
            ApplyWholeImageDefaultRegion(sheet, options);
        }
        catch
        {
            Withdraw(sheet, registryKey);
            throw;
        }

        return sheet;
    }

    private static Tilesheet LoadSheet(KenneyAssetEntry entry, string imagePath, string registryKey)
    {
        using Stream stream = entry.Pack.Archive.Open(imagePath);

        try
        {
            return TilesheetRegistry.Instance.LoadFromStream(registryKey, stream);
        }
        catch (ArgumentException ex)
        {
            //The engine reports an undecodable image as an ArgumentException on the stream; say which
            //  file it was, because a bundle holds hundreds of them
            throw new InvalidDataException(
                $"The image '{imagePath}' in '{entry.Pack.SourcePath}' could not be decoded.", ex);
        }
    }

    private static string ResolveAtlasImagePath(KenneyAssetEntry entry, SpriteAtlasDocument atlas)
    {
        IKenneyArchive archive = entry.Pack.Archive;

        if (atlas.ImagePath.Length > 0 && archive.HasEntry(atlas.ImagePath))
        {
            return atlas.ImagePath;
        }

        //The pack index resolved this path while cataloging, so reaching here means the pack changed
        //  underneath us - an extracted folder someone edited. Ask the resolver again rather than
        //  guessing, and fail with the path that could not be found.
        if (SpriteAtlasParser.TryResolveImagePath(atlas, archive, out string? resolved))
        {
            return resolved;
        }

        string unresolved = atlas.ImagePath.Length > 0 ? atlas.ImagePath : atlas.DeclaredImagePath;

        throw new FileNotFoundException(
            $"The sheet image of sprite atlas '{atlas.DocumentPath}' in '{entry.Pack.SourcePath}' " +
            $"could not be found. The document names '{atlas.DeclaredImagePath}'.",
            unresolved);
    }

    private static void ApplyWholeImageDefaultRegion(
        Tilesheet sheet, TilesheetMaterializeOptions? options)
    {
        TilesheetRegion region = sheet.DefaultRegion;

        //A freshly loaded sheet's default region covers the whole image but has no tile size, so it
        //  holds no tiles at all; one tile the size of the image is what makes sheet[0, 0] the picture
        region.TileSize = new Size(sheet.SkBitmap.Width, sheet.SkBitmap.Height);

        ApplyCollisionOptions(region, options);
    }

    private static void ApplyCollisionOptions(
        TilesheetRegion region, TilesheetMaterializeOptions? options)
    {
        if (options is null) { return; }

        region.CollisionType = options.CollisionType;

        if (options.CollisionAdjust is { } collisionAdjust)
        {
            region.CollisionAdjust = collisionAdjust;
        }
    }

    private void AddGridRegion(
        Tilesheet sheet, string registryKey, TilesheetMaterializeOptions? options)
    {
        if (options?.TileSize is not { } tileSize) { return; }

        if (tileSize.Width <= 0 || tileSize.Height <= 0)
        {
            AddWarning(
                registryKey,
                $"the tile size asked for ({tileSize.Width}x{tileSize.Height}) is not positive, so no " +
                $"'{GridRegionName}' region was added.");

            return;
        }

        TilesheetRegion grid = sheet.AddRegion(
            GridRegionName,
            new Rectangle(0, 0, sheet.SkBitmap.Width, sheet.SkBitmap.Height),
            tileSize,
            options.Padding,
            options.Margin,
            overhangPixels: null,
            options.CollisionAdjust,
            options.CollisionType);

        if (grid.Columns == 0 || grid.Rows == 0)
        {
            AddWarning(
                registryKey,
                $"the tile size asked for ({tileSize.Width}x{tileSize.Height}), with the padding and " +
                $"margin given, leaves no whole tile in the {sheet.SkBitmap.Width}x" +
                $"{sheet.SkBitmap.Height} image, so the '{GridRegionName}' region is empty.");
        }
    }

    private void AddAtlasFrameRegions(
        Tilesheet sheet,
        string registryKey,
        SpriteAtlasDocument atlas,
        TilesheetMaterializeOptions? options)
    {
        bool stripFrameExtension = options?.StripFrameExtension ?? true;
        Rectangle imageBounds = new(0, 0, sheet.SkBitmap.Width, sheet.SkBitmap.Height);

        foreach (SpriteAtlasFrame frame in atlas.Frames)
        {
            string name = RegionNameForFrame(frame.Name, stripFrameExtension);

            if (string.IsNullOrWhiteSpace(name))
            {
                AddWarning(registryKey, "an atlas frame has no usable name and was left out.");

                continue;
            }

            if (sheet.GetRegion(name) is not null)
            {
                AddWarning(
                    registryKey,
                    $"atlas frame '{frame.Name}' wants region name '{name}', which is already taken; " +
                    "the first frame of that name is the one kept.");

                continue;
            }

            Rectangle area = new(frame.X, frame.Y, frame.Width, frame.Height);

            if (!imageBounds.Contains(area))
            {
                AddWarning(
                    registryKey,
                    $"atlas frame '{frame.Name}' covers {area.Width}x{area.Height} at " +
                    $"({area.X}, {area.Y}), which falls outside the {imageBounds.Width}x" +
                    $"{imageBounds.Height} sheet image, so it was left out.");

                continue;
            }

            sheet.AddRegion(
                name,
                area,
                new Size(frame.Width, frame.Height),
                tilePadding: null,
                regionMargin: null,
                overhangPixels: null,
                options?.CollisionAdjust,
                options?.CollisionType ?? TileCollisionType.None);
        }
    }

    private static void Withdraw(Tilesheet sheet, string registryKey)
    {
        //Half a tilesheet is worse than none: leave nothing registered under the key the caller asked
        //  for when building its regions failed
        TilesheetRegistry.Instance.Remove(registryKey, dispose: false);
        sheet.Dispose();
    }

    private void AddWarning(string registryKey, string message)
    {
        lock (_gate)
        {
            _warnings.Add($"{registryKey}: {message}");
        }
    }
}
