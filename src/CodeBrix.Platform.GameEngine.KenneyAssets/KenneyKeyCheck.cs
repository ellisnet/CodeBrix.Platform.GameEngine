using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// The result of checking a list of asset keys against a provider's catalog: which keys resolve,
/// which do not, and what kinds the resolved ones are.
/// </summary>
/// <remarks>
/// Returned by <see cref="KenneyGameAssetProvider.CheckKeys(IEnumerable{string})"/>. A game's test
/// asserts <see cref="MissingKeys"/> is empty (and, where it matters, a key's
/// <see cref="KenneyKeyStatus.Kind"/>); a start-up log writes <see cref="ToString"/> and one line per
/// missing key.
/// </remarks>
public sealed class KenneyKeyCheck
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KenneyKeyCheck"/> class.
    /// </summary>
    /// <param name="keys">One status per key, in the order the keys were given.</param>
    internal KenneyKeyCheck(IReadOnlyList<KenneyKeyStatus> keys)
    {
        Keys = keys;
        MissingKeys = keys.Where(status => !status.Found).Select(status => status.Key).ToList();

        Dictionary<GameAssetKind, int> countsByKind = [];
        foreach (KenneyKeyStatus status in keys.Where(status => status.Found))
        {
            countsByKind[status.Kind] = countsByKind.GetValueOrDefault(status.Kind) + 1;
        }

        CountsByKind = countsByKind;
        TotalSizeBytes = keys.Sum(status => status.SizeBytes);
    }

    /// <summary>
    /// Gets one status per key checked, in the order the keys were given (a key given twice appears
    /// twice).
    /// </summary>
    public IReadOnlyList<KenneyKeyStatus> Keys { get; }

    /// <summary>
    /// Gets the keys that did not resolve, in the order given. Empty when every key resolved.
    /// </summary>
    public IReadOnlyList<string> MissingKeys { get; }

    /// <summary>
    /// Gets a value indicating whether every key resolved.
    /// </summary>
    public bool AllFound => MissingKeys.Count == 0;

    /// <summary>
    /// Gets how many of the resolved keys there are of each kind. A kind with no resolved key is
    /// absent rather than present with a zero.
    /// </summary>
    public IReadOnlyDictionary<GameAssetKind, int> CountsByKind { get; }

    /// <summary>
    /// Gets the total size, in bytes, of the files the resolved keys name inside their packs.
    /// </summary>
    public long TotalSizeBytes { get; }

    /// <summary>
    /// Gets the status of one checked key.
    /// </summary>
    /// <param name="key">The key, compared case-insensitively.</param>
    /// <returns>The first status reported for that key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the key was not among those checked.</exception>
    public KenneyKeyStatus this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);

            return Keys.FirstOrDefault(status => string.Equals(status.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"The key '{key}' was not among the keys checked.");
        }
    }

    /// <summary>
    /// Returns a one-line summary of the check, for a log.
    /// </summary>
    /// <returns>
    /// For example <c>72 key(s) checked - Audio 36, Font 2, SpriteAtlas 2 - 0 missing, 9.1 MB</c>.
    /// </returns>
    public override string ToString()
    {
        string byKind = string.Join(
            ", ",
            CountsByKind
                .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        string megabytes = (TotalSizeBytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);

        return $"{Keys.Count.ToString(CultureInfo.InvariantCulture)} key(s) checked - " +
               $"{(byKind.Length == 0 ? "none found" : byKind)} - " +
               $"{MissingKeys.Count.ToString(CultureInfo.InvariantCulture)} missing, {megabytes} MB";
    }
}
