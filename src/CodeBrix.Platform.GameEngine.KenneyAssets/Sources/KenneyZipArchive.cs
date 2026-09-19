using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Compression.Zip;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// An <see cref="IKenneyArchive"/> over a downloaded Kenney bundle (.zip). Entries are served on
/// demand; the archive is never extracted.
/// </summary>
/// <remarks>
/// Entry streams of one zip file share a single underlying file handle, so every read is serialized
/// on a private lock and <see cref="Open"/> hands back a fully buffered copy rather than a live zip
/// stream. That keeps the type safe to use from several threads, at the cost of holding one entry in
/// memory at a time.
/// </remarks>
internal sealed class KenneyZipArchive : IKenneyArchive
{
    private readonly ZipFile _zipFile;
    private readonly Dictionary<string, long> _indexesByPath;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Opens a Kenney bundle zip file for random-access entry reads.
    /// </summary>
    /// <param name="zipPath">The path of the zip file on disk.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="zipPath"/> is null, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when no file exists at <paramref name="zipPath"/>.</exception>
    /// <exception cref="IOException">Thrown when the file is not a readable zip archive.</exception>
    public KenneyZipArchive(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException($"Kenney bundle zip file not found: '{zipPath}'.", zipPath);
        }

        SourcePath = zipPath;

        //ZipFile.GetEntry is an O(n) scan per lookup; build a name index once instead
        _indexesByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        List<KenneyArchiveEntry> entries = [];
        try
        {
            _zipFile = new ZipFile(zipPath);
            foreach (ZipEntry entry in _zipFile)
            {
                if (entry.IsDirectory) { continue; }

                string path = KenneyArchivePath.ToArchivePath(entry.Name);
                if (path.Length == 0) { continue; }

                _indexesByPath[path] = entry.ZipFileIndex;
                entries.Add(new KenneyArchiveEntry(path, entry.Size));
            }
        }
        catch (Exception exception) when (exception is not IOException and not ArgumentException)
        {
            //The zip reader signals a damaged or truncated archive with its own exception type; a
            //  caller only needs to know the file could not be read as a bundle
            throw new IOException(
                $"The file '{zipPath}' could not be read as a Kenney bundle zip archive: " +
                $"{exception.Message}",
                exception);
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
        !string.IsNullOrEmpty(path) && _indexesByPath.ContainsKey(KenneyArchivePath.ToArchivePath(path));

    /// <inheritdoc/>
    public Stream Open(string path) => new MemoryStream(ReadBytes(path), writable: false);

    /// <inheritdoc/>
    public byte[] ReadBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string archivePath = KenneyArchivePath.ToArchivePath(path);
        if (!_indexesByPath.TryGetValue(archivePath, out long index))
        {
            throw new FileNotFoundException(
                $"The Kenney bundle '{SourcePath}' holds no entry '{archivePath}'.", archivePath);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using Stream input = _zipFile.GetInputStream(index);
            using MemoryStream buffer = new();
            input.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <inheritdoc/>
    public string ReadText(string path) => KenneyArchiveReader.DecodeText(ReadBytes(path));

    /// <inheritdoc/>
    public string? ResolveDependencyPath(string? basePath, string? relativePath, bool strict = true) =>
        KenneyArchiveReader.ResolveDependencyPath(this, basePath, relativePath, strict);

    /// <summary>
    /// Closes the underlying zip file. Reads attempted afterwards throw
    /// <see cref="ObjectDisposedException"/>. Disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _zipFile.Close();
        }
    }
}
