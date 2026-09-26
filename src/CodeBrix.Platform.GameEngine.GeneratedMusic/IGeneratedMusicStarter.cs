namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// Starts a generated-music session: the seam over
/// <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/> a game
/// substitutes in its tests. <see cref="EngineGeneratedMusicStarter"/> is the real one; a test passes a
/// fake that returns a scripted <see cref="IGeneratedMusicSession"/>.
/// </summary>
public interface IGeneratedMusicStarter
{
    /// <summary>
    /// Starts a FRESH session with these options, replacing (and disposing) any session an earlier
    /// call started - the same contract as <c>UseGeneratedMusic</c>.
    /// </summary>
    /// <param name="options">What to play and how.</param>
    /// <returns>The new session.</returns>
    IGeneratedMusicSession Start(GeneratedMusicOptions options);
}
