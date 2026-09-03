using SkiaSharp;
using CodeBrix.Platform.GameEngine.Assets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets; //was previously: Gondwana.Drawing.Tilesheets;
/// <summary>
/// Provides factory methods for creating <see cref="Tilesheet"/> instances from various sources.
/// </summary>
internal static class TilesheetFactory
{
    /// <summary>
    /// Creates a tilesheet from an existing SkiaSharp bitmap.
    /// </summary>
    /// <param name="name">The name to assign to the tilesheet.</param>
    /// <param name="bitmap">The SkiaSharp bitmap containing the tilesheet image.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance.</returns>
    internal static Tilesheet FromBitmap(string name, SKBitmap bitmap) => new Tilesheet(name, bitmap);

    /// <summary>
    /// Creates a tilesheet by loading an image from a stream.
    /// </summary>
    /// <param name="name">The name to assign to the tilesheet.</param>
    /// <param name="stream">The stream containing the image data.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance.</returns>
    internal static Tilesheet FromStream(string name, Stream stream) => new Tilesheet(name, stream);

    /// <summary>
    /// Creates a tilesheet by loading an image from a file.
    /// </summary>
    /// <param name="name">The name to assign to the tilesheet.</param>
    /// <param name="imageFilePath">The path to the image file.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance.</returns>
    internal static Tilesheet FromImageFile(string name, string imageFilePath) => new Tilesheet(name, imageFilePath);

    /// <summary>
    /// Creates a tilesheet by loading an image from an assets file.
    /// </summary>
    /// <param name="assetsFile">The assets file containing the tilesheet image.</param>
    /// <param name="entryName">The name of the asset entry within the assets file.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance.</returns>
    internal static Tilesheet FromAssetsFile(AssetsFile assetsFile, string entryName) => new Tilesheet(assetsFile, entryName);

    /// <summary>
    /// Creates a tilesheet by loading and parsing a GTS (Game Tilesheet) definition file.
    /// </summary>
    /// <param name="gtsPath">The path to the GTS definition file.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance configured according to the definition file.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="gtsPath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the GTS file does not exist.</exception>
    internal static Tilesheet FromDefinitionFile(string gtsPath)
    {
        if (string.IsNullOrWhiteSpace(gtsPath))
            throw new ArgumentException("GTS path must be a non-empty string.", nameof(gtsPath));

        var fullPath = Path.GetFullPath(gtsPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"GTS file not found: {fullPath}", fullPath);

        var definition = TilesheetDefinitionSerializer.Load(fullPath);

        var baseDirectory = Path.GetDirectoryName(fullPath);

        return FromDefinition(definition, baseDirectory);
    }

    /// <summary>
    /// Creates a tilesheet from a tilesheet definition object.
    /// </summary>
    /// <param name="definition">The tilesheet definition containing image source, regions, and masking settings.</param>
    /// <param name="baseDirectory">The base directory for resolving relative paths. If null, paths must be absolute.</param>
    /// <param name="defaultAssetsFile">The default assets file to use when the definition specifies an asset entry without an assets file path.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance configured according to the definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
    internal static Tilesheet FromDefinition(
        TilesheetDefinition definition,
        string? baseDirectory = null,
        AssetsFile? defaultAssetsFile = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tilesheet = CreateTilesheet(
            definition,
            addDefaultRegion: false,
            baseDirectory,
            defaultAssetsFile);

        foreach (var region in definition.Regions)
        {
            var runtimeRegion = tilesheet.AddRegion(
                region.Name,
                region.Area,
                region.TileSize,
                region.TilePadding,
                region.RegionMargin,
                region.Overhang,
                region.CollisionAdjust,
                region.CollisionType);

            // A missing frame value inherits the region default. A present value is an explicit
            // override, even when it currently equals that default.
            foreach (var frame in region.Frames ?? [])
            {
                if (frame.CollisionAdjust is { } adjust)
                    runtimeRegion.SetFrameCollisionAdjust(frame.XTile, frame.YTile, adjust);

                if (frame.CollisionType is { } collisionType)
                    runtimeRegion.SetFrameCollisionType(frame.XTile, frame.YTile, collisionType);
            }
        }

        if (definition.Mask is not null)
        {
            tilesheet.ApplyMask(
                new SKColor(
                    definition.Mask.Red,
                    definition.Mask.Green,
                    definition.Mask.Blue,
                    definition.Mask.Alpha),
                definition.Mask.Tolerance);
        }
        else if (definition.PremultiplyAlpha)
        {
            tilesheet.ApplyPremultiplyAlpha();
        }

        return tilesheet;
    }

    /// <summary>
    /// Creates a tilesheet by loading a GTS definition from an assets file.
    /// </summary>
    /// <param name="assetsFile">The assets file containing the GTS definition.</param>
    /// <param name="gtsEntryName">The name of the GTS definition entry within the assets file.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance configured according to the definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assetsFile"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="gtsEntryName"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the GTS definition asset cannot be found in the assets file.</exception>
    internal static Tilesheet FromDefinitionAsset(
        AssetsFile assetsFile,
        string gtsEntryName)
    {
        if (assetsFile is null)
            throw new ArgumentNullException(nameof(assetsFile));

        if (string.IsNullOrWhiteSpace(gtsEntryName))
            throw new ArgumentException("GTS asset entry name must be a non-empty string.", nameof(gtsEntryName));

        using var stream = assetsFile.Get(
            AssetTypes.TilesheetDefinition,
            gtsEntryName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Tilesheet definition asset '{gtsEntryName}' could not be found in AssetsFile '{assetsFile.FilePath}'.");
        }

        var definition = TilesheetDefinitionSerializer.Load(stream);

        if (!string.IsNullOrWhiteSpace(assetsFile.FilePath))
        {
            definition.Source = TilesheetDefinitionSource.PackedDefinitionFile(
                assetsFile.FilePath,
                gtsEntryName);
        }
        var baseDirectory = string.IsNullOrWhiteSpace(assetsFile.FilePath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(assetsFile.FilePath));

        return FromDefinition(
            definition,
            baseDirectory,
            defaultAssetsFile: assetsFile);
    }

