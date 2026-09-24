using System;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// What <see cref="GeneratedMusicProvider.Render"/> pulls from and reads its state out of: a playing
/// music session and its renderer in the game, a scripted source in tests.
/// </summary>
internal interface IMusicRenderSource
{
    /// <summary>Whether the session is still playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Whether the play head has caught up with the music and is waiting for more.</summary>
    bool IsStarved { get; }

    /// <summary>Whether the music has been played to its end.</summary>
    bool IsFinished { get; }

    /// <summary>What the generator last threw, or null.</summary>
    Exception? GenerationError { get; }

    /// <summary>Renders the next frames into two planes of equal length.</summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    void Render(Span<float> left, Span<float> right);
}

/// <summary>The real <see cref="IMusicRenderSource"/>: a started <see cref="MusicSession"/> and its renderer.</summary>
internal sealed class SessionRenderSource : IMusicRenderSource
{
    private readonly MusicSession _session;
    private readonly IAudioRenderer _renderer;

    /// <summary>Wraps a session that has been played, with the renderer it prepared.</summary>
    /// <param name="session">The playing session.</param>
    /// <param name="renderer">The session's renderer.</param>
    internal SessionRenderSource(MusicSession session, IAudioRenderer renderer)
    {
        _session = session;
        _renderer = renderer;
    }

    /// <inheritdoc />
    public bool IsPlaying => _session.IsPlaying;

    /// <inheritdoc />
    public bool IsStarved => _session.IsStarved;

    /// <inheritdoc />
    public bool IsFinished => _session.IsFinished;

    /// <inheritdoc />
    public Exception? GenerationError => _session.GenerationError;

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right) => _renderer.Render(left, right);
}
