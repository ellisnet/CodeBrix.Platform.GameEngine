using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Compression.Zip;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// An <see cref="IKenneyArchive"/> over a downloaded Kenney bundle (.zip), read from a file or from a
/// stream. Entries are served on demand; the archive is never extracted.
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
    private readonly Stream? _ownedStream;
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
            IndexEntries(_zipFile, _indexesByPath, entries);
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

    /// <summary>
    /// Opens a Kenney bundle zip held in a stream - a packaged app file, a download in memory - for
    /// random-access entry reads, without the bundle ever being a file on disk.
    /// </summary>
    /// <param name="zipStream">
    /// A readable stream holding the whole zip archive from its start. A seekable stream is read in
    /// place; a stream that cannot seek is copied into memory once, in full, and read from that copy.
    /// </param>
    /// <param name="sourceName">
    /// The name the bundle is known by, used as <see cref="SourcePath"/> and in every message about it.
    /// It is never treated as a file system path.
    /// </param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="zipStream"/> open when the archive is disposed or
    /// fails to open; <see langword="false"/> to hand the stream to the archive, which disposes it.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipStream"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceName"/> is null, empty or whitespace, or when
    /// <paramref name="zipStream"/> cannot be read.
    /// </exception>
    /// <exception cref="IOException">Thrown when the stream does not hold a readable zip archive.</exception>
    /// <remarks>
    /// A seekable stream stays in use for as long as the archive is open, so the caller must not move
    /// or read it in the meantime. A stream that cannot seek is finished with once it has been copied:
    /// when <paramref name="leaveOpen"/> is <see langword="false"/> it is disposed straight away, and
    /// the copy, which the archive always owns, is released when the archive is disposed.
    /// </remarks>
    public KenneyZipArchive(Stream zipStream, string sourceName, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!zipStream.CanRead)
        {
            throw new ArgumentException(
                $"The stream given for the Kenney bundle '{sourceName}' cannot be read.", nameof(zipStream));
        }

        SourcePath = sourceName;
        _indexesByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        List<KenneyArchiveEntry> entries = [];

        Stream? readFrom = null;
        bool ownsReadFrom = false;
        try
        {
            if (zipStream.CanSeek)
            {
                readFrom = zipStream;
                ownsReadFrom = !leaveOpen;
            }
            else
            {
                //The zip reader seeks to the central directory at the end, so a forward-only stream
                //  is copied once, in full, and the copy is what gets read
                MemoryStream buffer = new();
                readFrom = buffer;
                ownsReadFrom = true;
                zipStream.CopyTo(buffer);
                buffer.Position = 0;

                if (!leaveOpen) { zipStream.Dispose(); }
            }

            //An empty stream would be taken by the zip reader as a new, empty archive rather than as a
            //  bundle that is not there, which is what a zero-byte file opened by path amounts to
            if (readFrom.Length == 0)
            {
                throw new IOException(
                    $"The stream '{sourceName}' could not be read as a Kenney bundle zip archive: it is empty.");
            }

            //The archive decides when the stream is closed, so the zip reader never closes it itself
            _zipFile = new ZipFile(readFrom, leaveOpen: true);
            IndexEntries(_zipFile, _indexesByPath, entries);
        }
        catch (Exception exception)
        {
            _zipFile?.Close();
            if (ownsReadFrom) { readFrom?.Dispose(); }

            if (exception is IOException or ArgumentException or ObjectDisposedException) { throw; }

            //The zip reader signals a damaged or truncated archive with its own exception type; a
            //  caller only needs to know the stream could not be read as a bundle
            throw new IOException(
                $"The stream '{sourceName}' could not be read as a Kenney bundle zip archive: " +
                $"{exception.Message}",
                exception);
        }

        _ownedStream = ownsReadFrom ? readFrom : null;

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
    /// Closes the underlying zip file, and the stream it was read from when the archive owns that
    /// stream. Reads attempted afterwards throw <see cref="ObjectDisposedException"/>. Disposing twice
    /// is harmless.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _zipFile.Close();
            _ownedStream?.Dispose();
        }
    }

    //Every file entry of the zip, by its archive path; directories and nameless entries are skipped
    private static void IndexEntries(
        ZipFile zipFile, Dictionary<string, long> indexesByPath, List<KenneyArchiveEntry> entries)
    {
        foreach (ZipEntry entry in zipFile)
        {
            if (entry.IsDirectory) { continue; }

            string path = KenneyArchivePath.ToArchivePath(entry.Name);
            if (path.Length == 0) { continue; }

            indexesByPath[path] = entry.ZipFileIndex;
            entries.Add(new KenneyArchiveEntry(path, entry.Size));
        }
    }
}
