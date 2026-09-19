using System.Reflection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering.Text; //was previously: Gondwana.Rendering.Text;
/// <summary>
/// Centralized manager for loading, retrieving, and unloading shared fonts.
/// Fonts are stored by string key and reused across the application.
/// </summary>
/// <remarks>
/// Every registered font also carries a platform-neutral family name, read from the font file itself
/// (<see cref="GetFamilyName(string)"/>). Use that name, or the key, to identify a font in game code -
/// never <see cref="SKTypeface.FamilyName"/>, which is whatever the platform's native font back end
/// reports and differs between Windows, Linux and macOS for the same font file.
/// </remarks>
public sealed class FontManager : IDisposable
{
    private static readonly Lazy<FontManager> _instance = new(() => new FontManager());

    private readonly Dictionary<string, SKTypeface> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _familyNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Gets the singleton instance of the <see cref="FontManager"/>.
    /// </summary>
    public static FontManager Instance => _instance.Value;

    /// <summary>
    /// Prevents direct instantiation.
    /// </summary>
    private FontManager() { }

    /// <summary>
    /// Loads a font from a file path and stores it under the given key.
    /// If the key already exists, the old font is disposed and replaced.
    /// </summary>
    /// <param name="key">Logical name for the font.</param>
    /// <param name="filePath">Path to the font file.</param>
    /// <returns>The loaded font.</returns>
    /// <exception cref="ArgumentException">Thrown when key or filePath is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the font could not be loaded.</exception>
    public SKTypeface LoadFromFile(string key, string filePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Font file path cannot be null or whitespace.", nameof(filePath));

        var typeface = SKTypeface.FromFile(filePath);
        if (typeface == null)
            throw new InvalidOperationException($"Failed to load font from file: {filePath}");

        ReplaceInternal(key, typeface);
        return typeface;
    }

