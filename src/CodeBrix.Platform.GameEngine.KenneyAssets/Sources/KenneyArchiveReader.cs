using System;
using System.Linq;
using System.Text;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Archive behaviour that does not depend on where the bytes come from, shared by
/// <see cref="KenneyZipArchive"/> and <see cref="KenneyFolderArchive"/>: text decoding and
/// dependency-path resolution.
/// </summary>
internal static class KenneyArchiveReader
{
    /// <summary>
    /// Decodes archive bytes as UTF-8 text, honouring a byte order mark when one is present.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <returns>The decoded text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null.</exception>
    public static string DecodeText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        //Kenney ships plain UTF-8, but a byte order mark does turn up on hand-edited licence files
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Resolves a relative reference from one archive file to another, as described by
    /// <see cref="IKenneyArchive.ResolveDependencyPath"/>.
    /// </summary>
    /// <param name="archive">The archive to search.</param>
    /// <param name="basePath">The archive path of the file doing the referencing.</param>
    /// <param name="relativePath">The referenced file's path, as written in the referencing file.</param>
    /// <param name="strict"><see langword="true"/> to skip the bare-file-name fallback.</param>
    /// <returns>The resolved archive path, or <see langword="null"/> when nothing matches.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="archive"/> is null.</exception>
    public static string? ResolveDependencyPath(
        IKenneyArchive archive, string? basePath, string? relativePath, bool strict)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (basePath is null || string.IsNullOrWhiteSpace(relativePath)) { return null; }

        string relative = relativePath.Replace('\\', '/').TrimStart('/');
        string folder = KenneyArchivePath.GetFolder(KenneyArchivePath.ToArchivePath(basePath));

        //Try the reference against the referencing file's own folder, then each parent up to the root
        while (true)
        {
            string candidate = KenneyArchivePath.Combine(folder, relative);
            if (candidate.Length > 0 && archive.HasEntry(candidate)) { return candidate; }
            if (folder.Length == 0) { break; }

            folder = KenneyArchivePath.GetFolder(folder);
        }

        if (strict) { return null; }

        //Last resort, and a guess: match the bare file name anywhere in the archive. A kit can hold
        //  several same-named textures in different folders, so this can pick the wrong one.
        string fileName = KenneyArchivePath.GetFileName(relative);
        return archive.Entries
            .FirstOrDefault(e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            ?.Path;
    }
}
