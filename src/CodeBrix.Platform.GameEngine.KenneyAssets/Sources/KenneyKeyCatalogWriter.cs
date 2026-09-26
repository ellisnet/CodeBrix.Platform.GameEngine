using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Writes the readable key catalog: every key grouped by pack, with its kind, and the frame names
/// inside each sprite atlas.
/// </summary>
internal static class KenneyKeyCatalogWriter
{
    /// <summary>
    /// Writes the catalog.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="providerId">The provider identifier, for the heading.</param>
    /// <param name="packs">The registered packs, in registration order.</param>
    /// <param name="descriptors">The keys to list, in index order.</param>
    /// <param name="index">The index the descriptors came from, for the atlas documents.</param>
    /// <param name="filtered">Whether a query narrowed the list, for the heading.</param>
    public static void Write(
        TextWriter writer,
        string providerId,
        IReadOnlyList<KenneyPackSummary> packs,
        IReadOnlyList<GameAssetDescriptor> descriptors,
        KenneyPackIndex index,
        bool filtered)
    {
        Dictionary<string, List<GameAssetDescriptor>> byPack = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameAssetDescriptor descriptor in descriptors)
        {
            if (!byPack.TryGetValue(descriptor.Pack, out List<GameAssetDescriptor>? list))
            {
                list = [];
                byPack[descriptor.Pack] = list;
            }

            list.Add(descriptor);
        }

        List<KenneyPackSummary> listedPacks = packs.Where(pack => byPack.ContainsKey(pack.Slug)).ToList();

        writer.WriteLine(
            $"Asset keys of provider '{providerId}': {Count(listedPacks.Count)} pack(s), " +
            $"{Count(descriptors.Count)} key(s){(filtered ? " matching the query" : string.Empty)}.");

        foreach (KenneyPackSummary pack in listedPacks)
        {
            List<GameAssetDescriptor> keys = byPack[pack.Slug];

            writer.WriteLine();
            writer.WriteLine($"{pack.Slug} - {pack.LicenseTitle?.Trim() ?? pack.DisplayName} - {Count(keys.Count)} key(s)");

            foreach (GameAssetDescriptor descriptor in keys)
            {
                index.TryGetEntry(descriptor.Key, out KenneyAssetEntry? entry);
                SpriteAtlasDocument? atlas = entry?.SpriteAtlas;
                IReadOnlyList<string> frames = atlas is null ? [] : FrameNames(atlas);

                writer.WriteLine($"  {descriptor.Key}  [{Describe(descriptor, entry, atlas, frames.Count)}]");

                foreach (string frame in frames)
                {
                    writer.WriteLine($"      {frame}");
                }
            }
        }
    }

    /// <summary>
    /// Gets the region names a sprite atlas's frames get when the atlas is materialized with the
    /// default options: the frame names with an image extension removed, blanks left out, the first
    /// of two equal names kept.
    /// </summary>
    /// <param name="atlas">The atlas document.</param>
    /// <returns>The region names, in document order.</returns>
    public static IReadOnlyList<string> FrameNames(SpriteAtlasDocument atlas)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (SpriteAtlasFrame frame in atlas.Frames)
        {
            string name = TilesheetMaterializer.RegionNameForFrame(frame.Name);

            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name)) { names.Add(name); }
        }

        return names;
    }

    private static string Describe(
        GameAssetDescriptor descriptor, KenneyAssetEntry? entry, SpriteAtlasDocument? atlas, int frameCount)
    {
        string kind = descriptor.Kind.ToString();

        if (atlas is not null) { return $"{kind}, {Count(frameCount)} frame(s)"; }

        return entry is { IsMaterializable: false } ? $"{kind}, listed only" : kind;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
