using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Turns Kenney bundle file names, folder names and licence headers into display names and into the
/// slugs that namespace asset keys.
/// </summary>
internal static class KenneyNames
{
    /// <summary>
    /// The slug used when a name contains nothing a slug can be made of.
    /// </summary>
    public const string FallbackSlug = "pack";

    /// <summary>
    /// Prettifies a bundle file or folder name: <c>kenney_brick-kit.zip</c> becomes
    /// <c>Brick Kit</c>.
    /// </summary>
    /// <param name="fileOrFolderName">The bundle file name, folder name, or a full path to either.</param>
    /// <returns>A title-cased display name, or an empty string when there is nothing to prettify.</returns>
    public static string PrettifyBundleFileName(string? fileOrFolderName)
    {
        if (string.IsNullOrWhiteSpace(fileOrFolderName)) { return string.Empty; }

        string name = fileOrFolderName.Replace('\\', '/').TrimEnd('/');
        int lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0) { name = name[(lastSlash + 1)..]; }

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (name.StartsWith("kenney_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["kenney_".Length..];
        }

        IEnumerable<string> words = name
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..]);
        return string.Join(" ", words);
    }

    /// <summary>
    /// Attempts to read the pack title and version from the first content line of a Kenney
    /// <c>License.txt</c> file, which looks like <c>Brick Kit (1.0)</c>.
    /// </summary>
    /// <param name="licenseText">The full text of the licence file.</param>
    /// <param name="title">The pack title, or <see langword="null"/> when none was found.</param>
    /// <param name="version">The version inside the parentheses, or <see langword="null"/> when absent.</param>
    /// <returns><see langword="true"/> when a plausible title line was found.</returns>
    /// <remarks>
    /// A title line is short prose on the first non-blank line. A long line, or one carrying a URL,
    /// is licence body text rather than a title and is rejected, which is what keeps a licence file
    /// without the Kenney header from producing a nonsense pack name.
    /// </remarks>
    public static bool TryParseLicenseTitle(
        string? licenseText, [NotNullWhen(true)] out string? title, out string? version)
    {
        title = null;
        version = null;
        if (string.IsNullOrWhiteSpace(licenseText)) { return false; }

        string? firstLine = licenseText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
        if (firstLine is null) { return false; }

        //A title line is short prose, not one of the licence body sentences
        if (firstLine.Length > 80 || firstLine.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int openParen = firstLine.LastIndexOf('(');
        if (openParen > 0 && firstLine.EndsWith(')'))
        {
            title = firstLine[..openParen].Trim();
            version = firstLine[(openParen + 1)..^1].Trim();
        }
        else
        {
            title = firstLine;
        }

        return title.Length > 0;
    }

    /// <summary>
    /// Turns a pack display name into the lower-case slug that namespaces the pack's asset keys:
    /// <c>Puzzle Pack 1</c> becomes <c>puzzle-pack-1</c>.
    /// </summary>
    /// <param name="name">The display name to convert.</param>
    /// <returns>
    /// The slug: letters and digits lower-cased and kept, every other run of characters replaced by a
    /// single hyphen, with no leading or trailing hyphen. <see cref="FallbackSlug"/> when the name
    /// holds no letters or digits at all.
    /// </returns>
    /// <remarks>
    /// Letters outside the ASCII range are kept rather than transliterated, so a name carrying
    /// diacritics keeps them in its slug. Keys are compared case-insensitively, so the lower-casing
    /// is cosmetic.
    /// </remarks>
    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return FallbackSlug; }

        StringBuilder builder = new(name.Length);
        bool pendingHyphen = false;
        foreach (char character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingHyphen && builder.Length > 0) { builder.Append('-'); }
                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.Length == 0 ? FallbackSlug : builder.ToString();
    }
}
