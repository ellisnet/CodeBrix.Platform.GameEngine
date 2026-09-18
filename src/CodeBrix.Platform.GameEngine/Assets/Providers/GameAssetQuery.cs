using System;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Filters the assets returned by <see cref="IGameAssetProvider.Describe"/>. Every criterion is
/// optional; criteria that are set are combined with a logical AND.
/// </summary>
public sealed record GameAssetQuery
{
    /// <summary>
    /// Gets the pack name an asset must belong to, or <see langword="null"/> to accept any pack.
    /// Compared case-insensitively.
    /// </summary>
    public string? Pack { get; init; }

    /// <summary>
    /// Gets the asset kind to accept, or <see langword="null"/> to accept any kind.
    /// </summary>
    public GameAssetKind? Kind { get; init; }

    /// <summary>
    /// Gets a substring that must occur in <see cref="GameAssetDescriptor.Name"/>, or
    /// <see langword="null"/> to accept any name. Compared case-insensitively.
    /// </summary>
    public string? NameContains { get; init; }

    /// <summary>
    /// Gets a prefix that <see cref="GameAssetDescriptor.Path"/> must start with, or
    /// <see langword="null"/> to accept any path. Compared case-insensitively.
    /// </summary>
    public string? PathPrefix { get; init; }

    /// <summary>
    /// Determines whether a descriptor satisfies every criterion of this query.
    /// </summary>
    /// <param name="descriptor">The descriptor to test.</param>
    /// <returns><see langword="true"/> when the descriptor matches; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    public bool Matches(GameAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (Kind is not null && descriptor.Kind != Kind.Value)
            return false;

        if (!string.IsNullOrEmpty(Pack)
            && !string.Equals(descriptor.Pack, Pack, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(NameContains)
            && descriptor.Name.IndexOf(NameContains, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (!string.IsNullOrEmpty(PathPrefix)
            && !descriptor.Path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
