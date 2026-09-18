using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)
/// <summary>
/// Describes a single asset offered by an <see cref="IGameAssetProvider"/>, without loading it.
/// </summary>
/// <remarks>
/// A descriptor is pure data: providers build the whole catalog up front (from a zip central
/// directory or a directory walk) and decode bytes only when the asset is materialized.
/// </remarks>
public sealed record GameAssetDescriptor
{
    /// <summary>
    /// Gets the identifier of the provider that owns this asset. Matches the prefix of
    /// <see cref="Key"/> up to the first colon.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Gets the namespaced key this asset is addressed by, of the form
    /// <c>&lt;providerId&gt;:&lt;provider-relative identifier&gt;</c>. It is also the key the
    /// materialized object is registered under in the matching engine registry.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the category of the asset, which decides how it can be materialized.
    /// </summary>
    public required GameAssetKind Kind { get; init; }

    /// <summary>
    /// Gets the short, human-friendly name of the asset, normally the file name without its
    /// extension.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the pack (bundle, archive or folder) the asset belongs to, or an empty
    /// string when the provider does not group its assets.
    /// </summary>
    public string Pack { get; init; } = string.Empty;

    /// <summary>
    /// Gets the provider-relative path of the asset, using forward slashes and no leading slash.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the size of the asset in bytes, or <c>0</c> when the provider cannot report a size.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets free-form metadata the provider chose to publish, such as a licence title, an atlas
    /// frame count or a tile size. Keys are compared with
    /// <see cref="System.StringComparer.OrdinalIgnoreCase"/> when the provider builds the
    /// dictionary that way; callers must not assume any particular key exists.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = EmptyProperties;

    private static readonly IReadOnlyDictionary<string, string> EmptyProperties =
        new Dictionary<string, string>(0);
}
