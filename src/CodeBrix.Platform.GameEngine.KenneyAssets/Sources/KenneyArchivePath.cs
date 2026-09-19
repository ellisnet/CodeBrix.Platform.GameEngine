using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Path arithmetic for archive-relative paths. Every path an <see cref="IKenneyArchive"/> exposes
/// uses forward slashes, has no leading slash and no drive or device prefix, whether it came from a
/// zip central directory or from a directory walk, so one set of helpers serves both.
/// </summary>
internal static class KenneyArchivePath
{
    /// <summary>
    /// Converts a raw path from a zip entry or a file system walk into the archive form: forward
    /// slashes, no leading slash, no trailing slash.
    /// </summary>
    /// <param name="path">The raw path.</param>
    /// <returns>The archive form of the path, or an empty string when the input is null or empty.</returns>
    public static string ToArchivePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        string result = path.Replace('\\', '/').Trim('/');
        return result;
    }

    /// <summary>
    /// Collapses <c>.</c> and <c>..</c> segments so a relative reference becomes a real archive
    /// path. A <c>..</c> that would climb above the archive root is dropped.
    /// </summary>
    /// <param name="path">The path to normalize; it must already be in archive form.</param>
    /// <returns>The normalized path.</returns>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        List<string> segments = [];
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") { continue; }
            if (segment == "..")
            {
                if (segments.Count > 0) { segments.RemoveAt(segments.Count - 1); }
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    /// <summary>
    /// Gets the last segment of a path, including its extension.
    /// </summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The file name, or an empty string when the path is null or empty.</returns>
    public static string GetFileName(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        int lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? path : path[(lastSlash + 1)..];
    }

    /// <summary>
    /// Gets the folder part of a path, without a trailing slash.
    /// </summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The folder, or an empty string for a path at the archive root.</returns>
    public static string GetFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        int lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : path[..lastSlash];
    }

    /// <summary>
    /// Gets the file name of a path without its extension.
    /// </summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The file name without its extension, or an empty string when the path is null or empty.</returns>
    public static string GetFileStem(string? path)
    {
        string fileName = GetFileName(path);
        int lastDot = fileName.LastIndexOf('.');
        return lastDot <= 0 ? fileName : fileName[..lastDot];
    }

    /// <summary>
    /// Gets the lower-case extension of a path, without the leading dot. A leading dot on the file
    /// name itself (<c>.gitignore</c>) is not an extension.
    /// </summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The extension, or an empty string when the path has none.</returns>
    public static string GetExtension(string? path)
    {
        string fileName = GetFileName(path);
        int lastDot = fileName.LastIndexOf('.');
        return lastDot <= 0 || lastDot == fileName.Length - 1
            ? string.Empty
            : fileName[(lastDot + 1)..].ToLowerInvariant();
    }

    /// <summary>
    /// Removes the extension from a path, keeping its folder.
    /// </summary>
    /// <param name="path">The path to shorten.</param>
    /// <returns>The path without its extension, or the path unchanged when it has none.</returns>
    public static string RemoveExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) { return string.Empty; }

        string extension = GetExtension(path);
        return extension.Length == 0 ? path : path[..^(extension.Length + 1)];
    }

    /// <summary>
    /// Joins a folder and a relative path, then normalizes the result.
    /// </summary>
    /// <param name="folder">The folder to resolve against; an empty string means the archive root.</param>
    /// <param name="relativePath">The relative path.</param>
    /// <returns>The combined, normalized archive path.</returns>
    public static string Combine(string folder, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string relative = relativePath.Replace('\\', '/').TrimStart('/');
        return Normalize(string.IsNullOrEmpty(folder) ? relative : folder + "/" + relative);
    }
}
