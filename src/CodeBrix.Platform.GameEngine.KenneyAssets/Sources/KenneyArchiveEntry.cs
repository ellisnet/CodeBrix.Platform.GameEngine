using System;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// One file inside an <see cref="IKenneyArchive"/>. This is the mechanical view of a file - its
/// path and size - with no notion of what kind of asset it holds.
/// </summary>
internal sealed class KenneyArchiveEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyArchiveEntry"/> class.
    /// </summary>
    /// <param name="path">The archive-relative path of the file: forward slashes, no leading slash.</param>
    /// <param name="sizeBytes">The uncompressed size of the file in bytes; negative values become zero.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty or whitespace.</exception>
    public KenneyArchiveEntry(string path, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = KenneyArchivePath.ToArchivePath(path);
        SizeBytes = Math.Max(0L, sizeBytes);
        FileName = KenneyArchivePath.GetFileName(Path);
        Name = KenneyArchivePath.GetFileStem(Path);
        Extension = KenneyArchivePath.GetExtension(Path);
        Folder = KenneyArchivePath.GetFolder(Path);
    }

    /// <summary>
    /// Gets the archive-relative path of the file, using forward slashes and no leading slash.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the last segment of <see cref="Path"/>, including the extension.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the file name without its extension.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the lower-case extension of the file without the leading dot, or an empty string when
    /// the file has none.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the folder the file sits in, without a trailing slash, or an empty string for a file at
    /// the archive root.
    /// </summary>
    public string Folder { get; }

    /// <summary>
    /// Gets the uncompressed size of the file in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Returns the archive-relative path, which identifies the entry.
    /// </summary>
    /// <returns>The value of <see cref="Path"/>.</returns>
    public override string ToString() => Path;
}
