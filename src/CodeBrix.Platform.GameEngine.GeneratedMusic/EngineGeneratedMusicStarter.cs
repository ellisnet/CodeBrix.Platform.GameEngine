using System;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// The real <see cref="IGeneratedMusicStarter"/>: calls
/// <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/> and
/// returns the <see cref="GeneratedMusicProvider"/> it created.
/// </summary>
/// <remarks>
/// Created with no engine, it reads <see cref="Engine.Instance"/> each time <see cref="Start"/> is
/// called, so a game can create it (and the music code that holds it) before the engine starts.
/// </remarks>
public sealed class EngineGeneratedMusicStarter : IGeneratedMusicStarter
{
    private readonly Engine? _engine;

    /// <summary>Creates a starter for the running engine (<see cref="Engine.Instance"/>, read when <see cref="Start"/> is called).</summary>
    public EngineGeneratedMusicStarter()
    { }

    /// <summary>Creates a starter for one engine.</summary>
    /// <param name="engine">The engine to play the music in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is null.</exception>
    public EngineGeneratedMusicStarter(Engine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The options name no preset of any family, or the master volume is negative.</exception>
    public IGeneratedMusicSession Start(GeneratedMusicOptions options) => (_engine ?? Engine.Instance).UseGeneratedMusic(options);
}
