using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// An <see cref="IKenneyArchive"/> over a folder extracted from a Kenney bundle. It presents exactly
/// the paths the bundle's zip file would present, so a game can point at either and keep its asset
/// keys.
/// </summary>
/// <remarks>
/// The folder is walked once when the archive is created; files added or removed afterwards are not
/// noticed. A .zip inside the folder is one entry, never a folder: nested archives are not expanded,
/// matching the zip implementation. Reads open their own file handle, so no locking is needed and
/// concurrent reads run in parallel.
/// </remarks>
internal sealed class KenneyFolderArchive : IKenneyArchive
{
    private readonly string _rootPath;
    private readonly Dictionary<string, string> _fullPathsByPath;
    private bool _disposed;

    /// <summary>
    /// Walks an extracted bundle folder and indexes every file it holds, at any depth.
    /// </summary>
    /// <param name="folderPath">The path of the folder on disk.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folderPath"/> is null, empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when no folder exists at <paramref name="folderPath"/>.</exception>
    public KenneyFolderArchive(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Kenney asset folder not found: '{folderPath}'.");
        }

        SourcePath = folderPath;
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));

        _fullPathsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<KenneyArchiveEntry> entries = [];
        foreach (string fullPath in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(_rootPath, fullPath);
            string path = KenneyArchivePath.ToArchivePath(relative);
            if (path.Length == 0 || path.StartsWith("..", StringComparison.Ordinal)) { continue; }

            long size;
            try
            {
                size = new FileInfo(fullPath).Length;
            }
            catch (IOException)
            {
                //A file that vanished or cannot be stat'ed between the walk and here is listed at
                //  size 0 rather than aborting the whole folder
                size = 0L;
            }

            _fullPathsByPath[path] = fullPath;
            entries.Add(new KenneyArchiveEntry(path, size));
        }

        Entries = entries
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc/>
    public string SourcePath { get; }

    /// <inheritdoc/>
    public IReadOnlyList<KenneyArchiveEntry> Entries { get; }

    /// <inheritdoc/>
    public bool HasEntry(string? path) =>
        !string.IsNullOrEmpty(path) && _fullPathsByPath.ContainsKey(KenneyArchivePath.ToArchivePath(path));

    /// <inheritdoc/>
    public Stream Open(string path) => File.OpenRead(ResolveFullPath(path));

    /// <inheritdoc/>
    public byte[] ReadBytes(string path) => File.ReadAllBytes(ResolveFullPath(path));

    /// <inheritdoc/>
    public string ReadText(string path) => KenneyArchiveReader.DecodeText(ReadBytes(path));

    /// <inheritdoc/>
    public string? ResolveDependencyPath(string? basePath, string? relativePath, bool strict = true) =>
        KenneyArchiveReader.ResolveDependencyPath(this, basePath, relativePath, strict);

    /// <summary>
    /// Marks the archive as closed. Reads attempted afterwards throw
    /// <see cref="ObjectDisposedException"/>. Disposing twice is harmless; nothing on disk is
    /// touched.
    /// </summary>
    public void Dispose() => _disposed = true;

    private string ResolveFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string archivePath = KenneyArchivePath.ToArchivePath(path);
        if (!_fullPathsByPath.TryGetValue(archivePath, out string? fullPath))
        {
            throw new FileNotFoundException(
                $"The Kenney asset folder '{SourcePath}' holds no entry '{archivePath}'.", archivePath);
        }

        return fullPath;
    }
}
