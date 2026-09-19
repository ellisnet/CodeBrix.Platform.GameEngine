using System;
using System.IO;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Turns an audio file inside a Kenney pack into an engine <see cref="AudioResource"/> registered with
/// the engine's <see cref="AudioResourceManager"/>, so game code plays it by key exactly as it plays a
/// sound loaded from a loose file.
/// </summary>
/// <remarks>
/// <para>
/// Decoding is the engine's own business: the bytes go to
/// <see cref="AudioResourceManager.LoadFromStream(string, Stream, string, float, float)"/> with the
/// asset's file extension, and the engine resolves the reader for it. That makes the formats this
/// materializer supports exactly the formats the engine supports - .ogg, .wav, .mp3 and .flac out of
/// the box, plus any other format the application registers with
/// <see cref="PlatformAudioFactory"/> - and it means this library carries no decoder of its own.
/// </para>
/// <para>
/// Materializing is IDEMPOTENT by key: when the audio manager already holds the key, the registered
/// resource is returned and nothing is loaded, because re-registering a key disposes the resource
/// already registered under it and any voice playing from it. The first materialization of a key
/// therefore wins, volume and pan included; a caller that wants the same sound at another volume
/// materializes it under a second key.
/// </para>
/// <para>
/// One instance is safe to use from several threads: a lock spans the check-then-load, so two threads
/// asking for the same key produce one resource. The audio manager itself is the cache - nothing is
/// held here - so a resource a game unloads is materialized again on the next call.
/// </para>
/// </remarks>
internal sealed class AudioMaterializer
{
    private readonly object _gate = new();

    /// <summary>
    /// Materializes an audio asset and registers it under the given key.
    /// </summary>
    /// <param name="entry">The catalogued audio asset to load.</param>
    /// <param name="key">The key to register the resource under, which is the asset's key unless the caller overrode it.</param>
    /// <param name="volume">The resource's initial volume, from 0 (silent) to 1 (unattenuated). Ignored when the key is already registered.</param>
    /// <param name="pan">The resource's initial stereo pan, from -1 (left) to 1 (right). Ignored when the key is already registered.</param>
    /// <returns>
    /// The registered <see cref="AudioResource"/>: the one this call loaded, or the one the audio manager
    /// already held for the key.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnsupportedGameAssetException">Thrown when the asset is not a materializable audio asset.</exception>
    /// <exception cref="NotSupportedException">Thrown when the engine has no reader registered for the asset's audio format; the message names the formats it does have.</exception>
    /// <exception cref="InvalidDataException">Thrown when the asset's bytes could not be decoded; the message names the key and the asset's path, and the original failure is the inner exception.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the asset is no longer in its pack's archive.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the pack's archive has been disposed.</exception>
    public AudioResource Materialize(
        KenneyAssetEntry entry, string key, float volume = 1.0f, float pan = 0.0f)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (entry.Kind != GameAssetKind.Audio || !entry.IsMaterializable)
        {
            throw new UnsupportedGameAssetException(entry.Kind, key);
        }

        AudioResourceManager manager = AudioResourceManager.Instance;

        lock (_gate)
        {
            if (manager.TryGet(key, out AudioResource? registered) && registered is not null)
            {
                return registered;
            }

            //A zip entry hands out its bytes once, forward-only; the audio manager copies whatever it is
            //  given into memory before decoding, so the entry's stream can go straight to it
            using Stream stream = entry.Open();

            try
            {
                return manager.LoadFromStream(key, stream, FileExtension(entry), volume, pan);
            }
            catch (Exception exception) when (exception is not NotSupportedException)
            {
                //A failed load leaves nothing registered (the engine unwinds its own half-load), so the
                //  key is still free for another attempt. What the caller needs is the asset that failed:
                //  a decoder message alone names neither the key nor the file inside the bundle
                throw new InvalidDataException(
                    $"The audio asset '{key}' could not be decoded from '{entry.Path}' in "
                    + $"'{entry.Pack.SourcePath}'.",
                    exception);
            }
        }
    }

    //The engine reads the format from an extension WITH its leading dot; a catalogued extension has none
    private static string FileExtension(KenneyAssetEntry entry) => $".{entry.Extension}";
}
