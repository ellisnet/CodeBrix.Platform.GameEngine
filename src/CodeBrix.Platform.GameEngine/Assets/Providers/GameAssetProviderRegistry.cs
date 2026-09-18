using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Scenes;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Holds the <see cref="IGameAssetProvider"/> instances the game has registered and dispatches
/// asset requests to the provider that owns the key.
/// </summary>
/// <remarks>
/// <para>
/// Reach the registry through <c>Engine.Instance.Managers.AssetProviders</c>. Registration is
/// explicit and idempotent, in the same spirit as the engine's other opt-in subsystems; providers
/// are runtime arrangements and are never written to saved engine state. Everything the registry
/// holds is released when the engine is disposed.
/// </para>
/// <para>
/// The registry is safe to use from several threads. Its own bookkeeping is serialized, and
/// provider calls are made outside that lock so a slow load never blocks registration.
/// </para>
/// </remarks>
public sealed class GameAssetProviderRegistry
{
    private static readonly Lazy<GameAssetProviderRegistry> _instance = new(() => new GameAssetProviderRegistry());

    private readonly object _gate = new();
    private readonly Dictionary<string, IGameAssetProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Prevents direct instantiation.
    /// </summary>
    private GameAssetProviderRegistry() { }

    /// <summary>
    /// Gets the singleton instance of the <see cref="GameAssetProviderRegistry"/>.
    /// </summary>
    public static GameAssetProviderRegistry Instance => _instance.Value;

    /// <summary>
    /// Gets a snapshot of the registered providers.
    /// </summary>
    public IReadOnlyList<IGameAssetProvider> Providers
    {
        get
        {
            lock (_gate)
            {
                return _providers.Values.ToArray();
            }
        }
    }

