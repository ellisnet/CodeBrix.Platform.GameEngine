using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// One Kenney asset pack: an archive, the identity its licence file gives it, and its catalog of
/// assets.
/// </summary>
/// <remarks>
/// A pack does not own its archive. One archive can back several packs - a folder holding a collection
/// of packs gives each child pack its own archive, while a zip bundle is always one pack - so the
/// <see cref="KenneyAssetSource"/> that opened the archives is what disposes them.
/// </remarks>
internal sealed class KenneyPack
{
    /// <summary>
    /// The name of the file at the root of a Kenney pack that names the pack and states its licence.
    /// Matched case-insensitively.
    /// </summary>
    public const string LicenseFileName = "License.txt";

    private KenneyPack(
        IKenneyArchive archive,
        string slug,
        string displayName,
        string? version,
        string? licenseTitle,
        string? licenseText,
        string? licensePath)
    {
        Archive = archive;
        Slug = slug;
        DisplayName = displayName;
        Version = version;
        LicenseTitle = licenseTitle;
        LicenseText = licenseText;
        LicensePath = licensePath;

        KenneyPackCatalog catalog = KenneyPackCatalog.Build(this);
        Entries = catalog.Entries;
        Warnings = catalog.Warnings;
    }

    /// <summary>
    /// Reads a pack's identity from its archive and catalogs everything in it.
    /// </summary>
    /// <param name="archive">The archive holding the pack. The pack does not dispose it.</param>
    /// <param name="fallbackName">
    /// The name to prettify into a display name when the pack has no licence file, or
    /// <see langword="null"/> to use the archive's own file or folder name.
    /// </param>
    /// <returns>The pack, with its catalog already built.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="archive"/> is null.</exception>
    public static KenneyPack Create(IKenneyArchive archive, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(archive);

        string? licensePath = archive.Entries
            .FirstOrDefault(e => e.Folder.Length == 0
                && e.FileName.Equals(LicenseFileName, StringComparison.OrdinalIgnoreCase))
            ?.Path;

        string? licenseText = null;
        if (licensePath is not null)
        {
            try
            {
                licenseText = archive.ReadText(licensePath);
            }
            catch (IOException)
            {
                //An unreadable licence file costs the pack its title, not its catalog
                licenseText = null;
            }
        }

        string displayName;
        string? version = null;
        string? licenseTitle = null;
        if (KenneyNames.TryParseLicenseTitle(licenseText, out string? title, out string? parsedVersion))
        {
            displayName = title;
            version = parsedVersion;
            licenseTitle = FirstContentLine(licenseText);
        }
        else
        {
            displayName = KenneyNames.PrettifyBundleFileName(fallbackName ?? archive.SourcePath);
            if (displayName.Length == 0) { displayName = KenneyNames.FallbackSlug; }
        }

        return new KenneyPack(
            archive, KenneyNames.Slugify(displayName), displayName, version, licenseTitle, licenseText,
            licensePath);
    }

    /// <summary>
    /// Gets the archive the pack's files are read from.
    /// </summary>
    public IKenneyArchive Archive { get; }

    /// <summary>
    /// Gets the lower-case slug that namespaces the pack's asset keys. A
    /// <see cref="KenneyPackIndex"/> may change it to keep it unique among the packs of one provider.
    /// </summary>
    public string Slug { get; private set; }

    /// <summary>
    /// Gets the pack's display name: its licence title when it has one, otherwise its file or folder
    /// name prettified.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the pack version its licence file states, or <see langword="null"/> when it states none.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the first line of the pack's licence file, which names the pack and its version, or
    /// <see langword="null"/> when the pack has no licence file.
    /// </summary>
    public string? LicenseTitle { get; }

    /// <summary>
    /// Gets the full text of the pack's licence file, or <see langword="null"/> when it has none.
    /// </summary>
    public string? LicenseText { get; }

    /// <summary>
    /// Gets the archive path of the pack's licence file, or <see langword="null"/> when it has none.
    /// </summary>
    public string? LicensePath { get; }

    /// <summary>
    /// Gets the path of the zip file or folder the pack was read from.
    /// </summary>
    public string SourcePath => Archive.SourcePath;

    /// <summary>
    /// Gets the pack's assets, ordered by path, compared case-insensitively.
    /// </summary>
    public IReadOnlyList<KenneyAssetEntry> Entries { get; }

    /// <summary>
    /// Gets the messages describing anything in the pack that could not be catalogued exactly, such as
    /// a sprite atlas whose sheet image is missing or a tile map that does not parse. Empty for a pack
    /// that catalogued cleanly.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Renames the pack's slug. Only a <see cref="KenneyPackIndex"/> calls this, and only before it
    /// hands out any key, because a slug is part of every key the pack owns.
    /// </summary>
    /// <param name="slug">The new slug.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="slug"/> is null, empty or whitespace.</exception>
    public void AssignSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        Slug = slug;
    }

    /// <summary>
    /// Returns the pack's slug and display name.
    /// </summary>
    /// <returns>A short description of the pack.</returns>
    public override string ToString() => $"{Slug} ({DisplayName})";

    //The licence title line, as written, which is what the catalog publishes as the licence property
    private static string? FirstContentLine(string? text) =>
        text?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
}
