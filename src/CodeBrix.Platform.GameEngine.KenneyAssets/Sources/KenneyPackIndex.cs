using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

/// <summary>
/// Every pack of one provider, with the key-to-asset lookup the provider answers
/// <see cref="IGameAssetProvider.TryDescribe"/> and <see cref="IGameAssetProvider.Describe"/> from.
/// </summary>
/// <remarks>
/// <para>
/// Two packs can want the same slug - two versions of one bundle, or the same pack as a zip and as a
/// folder - so the index makes the slug unique by appending <c>-2</c>, <c>-3</c> and so on to the later
/// arrival. It does that BEFORE any key of that pack is built, because the slug is part of every key the
/// pack owns.
/// </para>
/// <para>
/// Packs may be added after reading has begun, because registering more asset sources adds to the
/// provider that is already serving the game. Adding therefore builds a fresh lookup and swaps it in
/// rather than editing the live one, so a read in flight sees either the old catalog or the new one and
/// never a half-built dictionary.
/// </para>
/// </remarks>
internal sealed class KenneyPackIndex
{
    /// <summary>
    /// The provider identifier asset keys carry unless a game asks for another one.
    /// </summary>
    public const string DefaultProviderId = "kenney";

    private readonly object _gate = new();
    private readonly List<KenneyPack> _packs = [];
    private readonly List<string> _warnings = [];

    private Dictionary<string, KenneyAssetEntry> _entriesByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private GameAssetDescriptor[] _descriptors = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyPackIndex"/> class.
    /// </summary>
    /// <param name="providerId">The provider identifier that namespaces every key in the index.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId"/> is null, empty, whitespace, or contains a colon.</exception>
    public KenneyPackIndex(string providerId = DefaultProviderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId.Contains(':'))
        {
            throw new ArgumentException(
                "A provider identifier cannot contain a colon, because a colon separates it from the rest " +
                $"of a key: '{providerId}'.",
                nameof(providerId));
        }

        ProviderId = providerId;
    }

    /// <summary>
    /// Gets the provider identifier every key in the index carries.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the packs in the index, in the order they were added.
    /// </summary>
    public IReadOnlyList<KenneyPack> Packs
    {
        get
        {
            lock (_gate) { return _packs.ToList(); }
        }
    }

    /// <summary>
    /// Gets a descriptor for every addressable asset, pack by pack in the order the packs were added and
    /// by path within a pack.
    /// </summary>
    public IReadOnlyList<GameAssetDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Gets the number of addressable assets in the index.
    /// </summary>
    public int Count => _descriptors.Length;

    /// <summary>
    /// Gets the messages describing anything that could not be catalogued exactly: the warnings of every
    /// pack, plus any key that could not be made unique.
    /// </summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate) { return _warnings.ToList(); }
        }
    }

    /// <summary>
    /// Adds every pack of an asset source, then rebuilds the lookup.
    /// </summary>
    /// <param name="source">The opened source whose packs are added.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public void AddSource(KenneyAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            foreach (KenneyPack pack in source.Packs) { AddPackLocked(pack); }

            //A source's warnings already include those of every pack it holds
            _warnings.AddRange(source.Warnings);
            Rebuild();
        }
    }

    /// <summary>
    /// Adds one pack, giving it a unique slug, then rebuilds the lookup.
    /// </summary>
    /// <param name="pack">The pack to add.</param>
    /// <returns>The slug the pack ended up with, which differs from the one it arrived with when that was taken.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pack"/> is null.</exception>
    public string Add(KenneyPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        lock (_gate)
        {
            AddPackLocked(pack);
            _warnings.AddRange(pack.Warnings);
            Rebuild();
            return pack.Slug;
        }
    }

    /// <summary>
    /// Looks up one asset by key.
    /// </summary>
    /// <param name="key">
    /// The asset's key. A key with this index's <c>&lt;providerId&gt;:</c> prefix and the bare
    /// <c>&lt;pack-slug&gt;/&lt;path&gt;</c> form are both accepted; a key carrying another provider's
    /// prefix is not found.
    /// </param>
    /// <param name="entry">The asset when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the key is in the index; otherwise <see langword="false"/>.</returns>
    public bool TryGetEntry(string? key, [NotNullWhen(true)] out KenneyAssetEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(key)) { return false; }

        return _entriesByKey.TryGetValue(Qualify(key), out entry);
    }

    /// <summary>
    /// Looks up one asset's descriptor by key.
    /// </summary>
    /// <param name="key">The asset's key, in either of the forms <see cref="TryGetEntry"/> accepts.</param>
    /// <param name="descriptor">The descriptor when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the key is in the index; otherwise <see langword="false"/>.</returns>
    public bool TryGetDescriptor(string? key, [NotNullWhen(true)] out GameAssetDescriptor? descriptor)
    {
        if (TryGetEntry(key, out KenneyAssetEntry? entry))
        {
            descriptor = entry.ToDescriptor(ProviderId);
            return true;
        }

        descriptor = null;
        return false;
    }

    /// <summary>
    /// Lists the assets in the index, optionally filtered.
    /// </summary>
    /// <param name="query">The filter to apply, or <see langword="null"/> to list everything.</param>
    /// <returns>The matching descriptors, in index order; an empty list when nothing matches.</returns>
    public IReadOnlyList<GameAssetDescriptor> Describe(GameAssetQuery? query = null)
    {
        GameAssetDescriptor[] descriptors = _descriptors;
        if (query is null) { return descriptors; }

        List<GameAssetDescriptor> matches = [];
        foreach (GameAssetDescriptor descriptor in descriptors)
        {
            if (query.Matches(descriptor)) { matches.Add(descriptor); }
        }

        return matches;
    }

    private void AddPackLocked(KenneyPack pack)
    {
        pack.AssignSlug(UniqueSlug(pack.Slug));
        _packs.Add(pack);
    }

    private string UniqueSlug(string slug)
    {
        if (!_packs.Any(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            return slug;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{slug}-{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!_packs.Any(p => p.Slug.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    //Builds a whole new lookup and descriptor list, then swaps them in
    private void Rebuild()
    {
        Dictionary<string, KenneyAssetEntry> entriesByKey = new(StringComparer.OrdinalIgnoreCase);
        List<GameAssetDescriptor> descriptors = [];

        foreach (KenneyPack pack in _packs)
        {
            foreach (KenneyAssetEntry entry in pack.Entries)
            {
                string key = entry.GetKey(ProviderId);
                if (entriesByKey.TryGetValue(key, out KenneyAssetEntry? existing))
                {
                    string warning =
                        $"'{entry.Path}' and '{existing.Path}' of pack '{pack.Slug}' both want the key " +
                        $"'{key}'; only the first is addressable.";
                    if (!_warnings.Contains(warning)) { _warnings.Add(warning); }
                    continue;
                }

                entriesByKey[key] = entry;
                descriptors.Add(entry.ToDescriptor(ProviderId));
            }
        }

        _entriesByKey = entriesByKey;
        _descriptors = [.. descriptors];
    }

    private string Qualify(string key) =>
        key.Contains(':') ? key : $"{ProviderId}:{key}";
}
