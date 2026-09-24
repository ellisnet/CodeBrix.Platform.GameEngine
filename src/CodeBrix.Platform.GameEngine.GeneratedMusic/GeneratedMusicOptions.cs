using System;
using CodeBrix.Audio.MusicGeneration;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// What <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>
/// plays and how: which generator and instrument library, what to ask the generator for, and how the
/// music joins the engine's music system. Every property is optional; a new instance is
/// "play generated music, starting now".
/// </summary>
/// <remarks>
/// <para>
/// NAMES ARE RESOLVED WHEN THE MUSIC STARTS, not when the options are made, so registering a model
/// package's generator or an instrument library after building the options is fine - as long as it
/// happens before the music starts.
/// </para>
/// <para>
/// A problem found when the music starts - a generator or library that is not registered, a request
/// the generator refuses - never reaches the game as an exception: the provider reports
/// <see cref="CodeBrix.Platform.GameEngine.Audio.StreamingMusicState.Faulted"/> with the reason in its
/// <see cref="GeneratedMusicProvider.Fault"/>, and the engine plays on in silence.
/// </para>
/// </remarks>
public sealed class GeneratedMusicOptions
{
    /// <summary>The default for <see cref="TrackKey"/>: <c>"generated-music"</c>.</summary>
    public const string DefaultTrackKey = "generated-music";

    /// <summary>The default for <see cref="FadeIn"/>: one and a half seconds.</summary>
    public static readonly TimeSpan DefaultFadeIn = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// The default for <see cref="SeamCrossfade"/>: four seconds, the overlap listening settled on for
    /// both model families.
    /// </summary>
    public static readonly TimeSpan DefaultSeamCrossfade = TimeSpan.FromSeconds(4.0);

    /// <summary>
    /// The name of the registered music generator to play, matched without regard to case.
    /// </summary>
    /// <value>
    /// Default <see langword="null"/>: the FIRST generator registered with
    /// <see cref="MusicGeneratorRegistry"/> that is not one of the package's built-in replays - the
    /// model package the game registered at start-up, typically. When no such generator is
    /// registered the package's embedded replay plays instead (a recording, the same piece every
    /// time), and one Information line in the engine log says so and names the fix.
    /// </value>
    /// <remarks>
    /// This differs on purpose from <see cref="MusicGenerationOptions.Generator"/>, where
    /// <see langword="null"/> always means the embedded replay: a game that registered a model
    /// package wants to hear it.
    /// </remarks>
    public string? Generator { get; set; }

    /// <summary>
    /// The name of the registered instrument library the music is played with, matched without
    /// regard to case - for example <c>"ModestSynthGm"</c> or <c>"FluidR3Gm"</c>.
    /// </summary>
    /// <value>
    /// Default <see langword="null"/>: the registry's default library, which is the FIRST one
    /// registered unless the game moved it. Name the library whenever more than one is registered.
    /// </value>
    public string? InstrumentLibrary { get; set; }

    /// <summary>
    /// The name of a ready-made request to start from - one of the presets written for the chosen
    /// generator's family (<c>"ClubArrangement"</c>, <c>"WaltzDuetInAMinor"</c> ...), matched without
    /// regard to case. Its suggested voicing is used with it.
    /// </summary>
    /// <value>
    /// Default <see langword="null"/>: no preset - the generator writes from an empty request, in its
    /// own default manner.
    /// </value>
    /// <remarks>
    /// A name that is no preset of any family is refused at once with an
    /// <see cref="ArgumentException"/> listing the presets. A preset of another family than the
    /// generator's faults the music when it starts, with the same list.
    /// </remarks>
    public string? Preset { get; set; }

