using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// A source of game assets that the engine can list and, through the capability interfaces,
/// materialize into engine objects.
/// </summary>
/// <remarks>
/// <para>
/// This is the minimal contract: a provider catalogs what it holds and can hand out raw bytes.
/// Turning an asset into an engine object is opt-in through the capability interfaces
/// <see cref="ITilesheetAssetSource"/>, <see cref="IAudioAssetSource"/>,
/// <see cref="IFontAssetSource"/> and <see cref="ITiledMapAssetSource"/>; a provider implements
/// only those it supports and reports the matching kinds in <see cref="SupportedKinds"/>.
/// </para>
/// <para>
/// Providers are registered with <see cref="GameAssetProviderRegistry"/> (reachable as
/// <c>Engine.Instance.Managers.AssetProviders</c>) and are disposed when they are replaced,
/// unregistered, or when the engine shuts down. Registration is a runtime arrangement and is not
/// part of saved engine state.
/// </para>
/// <para>
/// Implementations must be safe to call from more than one thread: the registry serializes its
/// own bookkeeping but calls provider methods outside its lock.
/// </para>
/// </remarks>
public interface IGameAssetProvider : IDisposable
{
    /// <summary>
    /// Gets the identifier that namespaces every key this provider owns. Keys take the form
    /// <c>&lt;ProviderId&gt;:&lt;provider-relative identifier&gt;</c>, so the identifier itself must
    /// not contain a colon.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets the asset kinds this provider can materialize. Kinds it merely lists (for example
    /// <see cref="GameAssetKind.Model3D"/>) are not included.
    /// </summary>
    IReadOnlySet<GameAssetKind> SupportedKinds { get; }

    /// <summary>
    /// Lists the assets this provider holds, optionally filtered.
    /// </summary>
    /// <param name="query">The filter to apply, or <see langword="null"/> to list everything.</param>
    /// <returns>The matching descriptors; an empty list when nothing matches.</returns>
    IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null);

    /// <summary>
    /// Looks up a single asset by its namespaced key.
    /// </summary>
    /// <param name="key">The namespaced key, including the <c>&lt;ProviderId&gt;:</c> prefix.</param>
    /// <param name="descriptor">The matching descriptor when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the key is known to this provider; otherwise <see langword="false"/>.</returns>
    bool TryDescribe(string key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor);

    /// <summary>
    /// Opens the raw bytes of an asset. This is the escape hatch for callers that want to decode
    /// an asset themselves.
    /// </summary>
    /// <param name="descriptor">A descriptor this provider issued.</param>
    /// <returns>A readable stream positioned at the start of the asset; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the descriptor does not belong to this provider.</exception>
    Stream OpenRaw(GameAssetDescriptor descriptor);
}
