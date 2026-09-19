using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// One place a game points the provider at: a downloaded Kenney bundle (.zip), a folder extracted from
/// one, or a folder that holds several such folders. Opening a source yields one pack, or several when
/// the folder turns out to be a collection.
/// </summary>
/// <remarks>
/// A folder is a collection when it carries no licence file of its own but folders inside it do, which
/// is the shape of Kenney's own "all in one" download: <c>2D assets/&lt;Pack&gt;/License.txt</c>. Each
/// such folder becomes a pack of its own, so pointing at the collection registers every pack in it. The
/// search stops as soon as a folder with a licence file is found and never goes deeper than
/// <see cref="MaxCollectionDepth"/> folders, which keeps it from walking a tree of thousands of asset
/// files looking for packs that are not there. A caller that wants a folder taken as ONE pack, whatever
/// it holds, opens it with the collection search turned off.
/// </remarks>
internal sealed class KenneyAssetSource : IDisposable
{
    /// <summary>
    /// How many folder levels below a source folder the search for packs will go.
    /// </summary>
    public const int MaxCollectionDepth = 3;

    private readonly IReadOnlyList<IKenneyArchive> _archives;
    private bool _disposed;

    private KenneyAssetSource(
        string sourcePath,
        bool isFolder,
        IReadOnlyList<KenneyPack> packs,
        IReadOnlyList<IKenneyArchive> archives,
        IReadOnlyList<string> warnings)
    {
        SourcePath = sourcePath;
        IsFolder = isFolder;
        Packs = packs;
        _archives = archives;
        Warnings = warnings;
    }

    /// <summary>
    /// Gets the path of the zip file or folder this source was opened from.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets a value indicating whether the source is a folder rather than a zip file.
    /// </summary>
    public bool IsFolder { get; }

    /// <summary>
    /// Gets the packs the source holds, in a stable order: one for a zip file or a single extracted
    /// pack, and one per child pack for a collection folder.
    /// </summary>
    public IReadOnlyList<KenneyPack> Packs { get; }

    /// <summary>
    /// Gets the messages describing anything the source could not open or catalog exactly, including
    /// the warnings of every pack it holds.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Opens a zip bundle or an asset folder and catalogs the packs it holds.
    /// </summary>
    /// <param name="zipFileOrFolderPath">The path of the .zip file or the folder.</param>
    /// <param name="recursiveFolders">
    /// Whether a folder carrying no licence file of its own is searched for pack folders inside it.
    /// Ignored for a zip file, which is always one pack.
    /// </param>
    /// <returns>The open source.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="zipFileOrFolderPath"/> is null, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when neither a file nor a folder exists at that path.</exception>
    /// <exception cref="IOException">Thrown when a zip file cannot be read.</exception>
    public static KenneyAssetSource Open(string zipFileOrFolderPath, bool recursiveFolders = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFileOrFolderPath);

        if (File.Exists(zipFileOrFolderPath)) { return OpenZip(zipFileOrFolderPath); }
        if (Directory.Exists(zipFileOrFolderPath))
        {
            return OpenFolder(zipFileOrFolderPath, recursiveFolders);
        }

        throw new FileNotFoundException(
            $"No Kenney bundle zip file or asset folder exists at '{zipFileOrFolderPath}'.",
            zipFileOrFolderPath);
    }

    /// <summary>
    /// Attempts to open a zip bundle or an asset folder, turning a failure into a message instead of an
    /// exception.
    /// </summary>
    /// <param name="zipFileOrFolderPath">The path of the .zip file or the folder.</param>
    /// <param name="source">The open source when the method returns <see langword="true"/>.</param>
    /// <param name="warning">Why the source could not be opened when the method returns <see langword="false"/>.</param>
    /// <param name="recursiveFolders">
    /// Whether a folder carrying no licence file of its own is searched for pack folders inside it.
    /// Ignored for a zip file, which is always one pack.
    /// </param>
    /// <returns><see langword="true"/> when the source opened; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// This is the form registration uses: one unreadable bundle among several must not stop a game from
    /// starting.
    /// </remarks>
    public static bool TryOpen(
        string? zipFileOrFolderPath,
        out KenneyAssetSource? source,
        out string? warning,
        bool recursiveFolders = true)
    {
        if (string.IsNullOrWhiteSpace(zipFileOrFolderPath))
        {
            source = null;
            warning = "An asset source path was empty and has been ignored.";
            return false;
        }

        try
        {
            source = Open(zipFileOrFolderPath, recursiveFolders);
            warning = null;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            source = null;
            warning = $"The asset source '{zipFileOrFolderPath}' could not be opened: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Closes every archive the source opened. The packs it produced cannot be read afterwards.
    /// Disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;

        foreach (IKenneyArchive archive in _archives) { archive.Dispose(); }
    }

    private static KenneyAssetSource OpenZip(string zipPath)
    {
        KenneyZipArchive archive = new(zipPath);
        try
        {
            KenneyPack pack = KenneyPack.Create(archive, Path.GetFileName(zipPath));
            return new KenneyAssetSource(zipPath, isFolder: false, [pack], [archive], [.. pack.Warnings]);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static KenneyAssetSource OpenFolder(string folderPath, bool recursiveFolders)
    {
        string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        List<string> packFolders = [];

        if (recursiveFolders) { FindPackFolders(rootPath, 0, packFolders); }

        //A folder with no licence file anywhere below it is still treated as one pack: a game may be
        //  pointing at an extracted bundle whose licence file was not kept
        if (packFolders.Count == 0 || (packFolders.Count == 1 && PathsMatch(packFolders[0], rootPath)))
        {
            KenneyFolderArchive archive = new(rootPath);
            try
            {
                KenneyPack pack = KenneyPack.Create(archive, Path.GetFileName(rootPath));
                return new KenneyAssetSource(
                    folderPath, isFolder: true, [pack], [archive], [.. pack.Warnings]);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        List<KenneyPack> packs = [];
        List<IKenneyArchive> archives = [];
        List<string> warnings = [];
        try
        {
            foreach (string packFolder in packFolders.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                KenneyFolderArchive archive;
                try
                {
                    archive = new KenneyFolderArchive(packFolder);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"The pack folder '{packFolder}' could not be read: {exception.Message}");
                    continue;
                }

                archives.Add(archive);
                KenneyPack pack = KenneyPack.Create(archive, Path.GetFileName(packFolder));
                packs.Add(pack);
                warnings.AddRange(pack.Warnings);
            }
        }
        catch
        {
            foreach (IKenneyArchive archive in archives) { archive.Dispose(); }
            throw;
        }

        return new KenneyAssetSource(folderPath, isFolder: true, packs, archives, warnings);
    }

    //Depth-first, stopping at the first folder that carries a licence file
    private static void FindPackFolders(string folderPath, int depth, List<string> found)
    {
        if (HasLicenseFile(folderPath))
        {
            found.Add(folderPath);
            return;
        }

        if (depth >= MaxCollectionDepth) { return; }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folderPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string child in children) { FindPackFolders(child, depth + 1, found); }
    }

    private static bool HasLicenseFile(string folderPath)
    {
        try
        {
            //Matched case-insensitively, which a plain File.Exists would not do on Linux
            return Directory.EnumerateFiles(folderPath, "*.txt").Any(file =>
                Path.GetFileName(file).Equals(KenneyPack.LicenseFileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);
}
