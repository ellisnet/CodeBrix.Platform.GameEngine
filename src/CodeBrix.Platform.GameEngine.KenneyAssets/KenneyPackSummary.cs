using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Assets.Providers;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What one registered Kenney pack holds, without listing its assets: the name it is addressed by,
/// the name and licence its licence file gives it, where it was read from, and how much is in it.
/// </summary>
/// <remarks>
/// This is what <see cref="KenneyGameAssetProvider.Packs"/> hands out, and it is enough to build an
/// asset browser's pack list or to credit the packs a game shipped. The assets themselves are listed
/// with <see cref="KenneyGameAssetProvider.Describe"/>, filtered by
/// <see cref="GameAssetQuery.Pack"/> = <see cref="Slug"/>.
/// </remarks>
public sealed record KenneyPackSummary
{
    /// <summary>
    /// Gets the lower-case slug that namespaces the pack's asset keys, which is the part of a key
    /// between the provider identifier and the asset's own path.
    /// </summary>
    /// <remarks>
    /// The slug comes from the pack's licence title, or from its file or folder name when it has no
    /// licence file. Two packs wanting one slug are told apart by a <c>-2</c>, <c>-3</c> suffix on the
    /// later arrival, so a slug is unique within a provider.
    /// </remarks>
    public required string Slug { get; init; }

    /// <summary>
    /// Gets the pack's display name: the title its licence file states, or its file or folder name
    /// prettified when it states none.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the pack version its licence file states, or <see langword="null"/> when it states none.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the first line of the pack's licence file, as written, or <see langword="null"/> when the
    /// pack has no licence file. This is the line to credit the pack by.
    /// </summary>
    public string? LicenseTitle { get; init; }

    /// <summary>
    /// Gets the path of the zip file or folder the pack was read from.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets the number of assets of this pack that can be addressed by key.
    /// </summary>
    /// <remarks>
    /// This counts what <see cref="KenneyGameAssetProvider.Describe"/> lists for the pack, which is
    /// fewer than the files in the bundle: a sprite atlas and its sheet image are one asset, a tile
    /// set document is not an asset at all, and a file that cannot be given a key of its own is
    /// reported in <see cref="KenneyGameAssetProvider.Warnings"/> instead.
    /// </remarks>
    public int AssetCount { get; init; }

    /// <summary>
    /// Gets the number of the pack's addressable assets the provider can turn into an engine object.
    /// The rest are listed for discovery only.
    /// </summary>
    public int MaterializableAssetCount { get; init; }

    /// <summary>
    /// Gets how many of the pack's addressable assets there are of each kind. A kind the pack holds
    /// nothing of is absent rather than present with a zero.
    /// </summary>
    public IReadOnlyDictionary<GameAssetKind, int> CountsByKind { get; init; } = EmptyCounts;

    /// <summary>
    /// Returns the pack's slug and display name.
    /// </summary>
    /// <returns>A short description of the pack.</returns>
    public override string ToString() => $"{Slug} ({DisplayName})";

    private static readonly IReadOnlyDictionary<GameAssetKind, int> EmptyCounts =
        new Dictionary<GameAssetKind, int>(0);
}
