using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What registering one source path did: whether it was read, skipped because it was already
/// registered, missing or unreadable, and which packs it stands for.
/// </summary>
/// <remarks>
/// One of these is reported per path passed to
/// <see cref="EngineKenneyAssetsExtensions.RegisterKenneyAssets(Engine, string[])"/> or
/// <see cref="KenneyGameAssetProvider.AddNewSources(string[])"/>, in the order the paths were given.
/// </remarks>
public sealed record KenneySourceResult
{
    /// <summary>
    /// Gets the path as it was passed in.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets what registering the path did.
    /// </summary>
    public required KenneySourceStatus Status { get; init; }

    /// <summary>
    /// Gets the packs the source stands for: the packs read in this call for
    /// <see cref="KenneySourceStatus.Read"/>, the packs of the earlier registration for
    /// <see cref="KenneySourceStatus.AlreadyRegistered"/>, and none otherwise.
    /// </summary>
    public IReadOnlyList<KenneyPackSummary> Packs { get; init; } = [];

    /// <summary>
    /// Gets why the source contributed nothing, for <see cref="KenneySourceStatus.Missing"/> and
    /// <see cref="KenneySourceStatus.Unreadable"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source's packs are available to the game, which is true
    /// for <see cref="KenneySourceStatus.Read"/> and <see cref="KenneySourceStatus.AlreadyRegistered"/>.
    /// </summary>
    public bool IsAvailable => Status is KenneySourceStatus.Read or KenneySourceStatus.AlreadyRegistered;

    /// <summary>
    /// Returns a one-line description of the result, for a log.
    /// </summary>
    /// <returns>For example <c>kenney_planets.zip: Read - planets (Planets)</c>.</returns>
    public override string ToString()
    {
        string name = Path.GetFileName(
            SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string detail = IsAvailable ? string.Join(", ", Packs) : Message ?? string.Empty;

        return detail.Length == 0 ? $"{name}: {Status}" : $"{name}: {Status} - {detail}";
    }
}
