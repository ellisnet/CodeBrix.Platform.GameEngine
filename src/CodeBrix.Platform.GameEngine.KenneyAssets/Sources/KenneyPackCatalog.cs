using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Turns the file listing of one pack into its asset catalog: what each file is, which files are not
/// assets of their own, and the key each asset answers to.
/// </summary>
/// <remarks>
/// <para>
/// THE KEY SCHEME. An asset's key is <c>&lt;providerId&gt;:&lt;pack-slug&gt;/&lt;path&gt;</c> and keys
/// are compared case-insensitively. A materializable asset drops its extension, so
/// <c>PNG/Double/ballBlue.png</c> is addressed as <c>PNG/Double/ballBlue</c>. Everything listed for
/// discovery only keeps its extension, because a model ships as several files that would otherwise
/// share one key: <c>character-a.obj</c> and <c>character-a.mtl</c>. Should two materializable assets
/// of one pack still want the same key, the first of them - ordered by asset kind, then by path - keeps
/// the short key and the others keep their extension, which makes the outcome the same on every run.
/// </para>
/// <para>
/// WHAT IS NOT AN ASSET. A sprite atlas is one asset, keyed by its XML document; the sheet image it
/// cuts frames from is not listed separately, because addressing it on its own would hand back a
/// tilesheet with no frames. A tile set's GRID image is likewise reached through the map that uses it,
/// and a tile set document is not an asset at all. An XML file that is not an atlas, or whose sheet
/// image is missing, stays an ordinary document. The one-image-per-tile pictures of an
/// image-collection tile set ARE listed, because they are ordinary sprites and no map can be imported
/// from such a tile set.
/// </para>
/// <para>
/// WHAT IS READ. The directory listing, plus the atlas, tile set and map documents, which are a few
/// kilobytes each. Nothing else is opened: a Kenney collection holds thousands of models, and reading
/// them to catalog them would make registering a source as expensive as loading the pack.
/// </para>
/// </remarks>
internal sealed class KenneyPackCatalog
{
    private KenneyPackCatalog(IReadOnlyList<KenneyAssetEntry> entries, IReadOnlyList<string> warnings)
    {
        Entries = entries;
        Warnings = warnings;
    }

    /// <summary>
    /// Gets the pack's assets, in the archive's own order, which is by path and case-insensitive.
    /// </summary>
    public IReadOnlyList<KenneyAssetEntry> Entries { get; }

    /// <summary>
    /// Gets the messages describing anything that could not be catalogued exactly.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Catalogs a pack.
    /// </summary>
    /// <param name="pack">The pack to catalog. Its archive and identity must already be set.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pack"/> is null.</exception>
    public static KenneyPackCatalog Build(KenneyPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        IKenneyArchive archive = pack.Archive;
        List<string> warnings = [];

        //Files that are part of another asset rather than assets of their own
        HashSet<string> coveredPaths = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SpriteAtlasDocument> atlases =
            ReadAtlases(archive, warnings, coveredPaths);
        ReadTilesets(archive, warnings, coveredPaths);
        Dictionary<string, TiledMapDocument> maps = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> mapErrors = new(StringComparer.OrdinalIgnoreCase);
        ReadMaps(archive, warnings, coveredPaths, maps, mapErrors);

        List<Candidate> candidates = [];
        foreach (KenneyArchiveEntry file in archive.Entries)
        {
            //A tile set document describes tiles of a map; it is never addressed on its own
            if (file.Extension.Equals("tsx", StringComparison.OrdinalIgnoreCase)) { continue; }
            if (coveredPaths.Contains(file.Path)) { continue; }

            if (atlases.TryGetValue(file.Path, out SpriteAtlasDocument? atlas))
            {
                candidates.Add(new Candidate(file, GameAssetKind.SpriteAtlas, true, atlas, null, null));
                continue;
            }

            GameAssetKind kind = AssetClassifier.Classify(file.Path);
            bool materializable = AssetClassifier.IsMaterializable(kind, file.Extension);
            maps.TryGetValue(file.Path, out TiledMapDocument? map);
            mapErrors.TryGetValue(file.Path, out string? mapError);
            candidates.Add(new Candidate(file, kind, materializable, null, map, mapError));
        }

        HashSet<string> keepExtension = ResolveKeyCollisions(candidates);

        List<KenneyAssetEntry> entries = [];
        foreach (Candidate candidate in candidates)
        {
            bool dropExtension = candidate.IsMaterializable
                && !keepExtension.Contains(candidate.File.Path);
            string keySegment = dropExtension
                ? KenneyArchivePath.RemoveExtension(candidate.File.Path)
                : candidate.File.Path;

            entries.Add(new KenneyAssetEntry(
                pack,
                candidate.File,
                candidate.Kind,
                keySegment,
                candidate.IsMaterializable,
                candidate.Atlas,
                candidate.Map,
                candidate.MapError));
        }

        return new KenneyPackCatalog(entries, warnings);
    }

