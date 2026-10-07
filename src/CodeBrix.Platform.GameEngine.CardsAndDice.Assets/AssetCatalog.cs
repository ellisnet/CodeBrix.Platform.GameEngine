using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Assets;

/// <summary>Offline SVG and sound catalog. Opening an asset never extracts a file.</summary>
public static class AssetCatalog
{
    private static readonly Assembly Assembly = typeof(AssetCatalog).Assembly;
    private static readonly Lazy<IReadOnlyList<AssetEntry>> Catalog = new(() =>
    {
        using var stream = Assembly.GetManifestResourceStream("cardsdice/catalog.json")
            ?? throw new InvalidOperationException("Embedded catalog missing.");
        return Array.AsReadOnly(JsonSerializer.Deserialize(stream, AssetCatalogJsonContext.Default.AssetEntryArray)!);
    });
    /// <summary>All embedded assets, sorted by key.</summary>
    public static IReadOnlyList<AssetEntry> All => Catalog.Value;
    /// <summary>Finds assets by category, filename, or readable name.</summary>
    public static IEnumerable<AssetEntry> Search(string query) => All.Where(a =>
        a.Key.Contains(query ?? "", StringComparison.OrdinalIgnoreCase) || a.Name.Contains(query ?? "", StringComparison.OrdinalIgnoreCase));
    /// <summary>Opens an independent read-only stream. The caller disposes it.</summary>
    public static Stream Open(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Assembly.GetManifestResourceStream("cardsdice/" + key)
            ?? throw new KeyNotFoundException($"No CardsAndDice asset named '{key}'.");
    }
    /// <summary>Reads a bundled SVG as text.</summary>
    public static string ReadSvg(string key)
    {
        if (!key.EndsWith(".svg", StringComparison.Ordinal)) throw new ArgumentException("Expected an SVG key.", nameof(key));
        using var stream = Open(key);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
