namespace CodeBrix.Platform.GameEngine.KenneyAssets;

/// <summary>
/// What registering one zip file or folder did, as reported by
/// <see cref="KenneySourceResult.Status"/>.
/// </summary>
public enum KenneySourceStatus
{
    /// <summary>
    /// The source was opened and its packs were added to the provider in this call.
    /// </summary>
    Read,

    /// <summary>
    /// The provider already held a source at this path, so nothing was added a second time; the
    /// result lists the packs that earlier registration produced.
    /// </summary>
    AlreadyRegistered,

    /// <summary>
    /// No file or folder exists at the path (or the path was blank), so it contributed nothing.
    /// </summary>
    Missing,

    /// <summary>
    /// A file or folder exists at the path but could not be read, so it contributed nothing.
    /// </summary>
    Unreadable,
}
