using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What a registration call did, source by source, and the provider that now serves the keys.
/// </summary>
/// <remarks>
/// Returned by <see cref="EngineKenneyAssetsExtensions.RegisterKenneyAssets(Engine, string[])"/> and
/// <see cref="KenneyGameAssetProvider.AddNewSources(string[])"/>. It is enough to log one line per
/// pack, to warn about a bundle that did not ship, and to refuse to start when nothing could be read,
/// without comparing paths by hand.
/// </remarks>
public sealed class KenneyAssetsRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyAssetsRegistration"/> class.
    /// </summary>
    /// <param name="provider">The provider the sources were registered with.</param>
    /// <param name="sources">One result per path, in the order the paths were given.</param>
    /// <param name="warnings">The warnings this call added to the provider.</param>
    internal KenneyAssetsRegistration(
        KenneyGameAssetProvider provider,
        IReadOnlyList<KenneySourceResult> sources,
        IReadOnlyList<string> warnings)
    {
        Provider = provider;
        Sources = sources;
        Warnings = warnings;
        Packs = sources
            .SelectMany(source => source.Packs)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Gets the provider that serves the registered keys.
    /// </summary>
    public KenneyGameAssetProvider Provider { get; }

    /// <summary>
    /// Gets one result per source path passed in, in the order given.
    /// </summary>
    public IReadOnlyList<KenneySourceResult> Sources { get; }

    /// <summary>
    /// Gets the packs the given paths stand for - read now or registered earlier - once each, in
    /// source order.
    /// </summary>
    public IReadOnlyList<KenneyPackSummary> Packs { get; }

    /// <summary>
    /// Gets what THIS call could not do exactly: a source that would not open and the catalog
    /// warnings of the packs it added. Empty when every source that was read catalogued cleanly.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Gets the results of the paths that contributed nothing, because they were missing or
    /// unreadable.
    /// </summary>
    public IReadOnlyList<KenneySourceResult> Unavailable =>
        Sources.Where(source => !source.IsAvailable).ToList();
}
