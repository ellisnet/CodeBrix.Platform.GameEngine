using System;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Turns a font file inside a Kenney pack into an <see cref="SKTypeface"/> registered with the engine's
/// <see cref="FontManager"/>, so the key resolves wherever the engine looks a font up and the typeface
/// itself can go straight to a text element's font.
/// </summary>
/// <remarks>
/// <para>
/// The font never touches the disk: the asset's bytes go to
/// <see cref="FontManager.LoadFromStream(string, Stream)"/>, which is the overload the engine carries for
/// fonts that live inside an archive. TrueType (.ttf) and OpenType (.otf) files are what Kenney packs
/// ship and what this handles. The web-font archives some packs place beside them (<c>Webfonts A.zip</c>)
/// are catalogued as nested archives rather than fonts and are never opened - a pack's loose .ttf files
/// hold the same faces.
/// </para>
/// <para>
/// Materializing is IDEMPOTENT by key: when the font manager already holds the key, the registered
/// typeface is returned and nothing is loaded, because re-registering a key disposes the typeface already
/// registered under it while text elements may still be drawing with it.
/// </para>
/// <para>
/// One instance is safe to use from several threads: a lock spans the check-then-load, so two threads
/// asking for the same key produce one typeface. The font manager itself is the cache - nothing is held
/// here - so a font a game removes is materialized again on the next call. The manager is not itself
/// synchronized, so an application that registers fonts from its own threads as well should serialize
/// those calls.
/// </para>
/// </remarks>
internal sealed class FontMaterializer
{
    private readonly object _gate = new();

    /// <summary>
    /// Materializes a font asset and registers it under the given key.
    /// </summary>
    /// <param name="entry">The catalogued font asset to load.</param>
    /// <param name="key">The key to register the typeface under, which is the asset's key unless the caller overrode it.</param>
    /// <returns>
    /// The registered <see cref="SKTypeface"/>: the one this call loaded, or the one the font manager
    /// already held for the key. The font manager owns it; callers do not dispose it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a materializable font asset, which includes a nested web-font archive.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes are not a loadable font; the message names the key and the asset's path, and the original failure is the inner exception.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the asset is no longer in its pack's archive.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the pack's archive or the font manager has been disposed.</exception>
    public SKTypeface Materialize(KenneyAssetEntry entry, string key)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (entry.Kind != GameAssetKind.Font || !entry.IsMaterializable)
        {
            throw new UnsupportedGameAssetException(entry.Kind, key);
        }

        FontManager fonts = FontManager.Instance;

        lock (_gate)
        {
            if (fonts.TryGet(key, out SKTypeface? registered) && registered is not null)
            {
                return registered;
            }

            //The font manager reads the stream to its end and copies the bytes itself, so a forward-only
            //  zip entry stream is all it needs
            using Stream stream = entry.Open();

            try
            {
                return fonts.LoadFromStream(key, stream);
            }
            catch (Exception exception) when (exception is not ObjectDisposedException)
            {
                //Nothing is registered when the load fails, so the key is still free for another attempt.
                //  What the caller needs is the asset that failed: the manager's own message names the key
                //  but not the file inside the bundle
                throw new InvalidDataException(
                    $"The font asset '{key}' could not be read as a typeface from '{entry.Path}' in "
                    + $"'{entry.Pack.SourcePath}'.",
                    exception);
            }
        }
    }
}
