using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// An endless source of music the engine PULLS from — generated music, a procedural score, anything
/// that produces audio as it goes rather than from a finished file. Play one through a
/// <see cref="StreamingMusicTrack"/>, usually by registering it with
/// <see cref="StreamingMusicRegistry"/> and calling <see cref="MusicManager.PlayStreaming"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE CONTRACT, from the provider's side:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="Start"/> RETURNS PROMPTLY. Anything slow (loading a model, preparing the first piece)
/// happens in the background; until audio is ready <see cref="State"/> is
/// <see cref="StreamingMusicState.Starting"/> and <see cref="Render"/> returns 0.
/// </description></item>
/// <item><description>
/// <see cref="Render"/> is called on the AUDIO FILL THREAD. It must be fast, must not allocate and
/// must never block. Returning fewer frames than asked is allowed at any time: the engine fills the
/// rest with silence and keeps pulling.
/// </description></item>
/// <item><description>
/// GAPS ARE NORMAL. <see cref="StreamingMusicState.Starting"/> and
/// <see cref="StreamingMusicState.Starved"/> are ordinary states; the engine never stops a stream
/// because it went quiet.
/// </description></item>
/// <item><description>
/// A PROVIDER NEVER THROWS INTO THE ENGINE. A failure sets <see cref="State"/> to
/// <see cref="StreamingMusicState.Faulted"/> and <see cref="Fault"/> to the exception, and raises
/// <see cref="StateChanged"/>. (The engine still guards every call, and treats an exception escaping
/// <see cref="Render"/> as a fault, but that is a safety net, not the contract.)
/// </description></item>
/// </list>
/// <para>
/// And from the engine's side: <see cref="Render"/> is never called concurrently with itself, never
/// before <see cref="Start"/> has returned and never after <see cref="Stop"/> has returned. A stream
/// is always <see cref="Stop"/>ped before it is started again, so <see cref="Start"/> after
/// <see cref="Stop"/> begins a fresh timeline. The engine never disposes a provider — whoever created
/// it does.
/// </para>
/// </remarks>
public interface IStreamingMusicProvider : IDisposable
{
    /// <summary>A short name for logs and a game's display, for example "Generated music".</summary>
    string Name { get; }

    /// <summary>
    /// One line describing what is playing, composed by the provider. The engine treats it as opaque
    /// text and only ever logs or displays it.
    /// </summary>
    string Description { get; }

    /// <summary>Where the provider is in its life. See <see cref="StreamingMusicState"/>.</summary>
    StreamingMusicState State { get; }

    /// <summary>
    /// What went wrong, when <see cref="State"/> is <see cref="StreamingMusicState.Faulted"/>;
    /// otherwise <see langword="null"/>. Reported here, never thrown into the engine.
    /// </summary>
    Exception? Fault { get; }

    /// <summary>
    /// Raised after <see cref="State"/> changes. May be raised on any thread, including the audio fill
    /// thread from inside <see cref="Render"/>; handlers must be quick.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Starts the stream at the output's format. Must return promptly: slow preparation continues in
    /// the background while <see cref="State"/> reports <see cref="StreamingMusicState.Starting"/>.
    /// </summary>
    /// <param name="sampleRate">
    /// The sample rate, in Hz, the engine's output runs at — the pinned
    /// <see cref="AudioSystem.DeviceSampleRate"/> when the game called
    /// <see cref="AudioSystem.Initialize"/>. Render at exactly this rate.
    /// </param>
    /// <param name="channels">
    /// The output's channel count (1 or 2). For information only: <see cref="Render"/> always
    /// produces two planes and the engine down-mixes for a mono output.
    /// </param>
    void Start(int sampleRate, int channels);

    /// <summary>
    /// Stops the stream. Must return promptly and must be safe to call when already stopped. A later
    /// <see cref="Start"/> begins a fresh timeline.
    /// </summary>
    void Stop();

    /// <summary>
    /// Renders the next frames of the stream as two planes. Called on the AUDIO FILL THREAD: fast, no
    /// allocations, never blocking.
    /// </summary>
    /// <param name="left">The left channel, one sample per frame; its length is the frames wanted.</param>
    /// <param name="right">The right channel; the same length as <paramref name="left"/>.</param>
    /// <returns>
    /// The number of frames written, from 0 to <paramref name="left"/>'s length. Fewer than asked
    /// (including 0 while starting or starved) is fine: the engine fills the rest with silence and
    /// keeps pulling.
    /// </returns>
    int Render(Span<float> left, Span<float> right);
}
