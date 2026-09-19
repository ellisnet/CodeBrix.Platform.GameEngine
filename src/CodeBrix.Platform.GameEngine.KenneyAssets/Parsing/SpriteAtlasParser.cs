using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Parses the <c>TextureAtlas</c> XML documents that accompany Kenney sprite sheet images, and
/// resolves the sheet image such a document points at.
/// </summary>
internal static class SpriteAtlasParser
{
    /// <summary>
    /// Attempts to parse a <c>TextureAtlas</c> XML document.
    /// </summary>
    /// <param name="xmlText">The full text of the XML document.</param>
    /// <param name="documentPath">The archive path of the XML document, used to name the atlas and to resolve its relative <c>imagePath</c>.</param>
    /// <param name="atlas">The parsed atlas, or <see langword="null"/> when the text is not an atlas document.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a <c>TextureAtlas</c> document carrying at least one
    /// usable frame; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Bundles carry XML files that are not atlases, so failing to parse is an ordinary outcome and
    /// never an error. Whether the sheet image the document names actually exists is a separate
    /// question, answered by <see cref="TryResolveImagePath"/>.
    /// </remarks>
    public static bool TryParse(
        string? xmlText, string documentPath, [NotNullWhen(true)] out SpriteAtlasDocument? atlas)
    {
        atlas = null;
        if (string.IsNullOrWhiteSpace(xmlText)) { return false; }

        XElement? root;
        try
        {
            root = XDocument.Parse(xmlText).Root;
        }
        catch (Exception)
        {
            return false;
        }

        if (root is null || !root.Name.LocalName.Equals("TextureAtlas", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string declaredImagePath = (string?)root.Attribute("imagePath") ?? string.Empty;
        List<SpriteAtlasFrame> frames = [];

        foreach (XElement subTexture in root.Elements()
            .Where(e => e.Name.LocalName.Equals("SubTexture", StringComparison.OrdinalIgnoreCase)))
        {
            string name = (string?)subTexture.Attribute("name") ?? string.Empty;
            if (name.Length == 0) { continue; }

            if (TryReadInt(subTexture, "x", out int x) &&
                TryReadInt(subTexture, "y", out int y) &&
                TryReadInt(subTexture, "width", out int width) &&
                TryReadInt(subTexture, "height", out int height) &&
                width > 0 && height > 0)
            {
                frames.Add(new SpriteAtlasFrame
                {
                    Name = name,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                });
            }
        }

        if (frames.Count == 0) { return false; }

        string archivePath = KenneyArchivePath.ToArchivePath(documentPath);
        atlas = new SpriteAtlasDocument
        {
            Name = KenneyArchivePath.GetFileStem(archivePath),
            DocumentPath = archivePath,
            ImagePath = ResolveDeclaredImagePath(archivePath, declaredImagePath),
            DeclaredImagePath = declaredImagePath,
            Frames = frames,
        };
        return true;
    }

    /// <summary>
    /// Finds the sheet image an atlas document belongs to, inside the archive that holds the document.
    /// </summary>
    /// <param name="atlas">The parsed atlas document.</param>
    /// <param name="archive">The archive the document came from.</param>
    /// <param name="imagePath">The archive path of the sheet image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a sheet image was found; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="atlas"/> or <paramref name="archive"/> is null.</exception>
    /// <remarks>
    /// Four steps, in order. The declared <c>imagePath</c> resolved against the document's folder is
    /// tried first, then - for a declared path that names a folder of its own - the same path read
    /// from the archive root. Some Kenney bundles declare a stale image name - <c>sprites.png</c>
    /// beside a sheet actually called <c>spritesheet_characters.png</c> - so the same-stem sibling
    /// image next to the document is tried third. Only then, as a last resort, is the declared name
    /// matched anywhere in the archive, which is a guess and the one place this library resolves a
    /// dependency loosely.
    /// </remarks>
    public static bool TryResolveImagePath(
        SpriteAtlasDocument atlas, IKenneyArchive archive, [NotNullWhen(true)] out string? imagePath)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(archive);

        imagePath = null;

        if (atlas.ImagePath.Length > 0 && archive.HasEntry(atlas.ImagePath))
        {
            imagePath = atlas.ImagePath;
            return true;
        }

        string rootRelativePath = KenneyArchivePath.Normalize(
            KenneyArchivePath.ToArchivePath(atlas.DeclaredImagePath));
        if (rootRelativePath.Contains('/') && archive.HasEntry(rootRelativePath))
        {
            imagePath = rootRelativePath;
            return true;
        }

        string siblingPath = KenneyArchivePath.RemoveExtension(atlas.DocumentPath) + ".png";
        if (archive.HasEntry(siblingPath))
        {
            imagePath = siblingPath;
            return true;
        }

        string? loose = archive.ResolveDependencyPath(
            atlas.DocumentPath, atlas.DeclaredImagePath, strict: false);
        if (loose is not null)
        {
            imagePath = loose;
            return true;
        }

        return false;
    }

    private static bool TryReadInt(XElement element, string attributeName, out int value)
    {
        value = 0;
        string? text = (string?)element.Attribute(attributeName);
        return text is not null
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    //The imagePath attribute is relative to the folder holding the XML document
    private static string ResolveDeclaredImagePath(string documentPath, string declaredImagePath)
    {
        if (declaredImagePath.Length == 0) { return string.Empty; }

        return KenneyArchivePath.Combine(
            KenneyArchivePath.GetFolder(documentPath), declaredImagePath);
    }
}
