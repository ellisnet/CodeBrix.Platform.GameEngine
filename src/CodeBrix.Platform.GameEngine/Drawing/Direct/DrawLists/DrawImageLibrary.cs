using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// Looks pictures up by asset key and frame name for <see cref="DrawList"/> image commands, and remembers each one,
/// so a list built every frame never loads anything twice.
/// </summary>
/// <remarks>
/// <para>
/// An asset key names a tilesheet: first a sheet in <see cref="TilesheetRegistry"/> under that name, otherwise an
/// asset from a registered asset provider (<see cref="GameAssetProviderRegistry.LoadTilesheet"/>, for example a
/// Kenney atlas or a loose picture). A frame name is a region of that sheet (an atlas frame such as
/// <c>"playerShip1_blue.png"</c>) and the picture is that region's first tile; no frame name means the sheet's
/// default region, which is the whole picture of a loose image. <see cref="AddTilesheet"/> and <see cref="AddImage"/>
/// register pictures the game loaded itself.
/// </para>
/// <para>
/// A key or frame that cannot be found draws nothing: it is listed in <see cref="Missing"/> and logged ONCE as a
/// warning (with the closest real names when there are any), never thrown in the middle of a frame. Resolve every
/// picture at load time with <see cref="Preload"/> to find spelling mistakes before the first frame.
/// </para>
/// <para>
/// Not thread-safe: use it on the thread that builds the lists (the engine thread), including loading. The pictures
/// it returns stay owned by their tilesheets; the library never disposes anything.
/// </para>
/// </remarks>
public sealed class DrawImageLibrary
{
    private readonly Dictionary<string, Tilesheet?> _sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string AssetKey, string? FrameName), SKImage?> _images = new();
    private readonly List<string> _missing = new();
    private readonly List<string> _warnings = new();

    /// <summary>Gets the number of pictures found so far.</summary>
    public int Count => _images.Count(pair => pair.Value != null);

    /// <summary>
    /// Gets every asset key (<c>"key"</c>) and frame (<c>"key / frame"</c>) that could not be found, in the order they
    /// were first asked for.
    /// </summary>
    public IReadOnlyList<string> Missing => _missing;

    //The warning texts logged so far, for tests
    internal IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Gets a picture, loading and remembering its tilesheet the first time the asset key is used.
    /// </summary>
    /// <param name="assetKey">The tilesheet name or asset key.</param>
    /// <param name="frameName">The frame (region) name, or null for the sheet's default region (a whole picture).</param>
    /// <returns>The picture, or <see langword="null"/> when the key or the frame cannot be found.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assetKey"/> is null or whitespace.</exception>
    public SKImage? Get(string assetKey, string? frameName = null)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
            throw new ArgumentException("An asset key is required.", nameof(assetKey));

        var key = (assetKey, frameName);
        if (_images.TryGetValue(key, out var image))
            return image;

        image = Resolve(assetKey, frameName);
        _images[key] = image;
        return image;
    }

    /// <summary>
    /// Resolves frames of one tilesheet ahead of time (at load), so spelling mistakes show up in
    /// <see cref="Missing"/> and in the log before the first frame is drawn.
    /// </summary>
    /// <param name="assetKey">The tilesheet name or asset key.</param>
    /// <param name="frameNames">The frame names; an empty sequence resolves the sheet's default region.</param>
    /// <returns>How many of the requested pictures were found.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assetKey"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="frameNames"/> is null.</exception>
    public int Preload(string assetKey, IEnumerable<string> frameNames)
    {
        ArgumentNullException.ThrowIfNull(frameNames);

        var names = frameNames.ToList();
        if (names.Count == 0)
            return Get(assetKey) != null ? 1 : 0;

        var found = 0;
        foreach (var name in names)
        {
            if (Get(assetKey, name) != null)
                found++;
        }

        return found;
    }

    /// <summary>
    /// Registers a tilesheet the game loaded itself under an asset key; its frames are looked up on first use. Any
    /// pictures already remembered for that key are forgotten.
    /// </summary>
    /// <param name="assetKey">The key image commands will use.</param>
    /// <param name="sheet">The tilesheet. It stays owned by the caller and must outlive every list that uses it.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assetKey"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sheet"/> is null.</exception>
    public void AddTilesheet(string assetKey, Tilesheet sheet)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
            throw new ArgumentException("An asset key is required.", nameof(assetKey));
        ArgumentNullException.ThrowIfNull(sheet);

        _sheets[assetKey] = sheet;
        foreach (var key in _images.Keys.Where(key => key.AssetKey == assetKey).ToList())
            _images.Remove(key);
    }

    /// <summary>Registers one picture the game made or loaded itself under an asset key and optional frame name.</summary>
    /// <param name="assetKey">The key image commands will use.</param>
    /// <param name="frameName">The frame name, or null.</param>
    /// <param name="image">The picture. It stays owned by the caller and must outlive every list that uses it.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assetKey"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
    public void AddImage(string assetKey, string? frameName, SKImage image)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
            throw new ArgumentException("An asset key is required.", nameof(assetKey));
        ArgumentNullException.ThrowIfNull(image);

        _images[(assetKey, frameName)] = image;
    }

    private SKImage? Resolve(string assetKey, string? frameName)
    {
        var sheet = GetSheet(assetKey);
        if (sheet == null)
            return null;

        var regionName = frameName ?? TilesheetRegion.DefaultRegionName;
        var region = sheet.GetRegion(regionName);
        var image = region?.GetImage(0, 0);
        if (image != null)
            return image;

        var message = region == null
            ? $"Draw list picture not found: no frame '{regionName}' in '{assetKey}'." +
              KeySuggestions.DidYouMean(regionName, () => sheet.Regions.Select(r => r.Name))
            : $"Draw list picture not found: frame '{regionName}' of '{assetKey}' has no picture (is its tile size set?).";
        ReportMissing($"{assetKey} / {regionName}", message);
        return null;
    }

    private Tilesheet? GetSheet(string assetKey)
    {
        if (_sheets.TryGetValue(assetKey, out var sheet))
            return sheet;

        sheet = LoadSheet(assetKey);
        _sheets[assetKey] = sheet;
        return sheet;
    }

    private Tilesheet? LoadSheet(string assetKey)
    {
        if (TilesheetRegistry.Instance.TryGet(assetKey, out var registered) && registered != null)
            return registered;

        try
        {
            return GameAssetProviderRegistry.Instance.LoadTilesheet(assetKey);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or UnsupportedGameAssetException or ArgumentException)
        {
            ReportMissing(assetKey, $"Draw list picture not found: {ex.Message}");
            return null;
        }
    }

    private void ReportMissing(string name, string message)
    {
        _missing.Add(name);
        _warnings.Add(message);
        Engine.Logger.LogWarning("{Message}", message);
    }
}