    #region private methods

    /// <summary>
    /// Creates a tilesheet from a definition, resolving the image source from either a file path or assets file.
    /// </summary>
    /// <param name="definition">The tilesheet definition.</param>
    /// <param name="addDefaultRegion">Whether to add a default region covering the entire tilesheet.</param>
    /// <param name="baseDirectory">The base directory for resolving relative paths.</param>
    /// <param name="defaultAssetsFile">The default assets file to use when the definition specifies an asset entry without an assets file path.</param>
    /// <returns>A new <see cref="Tilesheet"/> instance with the image loaded but no regions defined yet.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the definition does not specify a valid image source.</exception>
    private static Tilesheet CreateTilesheet(
        TilesheetDefinition definition,
        bool addDefaultRegion,
        string? baseDirectory = null,
        AssetsFile? defaultAssetsFile = null)
    {
        var image = definition.Image
            ?? throw new InvalidOperationException(
                "TilesheetDefinition must specify an image source.");

        ValidateImageDefinition(image, defaultAssetsFile);

        if (!string.IsNullOrWhiteSpace(image.FilePath))
        {
            var imagePath = ResolvePath(image.FilePath, baseDirectory);

            return new Tilesheet(
                definition.Name,
                imagePath,
                addDefaultRegion);
        }

        AssetsFile assetsFile;

        if (!string.IsNullOrWhiteSpace(image.AssetsFilePath))
        {
            var assetsPath = ResolvePath(image.AssetsFilePath, baseDirectory);
            assetsFile = LoadExistingAssetsFile(assetsPath);
        }
        else
        {
            assetsFile = defaultAssetsFile!;
        }

        var tilesheet = new Tilesheet(
            assetsFile,
            image.AssetEntryName!,
            addDefaultRegion);

        // The asset entry identifies the image; it is not necessarily the logical
        // name assigned to the tilesheet by the GTS definition.
        tilesheet.Name = definition.Name;

        return tilesheet;
    }

    /// <summary>
    /// Validates that a tilesheet definition's image source is unambiguous and complete.
    /// </summary>
    /// <param name="image">The image definition to validate.</param>
    /// <param name="defaultAssetsFile">
    /// The default assets file supplied by the caller, used when the definition names an asset
    /// entry without naming the assets file that holds it.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the definition combines a file path with an asset source, names an assets file
    /// without an entry name, names no image source at all, or names an asset entry with neither an
    /// assets file path nor a default assets file.
    /// </exception>
    private static void ValidateImageDefinition(
        TilesheetImageDefinition image,
        AssetsFile? defaultAssetsFile)
    {
        var hasFilePath = !string.IsNullOrWhiteSpace(image.FilePath);
        var hasAssetsFilePath = !string.IsNullOrWhiteSpace(image.AssetsFilePath);
        var hasAssetEntryName = !string.IsNullOrWhiteSpace(image.AssetEntryName);

        if (hasFilePath && (hasAssetsFilePath || hasAssetEntryName))
        {
            throw new InvalidOperationException(
                "TilesheetDefinition image source is ambiguous. Image.FilePath cannot be combined with Image.AssetsFilePath or Image.AssetEntryName.");
        }

        if (hasAssetsFilePath && !hasAssetEntryName)
        {
            throw new InvalidOperationException(
                "TilesheetDefinition image specifies an AssetsFilePath but does not specify an AssetEntryName.");
        }

        if (hasFilePath)
            return;

        if (!hasAssetEntryName)
        {
            throw new InvalidOperationException(
                "TilesheetDefinition must specify either Image.FilePath or Image.AssetEntryName.");
        }

        if (!hasAssetsFilePath && defaultAssetsFile is null)
        {
            throw new InvalidOperationException(
                "TilesheetDefinition image specifies an AssetEntryName but does not specify an AssetsFilePath, and no default AssetsFile was provided.");
        }
    }

    /// <summary>
    /// Loads an existing assets file from the specified path.
    /// </summary>
    /// <param name="path">The path to the assets file.</param>
    /// <returns>The loaded <see cref="AssetsFile"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the assets file does not exist.</exception>
    private static AssetsFile LoadExistingAssetsFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Assets file path must be a non-empty string.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Assets file not found: {fullPath}",
                fullPath);

        return AssetsFile.LoadOrCreate(fullPath);
    }

    /// <summary>
    /// Resolves a path by combining it with a base directory if it is relative.
    /// </summary>
    /// <param name="path">The path to resolve, which may be relative or absolute.</param>
    /// <param name="baseDirectory">The base directory to use for resolving relative paths. If null or empty, the path is returned as-is.</param>
    /// <returns>The resolved absolute path if the path is relative and a base directory is provided; otherwise, the original path.</returns>
    private static string ResolvePath(string path, string? baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;

        if (string.IsNullOrWhiteSpace(baseDirectory))
            return path;

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    #endregion
}