    /// <summary>
    /// Words describing the music, used INSTEAD of <see cref="Preset"/> when set.
    /// </summary>
    /// <value>Default <see langword="null"/>: no words.</value>
    /// <remarks>
    /// Words that are all known CHARACTER WORDS - "dark", "driving", "calm, minor" - are passed on as
    /// character words, which both model families turn into a tempo, a mode or both. Anything else is
    /// passed on as free text, which a generator that does not read prose refuses by name (neither
    /// built-in model family does); the refusal faults the music rather than the game.
    /// </remarks>
    public string? Text { get; set; }

    /// <summary>
    /// A tempo to hold the whole session at, in quarter notes a minute.
    /// </summary>
    /// <value>
    /// Default <see langword="null"/>: every piece plays at the tempo it was written at.
    /// </value>
    /// <remarks>
    /// It becomes the session tempo with every fresh piece carried to it, and it is never sent to the
    /// generator, so it works with any generator - the embedded replay included.
    /// </remarks>
    public double? BeatsPerMinute { get; set; }

    /// <summary>
    /// A seed, so that the same options write the same music again.
    /// </summary>
    /// <value>Default <see langword="null"/>: a different piece every time.</value>
    public int? Seed { get; set; }

    /// <summary>
    /// How long the outgoing and incoming pieces overlap where the music moves on to a FRESH piece.
    /// </summary>
    /// <value>
    /// Default <see cref="DefaultSeamCrossfade"/> (four seconds). <see cref="TimeSpan.Zero"/> joins
    /// pieces with a cut at a bar line.
    /// </value>
    /// <remarks>
    /// Only fresh seams are crossfaded, so it takes effect together with a
    /// <see cref="SegmentPriming"/> that makes fresh pieces.
    /// </remarks>
    public TimeSpan SeamCrossfade { get; set; } = DefaultSeamCrossfade;

    /// <summary>
    /// How each new segment of the music starts: carrying on from the music so far
    /// (<see cref="CodeBrix.Audio.MusicGeneration.SegmentPriming.Primed"/>), as a fresh piece
    /// (<see cref="CodeBrix.Audio.MusicGeneration.SegmentPriming.Fresh"/>), or taking turns
    /// (<see cref="CodeBrix.Audio.MusicGeneration.SegmentPriming.Alternate"/>).
    /// </summary>
    /// <value>
    /// Default <see langword="null"/>: the choice listening settled on for the generator's family -
    /// <c>Alternate</c> for SkyTNT, <c>Fresh</c> for MuPT - and the music package's own default for
    /// any other generator.
    /// </value>
    public SegmentPriming? SegmentPriming { get; set; }

    /// <summary>
    /// The music's own level, from 0 upwards, applied before the engine's music bus, ducking and
    /// master volume.
    /// </summary>
    /// <value>Default 1.</value>
    public float MasterVolume { get; set; } = 1.0F;

    /// <summary>
    /// Whether <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>
    /// also starts playing the music through the engine's music manager.
    /// </summary>
    /// <value>
    /// Default <see langword="true"/> - plug it in and it plays. <see langword="false"/> only registers
    /// the provider; the game starts it when it chooses with
    /// <c>MusicManager.Instance.PlayStreaming(fadeIn)</c>.
    /// </value>
    public bool StartImmediately { get; set; } = true;

    /// <summary>
    /// The key of the music track that <see cref="StartImmediately"/> plays, as it appears in the
    /// engine log and in <c>MusicManager.Instance.NowPlaying</c>.
    /// </summary>
    /// <value>Default <see cref="DefaultTrackKey"/>.</value>
    public string TrackKey { get; set; } = DefaultTrackKey;

    /// <summary>How long the music takes to fade in when <see cref="StartImmediately"/> starts it.</summary>
    /// <value>Default <see cref="DefaultFadeIn"/> (one and a half seconds).</value>
    public TimeSpan FadeIn { get; set; } = DefaultFadeIn;

    /// <summary>Makes an independent copy of these options.</summary>
    /// <returns>A new instance with the same values.</returns>
    public GeneratedMusicOptions Clone() => (GeneratedMusicOptions)MemberwiseClone();
}