    /// <summary>
    /// Loads a font from a stream and stores it under the given key.
    /// If the key already exists, the old font is disposed and replaced.
    /// </summary>
    /// <param name="key">Logical name for the font.</param>
    /// <param name="stream">The stream containing the font data. It is read to the end from its
    /// current position; the caller keeps ownership and disposes it.</param>
    /// <returns>The loaded font.</returns>
    /// <remarks>
    /// This is the overload to use for a font that lives inside an archive or any other container
    /// that cannot hand out a file path. The stream does not have to be seekable.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the key is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the stream is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the font could not be loaded.</exception>
    public SKTypeface LoadFromStream(string key, Stream stream)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return LoadFromBytes(key, buffer.ToArray());
    }

    /// <summary>
    /// Loads a font from an in-memory copy of a font file and stores it under the given key.
    /// If the key already exists, the old font is disposed and replaced.
    /// </summary>
    /// <param name="key">Logical name for the font.</param>
    /// <param name="fontData">The complete contents of a font file.</param>
    /// <returns>The loaded font.</returns>
    /// <exception cref="ArgumentException">Thrown when the key is null or whitespace, or the data is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the data is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the font could not be loaded.</exception>
    public SKTypeface LoadFromBytes(string key, byte[] fontData)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        ArgumentNullException.ThrowIfNull(fontData);

        if (fontData.Length == 0)
            throw new ArgumentException("Font data cannot be empty.", nameof(fontData));

        var typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
        if (typeface == null)
            throw new InvalidOperationException($"Failed to load font from data for key: {key}");

        ReplaceInternal(key, typeface);
        return typeface;
    }

    /// <summary>
    /// Loads a font from an embedded resource in the specified assembly and stores it under the given key.
    /// If the key already exists, the old font is disposed and replaced.
    /// </summary>
    /// <param name="key">Logical name for the font.</param>
    /// <param name="assembly">Assembly containing the embedded font resource.</param>
    /// <param name="resourceName">Fully qualified embedded resource name.</param>
    /// <returns>The loaded font.</returns>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the resource is missing or the font could not be loaded.</exception>
    public SKTypeface LoadFromResource(string key, Assembly assembly, string resourceName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name cannot be null or whitespace.", nameof(resourceName));

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded font resource not found: '{resourceName}' in assembly '{assembly.FullName}'.");

        var typeface = SKTypeface.FromStream(stream);
        if (typeface == null)
            throw new InvalidOperationException($"Failed to load font from embedded resource: {resourceName}");

        ReplaceInternal(key, typeface);
        return typeface;
    }

    /// <summary>
    /// Loads a font from an embedded resource in the calling assembly and stores it under the given key.
    /// If the key already exists, the old font is disposed and replaced.
    /// </summary>
    public SKTypeface LoadFromResource(string key, string resourceName)
    {
        return LoadFromResource(key, Assembly.GetCallingAssembly(), resourceName);
    }

    /// <summary>
    /// Gets a font by key.
    /// </summary>
    /// <param name="key">Logical name of the font.</param>
    /// <returns>The matching font.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the key does not exist.</exception>
    public SKTypeface Get(string key)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        if (!_fonts.TryGetValue(key, out var typeface))
            throw new KeyNotFoundException($"No font is registered under key '{key}'.");

        return typeface;
    }

    /// <summary>
    /// Tries to get a font by key.
    /// </summary>
    public bool TryGet(string key, out SKTypeface? typeface)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
        {
            typeface = null;
            return false;
        }

        return _fonts.TryGetValue(key, out typeface);
    }

    /// <summary>
    /// Retrieves the typeface associated with the specified key, or returns the default typeface if the key is not
    /// found or is null or whitespace.
    /// </summary>
    /// <param name="key">The key used to identify the desired typeface. If null, empty, or consists only of whitespace, the default
    /// typeface is returned.</param>
    /// <returns>The typeface associated with the specified key, or the default typeface if the key is not found or the key is
    /// null or whitespace.</returns>
    public SKTypeface GetOrDefault(string key)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            return SKTypeface.Default;

        return _fonts.TryGetValue(key, out var typeface)
            ? typeface
            : SKTypeface.Default;
    }

    /// <summary>
    /// Gets the platform-neutral family name of the font registered under a key.
    /// </summary>
    /// <param name="key">Logical name of the font.</param>
    /// <returns>
    /// The family name read from the font file: its typographic family name (OpenType name ID 16) when it
    /// declares one, otherwise its family name (name ID 1). The same font file gives the same name on
    /// every platform, unlike <see cref="SKTypeface.FamilyName"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the key is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the key does not exist.</exception>
    public string GetFamilyName(string key)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Font key cannot be null or whitespace.", nameof(key));

        if (!_familyNames.TryGetValue(key, out var familyName))
            throw new KeyNotFoundException($"No font is registered under key '{key}'.");

        return familyName;
    }

    /// <summary>
    /// Tries to get the platform-neutral family name of the font registered under a key.
    /// </summary>
    /// <param name="key">Logical name of the font.</param>
    /// <param name="familyName">The family name, as <see cref="GetFamilyName(string)"/> returns it.</param>
    /// <returns>True if a font is registered under the key; otherwise false.</returns>
    public bool TryGetFamilyName(string key, out string? familyName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
        {
            familyName = null;
            return false;
        }

        return _familyNames.TryGetValue(key, out familyName);
    }

    /// <summary>
    /// Gets the keys of every registered font with the given platform-neutral family name.
    /// </summary>
    /// <param name="familyName">The family name to match, ignoring case.</param>
    /// <returns>The matching keys in ordinal, case-insensitive order; empty when none match.</returns>
    public IReadOnlyList<string> GetKeysByFamilyName(string familyName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(familyName))
            return [];

        return _familyNames
            .Where(pair => string.Equals(pair.Value, familyName, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Tries to get a registered font by its platform-neutral family name.
    /// </summary>
    /// <param name="familyName">The family name to match, ignoring case.</param>
    /// <param name="typeface">
    /// The matching font. When several keys hold fonts of the family - two weights of one typographic
    /// family, say - it is the font under the first of <see cref="GetKeysByFamilyName(string)"/>; use
    /// the keys to pick a specific face.
    /// </param>
    /// <returns>True if a registered font has the family name; otherwise false.</returns>
    public bool TryGetByFamilyName(string familyName, out SKTypeface? typeface)
    {
        var keys = GetKeysByFamilyName(familyName);

        if (keys.Count == 0)
        {
            typeface = null;
            return false;
        }

        typeface = _fonts[keys[0]];
        return true;
    }

    /// <summary>
    /// Returns true if a font exists for the given key.
    /// </summary>
    public bool Contains(string key)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            return false;

        return _fonts.ContainsKey(key);
    }

    /// <summary>
    /// Removes a single font entry by key and disposes the stored typeface.
    /// </summary>
    /// <param name="key">Logical name of the font.</param>
    /// <returns>True if the font was found and removed; otherwise false.</returns>
    public bool Remove(string key)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!_fonts.TryGetValue(key, out var existing))
            return false;

        _fonts.Remove(key);
        _familyNames.Remove(key);
        existing.Dispose();
        return true;
    }

    /// <summary>
    /// Disposes all loaded fonts and clears the manager.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        foreach (var font in _fonts.Values)
            font.Dispose();

        _fonts.Clear();
        _familyNames.Clear();
    }

    /// <summary>
    /// Gets all currently registered font keys.
    /// </summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            ThrowIfDisposed();
            return _fonts.Keys.ToArray();
        }
    }

    private void ReplaceInternal(string key, SKTypeface newTypeface)
    {
        _familyNames[key] = FontFamilyNameReader.GetFamilyName(newTypeface);

        if (_fonts.TryGetValue(key, out var existing))
        {
            existing.Dispose();
            _fonts[key] = newTypeface;
        }
        else
        {
            _fonts.Add(key, newTypeface);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FontManager));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }
}