    /// <summary>
    /// Gets the number of registered providers.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _providers.Count;
            }
        }
    }

    /// <summary>
    /// Registers a provider, or replaces the provider already registered under the same
    /// identifier.
    /// </summary>
    /// <param name="provider">The provider to register.</param>
    /// <remarks>
    /// Registering the same instance twice does nothing. Registering a different instance under an
    /// identifier that is already taken replaces the old provider and disposes it, so keys keep
    /// resolving to exactly one provider.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the provider's identifier is null, whitespace, or contains a colon (which would
    /// make its keys ambiguous).
    /// </exception>
    public void Register(IGameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        string providerId = provider.ProviderId;

        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("An asset provider must have a non-empty identifier.", nameof(provider));

        if (providerId.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException(
                $"The asset provider identifier '{providerId}' cannot contain a colon; the colon separates the "
                + "identifier from the rest of an asset key.",
                nameof(provider));

        IGameAssetProvider? replaced = null;

        lock (_gate)
        {
            if (_providers.TryGetValue(providerId, out var existing))
            {
                if (ReferenceEquals(existing, provider))
                    return;

                replaced = existing;
            }

            _providers[providerId] = provider;
        }

        if (replaced is not null)
            SafeDispose(replaced);
    }

    /// <summary>
    /// Removes the provider registered under an identifier.
    /// </summary>
    /// <param name="providerId">The identifier the provider was registered under.</param>
    /// <param name="dispose">Whether the removed provider is disposed. Defaults to <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a provider was found and removed; otherwise <see langword="false"/>.</returns>
    public bool Unregister(string providerId, bool dispose = true)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        IGameAssetProvider? removed;

        lock (_gate)
        {
            if (!_providers.Remove(providerId, out removed))
                return false;
        }

        if (dispose && removed is not null)
            SafeDispose(removed);

        return true;
    }

    /// <summary>
    /// Finds the provider that owns an asset key, by the <c>&lt;providerId&gt;:</c> prefix of the key.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <param name="provider">The owning provider when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a registered provider owns the key; otherwise <see langword="false"/>.</returns>
    public bool TryFind(string key, [NotNullWhen(true)] out IGameAssetProvider? provider)
    {
        provider = null;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        int separatorIndex = key.IndexOf(':', StringComparison.Ordinal);

        if (separatorIndex <= 0)
            return false;

        string providerId = key[..separatorIndex];

        lock (_gate)
        {
            return _providers.TryGetValue(providerId, out provider);
        }
    }

    /// <summary>
    /// Looks up a single asset by its namespaced key, across every registered provider.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <param name="descriptor">The matching descriptor when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the key resolves to an asset; otherwise <see langword="false"/>.</returns>
    public bool TryDescribe(string key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor)
    {
        descriptor = null;

        return TryFind(key, out var provider) && provider.TryDescribe(key, out descriptor);
    }

    /// <summary>
    /// Lists the assets of every registered provider, optionally filtered.
    /// </summary>
    /// <param name="query">The filter to apply, or <see langword="null"/> to list everything.</param>
    /// <returns>
    /// The union of what each provider describes, in registration order. An empty list when
    /// nothing matches.
    /// </returns>
    public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null)
    {
        var results = new List<GameAssetDescriptor>();

        foreach (var provider in Providers)
        {
            var described = provider.Describe(query);

            if (described is { Count: > 0 })
                results.AddRange(described);
        }

        return results;
    }

    /// <summary>
    /// Materializes an image, sprite atlas or vector asset as a tilesheet.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <param name="options">Grid, collision and rasterization options, or <see langword="null"/> for the provider's defaults.</param>
    /// <returns>The registered tilesheet.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no registered provider holds the key.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset's kind cannot become a tilesheet, or its provider cannot materialize it.
    /// </exception>
    public Tilesheet LoadTilesheet(string key, TilesheetMaterializeOptions? options = null)
    {
        var (provider, descriptor) = Resolve(key);

        bool tilesheetKind = descriptor.Kind is GameAssetKind.Image
            or GameAssetKind.SpriteAtlas
            or GameAssetKind.Vector;

        if (provider is not ITilesheetAssetSource source || !tilesheetKind || !Supports(provider, descriptor.Kind))
            throw new UnsupportedGameAssetException(descriptor.Kind, descriptor.Key);

        return source.MaterializeTilesheet(descriptor, options);
    }

    /// <summary>
    /// Materializes an audio asset as an <see cref="AudioResource"/>.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <param name="volume">The default playback volume, where <c>1</c> is unattenuated.</param>
    /// <param name="pan">The default stereo pan, from <c>-1</c> (left) to <c>1</c> (right).</param>
    /// <returns>The registered audio resource.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no registered provider holds the key.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not audio, or its provider cannot materialize audio.
    /// </exception>
    public AudioResource LoadAudio(string key, float volume = 1.0f, float pan = 0.0f)
    {
        var (provider, descriptor) = Resolve(key);

        if (provider is not IAudioAssetSource source
            || descriptor.Kind != GameAssetKind.Audio
            || !Supports(provider, descriptor.Kind))
            throw new UnsupportedGameAssetException(descriptor.Kind, descriptor.Key);

        return source.MaterializeAudio(descriptor, volume, pan);
    }

    /// <summary>
    /// Materializes a font asset as an <see cref="SKTypeface"/>.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <returns>The registered typeface.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no registered provider holds the key.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not a font, or its provider cannot materialize fonts.
    /// </exception>
    public SKTypeface LoadFont(string key)
    {
        var (provider, descriptor) = Resolve(key);

        if (provider is not IFontAssetSource source
            || descriptor.Kind != GameAssetKind.Font
            || !Supports(provider, descriptor.Kind))
            throw new UnsupportedGameAssetException(descriptor.Kind, descriptor.Key);

        return source.MaterializeFont(descriptor);
    }

    /// <summary>
    /// Imports a tile map asset into a scene.
    /// </summary>
    /// <param name="key">The namespaced asset key.</param>
    /// <param name="scene">The scene that receives the imported layers.</param>
    /// <param name="options">Z-order, parallax, collision and filtering options, or <see langword="null"/> for the provider's defaults.</param>
    /// <returns>The layers, tilesheets, object data and warnings produced by the import.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scene"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no registered provider holds the key.</exception>
    /// <exception cref="UnsupportedGameAssetException">
    /// Thrown when the asset is not a tile map, or its provider cannot import tile maps.
    /// </exception>
    public TiledMapImport ImportTiledMap(string key, Scene scene, TiledMapImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var (provider, descriptor) = Resolve(key);

        if (provider is not ITiledMapAssetSource source
            || descriptor.Kind != GameAssetKind.TiledMap
            || !Supports(provider, descriptor.Kind))
            throw new UnsupportedGameAssetException(descriptor.Kind, descriptor.Key);

        return source.MaterializeTiledMap(descriptor, scene, options);
    }

    /// <summary>
    /// Removes every registered provider and disposes them.
    /// </summary>
    /// <remarks>
    /// Called during engine disposal. A provider that throws while being disposed is logged and
    /// skipped, so clearing always completes.
    /// </remarks>
    public void Clear()
    {
        IGameAssetProvider[] providers;

        lock (_gate)
        {
            providers = _providers.Values.ToArray();
            _providers.Clear();
        }

        foreach (var provider in providers)
            SafeDispose(provider);
    }

    private (IGameAssetProvider Provider, GameAssetDescriptor Descriptor) Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!TryFind(key, out var provider))
            throw new KeyNotFoundException(
                $"No asset provider is registered for the key '{key}'. Asset keys are of the form "
                + "'<providerId>:<provider-relative identifier>'.");

        if (!provider.TryDescribe(key, out var descriptor))
            throw new KeyNotFoundException(
                $"The asset provider '{provider.ProviderId}' does not hold an asset with the key '{key}'.");

        return (provider, descriptor);
    }

    private static bool Supports(IGameAssetProvider provider, GameAssetKind kind)
    {
        return provider.SupportedKinds is { } supportedKinds && supportedKinds.Contains(kind);
    }

    private static void SafeDispose(IGameAssetProvider provider)
    {
        string providerId = "(unknown)";

        try
        {
            providerId = provider.ProviderId;
            provider.Dispose();
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "Error disposing the asset provider '{ProviderId}'.", providerId);
        }
    }
}
