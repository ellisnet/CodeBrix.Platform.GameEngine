using System;
using CodeBrix.Platform.GameEngine.Audio;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// One running generated-music session, as a game's music policy drives it: its state, what is
/// playing, how it is doing, and follow-ups. <see cref="GeneratedMusicProvider"/> implements it, and
/// <see cref="IGeneratedMusicStarter"/> hands one out, so a game's music code can be tested against a
/// scripted fake session - no model, no instrument library, no audio device - with no adapter class
/// of the game's own.
/// </summary>
/// <remarks>
/// Every member behaves exactly as the <see cref="GeneratedMusicProvider"/> member of the same name.
/// Everything here reports plain values (strings, counts, <see cref="GeneratedMusicSourceInfo"/>),
/// never the music-generation library's own types, so a fake can produce all of it.
/// </remarks>
public interface IGeneratedMusicSession
{
    /// <summary>
    /// Raised after <see cref="State"/> changes, on ANY thread (the audio fill thread included).
    /// Handlers must be quick; marshal to the engine thread before touching game state.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>The session's state: Starting while the generator loads, Playing once music is heard, and so on.</summary>
    StreamingMusicState State { get; }

    /// <summary>Why the session faulted, or <see langword="null"/>.</summary>
    Exception? Fault { get; }

    /// <summary>What the generator last threw, or <see langword="null"/> (a refused follow-up lands here while the music plays on).</summary>
    Exception? GenerationError { get; }

    /// <summary>A copy of the options the session plays with.</summary>
    GeneratedMusicOptions Options { get; }

    /// <summary>What is playing, as one line for a log or a HUD; an empty string until the music has started.</summary>
    string ActiveSourceSummary { get; }

    /// <summary>What is playing - the generator and the instrument library - or <see langword="null"/> until the music has started.</summary>
    GeneratedMusicSourceInfo? ActiveSourceInfo { get; }

    /// <summary>How many times the music has waited for the generator; 0 before there are diagnostics.</summary>
    int StarvationGapCount { get; }

    /// <summary>The session's diagnostics as one line; an empty string before there are any.</summary>
    string DiagnosticsSummary { get; }

    /// <summary>
    /// Moves the music on to a preset of the playing generator (or to what the words ask for); the new
    /// music takes over at a bar line and the music never stops meanwhile.
    /// </summary>
    /// <param name="presetOrText">A preset name, or words describing the music.</param>
    void FollowUp(string presetOrText);

    /// <summary>Moves the music on, from a set of options; see <see cref="GeneratedMusicProvider.FollowUp(GeneratedMusicOptions)"/>.</summary>
    /// <param name="options">What to change to.</param>
    void FollowUp(GeneratedMusicOptions options);
}
