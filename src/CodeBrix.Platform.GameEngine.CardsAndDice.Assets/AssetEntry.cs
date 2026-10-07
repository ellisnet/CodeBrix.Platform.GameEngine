namespace CodeBrix.Platform.GameEngine.CardsAndDice.Assets;

/// <summary>An embedded asset and its provenance. Keys are ordinal and case-sensitive.</summary>
public sealed record AssetEntry
{
    /// <summary>Stable resource key, including category and extension.</summary>
    public required string Key { get; init; }
    /// <summary>Readable display name.</summary>
    public required string Name { get; init; }
    /// <summary>Top-level category.</summary>
    public required string Category { get; init; }
    /// <summary>Original source URL or documented local bundle path.</summary>
    public required string Source { get; init; }
    /// <summary>License identifier or public-domain designation.</summary>
    public required string License { get; init; }
    /// <summary>SHA-256 of the embedded bytes.</summary>
    public required string Sha256 { get; init; }
    /// <summary>SHA-256 of the original asset before conversion.</summary>
    public required string SourceSha256 { get; init; }
}