    //Every XML document that turns out to be a sprite atlas whose sheet image is in the pack
    private static Dictionary<string, SpriteAtlasDocument> ReadAtlases(
        IKenneyArchive archive, List<string> warnings, HashSet<string> coveredPaths)
    {
        Dictionary<string, SpriteAtlasDocument> atlases = new(StringComparer.OrdinalIgnoreCase);

        foreach (KenneyArchiveEntry file in archive.Entries
            .Where(e => e.Extension.Equals("xml", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadText(archive, file.Path, warnings, out string? xml)) { continue; }
            if (!SpriteAtlasParser.TryParse(xml, file.Path, out SpriteAtlasDocument? atlas)) { continue; }

            if (!SpriteAtlasParser.TryResolveImagePath(atlas, archive, out string? imagePath))
            {
                warnings.Add(
                    $"Sprite atlas '{file.Path}' names the sheet image '{atlas.DeclaredImagePath}', which is " +
                    "not in this pack; it is listed as a document instead of an atlas.");
                continue;
            }

            atlases[file.Path] = atlas with { ImagePath = imagePath };
            coveredPaths.Add(imagePath);
        }

        return atlases;
    }

    //Tile set documents, read for the images they claim so those images are not listed twice
    private static void ReadTilesets(
        IKenneyArchive archive, List<string> warnings, HashSet<string> coveredPaths)
    {
        foreach (KenneyArchiveEntry file in archive.Entries
            .Where(e => e.Extension.Equals("tsx", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadText(archive, file.Path, warnings, out string? xml)) { continue; }
            if (!TiledMapParser.TryParseTileset(
                xml, file.Path, out TiledTilesetDocument? tileset, out string? error))
            {
                warnings.Add(error);
                continue;
            }

            CoverTilesetImages(archive, tileset, file.Path, warnings, coveredPaths);
        }
    }

    //Tile maps, parsed once so a caller knows a map's size before loading it
    private static void ReadMaps(
        IKenneyArchive archive,
        List<string> warnings,
        HashSet<string> coveredPaths,
        Dictionary<string, TiledMapDocument> maps,
        Dictionary<string, string> mapErrors)
    {
        foreach (KenneyArchiveEntry file in archive.Entries
            .Where(e => e.Extension.Equals("tmx", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadText(archive, file.Path, warnings, out string? xml)) { continue; }
            if (!TiledMapParser.TryParseMap(xml, file.Path, out TiledMapDocument? map, out string? error))
            {
                mapErrors[file.Path] = error;
                warnings.Add(error);
                continue;
            }

            maps[file.Path] = map;
            foreach (string warning in map.Warnings) { warnings.Add(warning); }

            //An inline tile set names its image relative to the map itself
            foreach (TiledTilesetReference reference in map.Tilesets)
            {
                if (reference.Inline is not null)
                {
                    CoverTilesetImages(archive, reference.Inline, file.Path, warnings, coveredPaths);
                }
            }
        }
    }

    private static void CoverTilesetImages(
        IKenneyArchive archive,
        TiledTilesetDocument tileset,
        string documentPath,
        List<string> warnings,
        HashSet<string> coveredPaths)
    {
        if (tileset.ImagePath.Length > 0)
        {
            string? resolved = archive.ResolveDependencyPath(documentPath, tileset.ImagePath);
            if (resolved is null)
            {
                warnings.Add(
                    $"Tile set '{documentPath}' names the image '{tileset.ImagePath}', which is not in this " +
                    "pack; a map using it cannot be imported.");
            }
            else
            {
                coveredPaths.Add(resolved);
            }
        }

        //The one-image-per-tile pictures of an image-collection tile set are NOT covered. They are
        //  ordinary sprites, and the importer refuses such a tile set anyway, so hiding them would
        //  make a whole pack unaddressable: three packs of Kenney's own collection put every sprite
        //  they ship into one image-collection tile set.
    }

    //The deterministic tie-break: within one key, the first candidate by kind then path keeps the
    //  short key and every other materializable candidate keeps its extension
    private static HashSet<string> ResolveKeyCollisions(List<Candidate> candidates)
    {
        HashSet<string> keepExtension = new(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, Candidate> group in candidates
            .GroupBy(c => c.KeySegment, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            bool first = true;
            foreach (Candidate candidate in group
                .OrderBy(c => (int)c.Kind)
                .ThenBy(c => c.File.Path, StringComparer.Ordinal))
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                keepExtension.Add(candidate.File.Path);
            }
        }

        return keepExtension;
    }

    private static bool TryReadText(
        IKenneyArchive archive, string path, List<string> warnings, out string? text)
    {
        try
        {
            text = archive.ReadText(path);
            return true;
        }
        catch (IOException exception)
        {
            warnings.Add($"'{path}' could not be read: {exception.Message}");
            text = null;
            return false;
        }
    }

    //One asset before its key is final; KeySegment is the key it would take on its own
    private sealed record Candidate(
        KenneyArchiveEntry File,
        GameAssetKind Kind,
        bool IsMaterializable,
        SpriteAtlasDocument? Atlas,
        TiledMapDocument? Map,
        string? MapError)
    {
        public string KeySegment => IsMaterializable
            ? KenneyArchivePath.RemoveExtension(File.Path)
            : File.Path;
    }
}
