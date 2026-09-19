using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Random-access reads over the files of one Kenney asset pack, whether the pack is a downloaded
/// .zip bundle or a folder extracted from one.
/// </summary>
/// <remarks>
/// <para>
/// Paths are archive-relative, use forward slashes and have no leading slash. A zip bundle and a
/// folder extracted from that same bundle therefore present identical paths, which is what lets a
/// game switch between the two without touching its asset keys.
/// </para>
/// <para>
/// Implementations are safe to call from more than one thread, because the asset-provider contract
/// allows materialization from any thread. Nested archives are never expanded: a .zip inside an
/// archive is one entry, not a folder.
/// </para>
/// </remarks>
internal interface IKenneyArchive : IDisposable
{
    /// <summary>
    /// Gets the path of the zip file or folder this archive reads from.
    /// </summary>
    string SourcePath { get; }

    /// <summary>
    /// Gets every file in the archive, ordered by path, compared case-insensitively. Directories
    /// are not listed.
    /// </summary>
    IReadOnlyList<KenneyArchiveEntry> Entries { get; }

    /// <summary>
    /// Determines whether the archive holds a file at the given path. Paths are compared
    /// case-insensitively.
    /// </summary>
    /// <param name="path">The archive-relative path of the file.</param>
    /// <returns><see langword="true"/> when the file exists; otherwise <see langword="false"/>.</returns>
    bool HasEntry(string? path);

    /// <summary>
    /// Opens one file for reading.
    /// </summary>
    /// <param name="path">The archive-relative path of the file.</param>
    /// <returns>A readable, seekable stream positioned at the start of the file. The caller disposes it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive holds no file at that path.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the archive has been disposed.</exception>
    Stream Open(string path);

    /// <summary>
    /// Reads the whole of one file.
    /// </summary>
    /// <param name="path">The archive-relative path of the file.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive holds no file at that path.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the archive has been disposed.</exception>
    byte[] ReadBytes(string path);

    /// <summary>
    /// Reads the whole of one file as UTF-8 text, honouring a byte order mark when one is present.
    /// </summary>
    /// <param name="path">The archive-relative path of the file.</param>
    /// <returns>The file's text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive holds no file at that path.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the archive has been disposed.</exception>
    string ReadText(string path);

    /// <summary>
    /// Resolves the archive path of a file that another file references by relative path - the
    /// <c>../Tilemap/tilemap_packed.png</c> a Tiled tileset points at, or the texture a glTF model
    /// names beside itself.
    /// </summary>
    /// <param name="basePath">The archive path of the file doing the referencing.</param>
    /// <param name="relativePath">The referenced file's path, as written in the referencing file.</param>
    /// <param name="strict">
    /// <see langword="true"/> to resolve only against the referencing file's own folder and then
    /// each parent folder up to the archive root. <see langword="false"/> additionally accepts a
    /// file with the same bare name anywhere in the archive, which is a guess: a Kenney kit can hold
    /// several same-named textures in different folders, so non-strict resolution can pick the wrong
    /// one and is reserved for cases where a bundle's own metadata is known to be stale.
    /// </param>
    /// <returns>The resolved archive path, or <see langword="null"/> when nothing matches.</returns>
    string? ResolveDependencyPath(string? basePath, string? relativePath, bool strict = true);
}
