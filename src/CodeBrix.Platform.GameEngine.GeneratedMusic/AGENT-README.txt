================================================================================
AGENT-README: CodeBrix.Platform.GameEngine.GeneratedMusic
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.GameEngine.GeneratedMusic gives a CodeBrix.Platform.GameEngine
game ENDLESS, GENERATED MUSIC WITH ONE CALL. Target: .NET 10 or later.

The music comes from CodeBrix.Audio.MusicGeneration: a model (or, with no model,
a recorded piece that ships inside that package) writes MIDI while the game
runs, and an instrument library plays it. This package is the bridge into the
engine: it wraps a MusicGeneration session in the engine's STREAMING MUSIC
PROVIDER seam, so the music plays on the engine's MUSIC BUS — fades, crossfades,
ducking, stingers, the music volume slider and the global engine pause all work
on it exactly as they do on a music file. The session opens NO audio device of
its own: the engine pulls the music through its one audio output.

    using CodeBrix.Audio.ModestSynth;                    // the instruments
    using CodeBrix.Audio.MusicGeneration.SkyTNT;         // a model
    using CodeBrix.Platform.GameEngine.GeneratedMusic;   // this package

    GeneralMidiInstrumentLibrary.Register();             // once, at start-up
    SkyTNTModel.Register();                              // once, at start-up

    Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions
    {
        Preset = "ClubArrangement",
    });

That is the whole of it: the music fades in and never ends.

THIS PACKAGE REGISTERS NOTHING. Which instruments and which model a game uses
are the game's decisions, made with one Register() line each (see
INSTALLATION). With no model registered the music package's EMBEDDED REPLAY
plays — a recording, the same piece every time — and the engine log says so in
one line that names the fix. With no instrument library registered there is no
sound at all, the provider reports a fault naming the fix, and the game runs on.

OTHER PACKAGES FROM THE SAME REPOSITORY
---------------------------------------
  CodeBrix.Platform.GameEngine.MitLicenseForever — the game engine itself
  (engine core + CodeBrix.Platform host layer). License: MIT. It is a hard
  dependency of this package; see AGENT-README.txt in the repository root for
  everything about the music system (MusicManager, tracks, ducking, stingers)
  and for the STREAMING MUSIC PROVIDERS section that documents the seam this
  package plugs into. THIS file covers generated music only.

  CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever — Kenney asset
  bundles, including their sound effects (optional add-on); see
  src/CodeBrix.Platform.GameEngine.KenneyAssets/AGENT-README.txt.

  CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever — gamepads (optional
  add-on); see src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt.

INSTALLATION
============
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever

The assembly and namespace are CodeBrix.Platform.GameEngine.GeneratedMusic
(WITHOUT the license suffix).

License: MIT.

NuGet dependencies (pulled in automatically, listed by id):
    CodeBrix.Platform.GameEngine.MitLicenseForever     -- the engine + host
    CodeBrix.Audio.MusicGeneration.MitLicenseForever   -- the music generation
        (which brings CodeBrix.Audio, CodeBrix.Audio.ModestSynth - the
        synthesized General MIDI instrument library - and the model runner)

THE THREE PACKAGE REFERENCES A GAME TYPICALLY ADDS, and the Register() line
each one needs:

    CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever
        (nothing to register: this package is the one UseGeneratedMusic call)

    CodeBrix.Audio.MusicGeneration.SkyTNT.ApacheLicenseForever   a model that
        writes electronica - drums, bass, leads, pads
        SkyTNTModel.Register();        using CodeBrix.Audio.MusicGeneration.SkyTNT;
    and/or
    CodeBrix.Audio.MusicGeneration.MuPT.ApacheLicenseForever     a model that
        writes folk and classical tunes in parts, with no drums
        MuPTModel.Register();          using CodeBrix.Audio.MusicGeneration.MuPT;

    CodeBrix.Audio.Samples.FluidR3Gm.MitLicenseForever           OPTIONAL:
        recorded General MIDI instruments instead of synthesized ones
        FluidR3GmInstrumentLibrary.Register();
                                       using CodeBrix.Audio.Samples.FluidR3Gm;

And ALWAYS one instrument library - the synthesized one needs no extra package:

    GeneralMidiInstrumentLibrary.Register();   using CodeBrix.Audio.ModestSynth;

Every Register() is idempotent and LOADS NOTHING: a model loads when the music
first asks for it (in the background - the game never waits for it).

The engine dependency is an ORDINARY PackageReference on a published version:
this package is versioned and published independently of the engine package,
and the two do NOT share a version number. Take the latest of each.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.GameEngine.GeneratedMusic;   // UseGeneratedMusic,
                                                         // GeneratedMusicOptions,
                                                         // GeneratedMusicProvider
    using CodeBrix.Platform.GameEngine.Audio;            // MusicManager,
                                                         // StreamingMusicState,
                                                         // StreamingMusicTrack
    using CodeBrix.Audio.ModestSynth;                    // GeneralMidiInstrumentLibrary
    using CodeBrix.Audio.MusicGeneration;                // ActiveMusicSource,
                                                         // MusicDiagnostics,
                                                         // SegmentPriming,
                                                         // MusicGenerationException
    using CodeBrix.Audio.MusicGeneration.Presets;        // SkyTNTPresets, MuPTPresets

STARTING THE MUSIC
==================
AT START-UP, before the music starts: pin the engine's audio format, register an
instrument library and a model, then make the one call.

    AudioSystem.Initialize(48000, 2);               // the engine's output format
    GeneralMidiInstrumentLibrary.Register();        // "ModestSynthGm"
    SkyTNTModel.Register();                         // "SkyTNT"

    GeneratedMusicProvider music = Engine.Instance.UseGeneratedMusic(
        new GeneratedMusicOptions
        {
            Generator         = SkyTNTModel.GeneratorName,   // optional
            InstrumentLibrary = "ModestSynthGm",             // name it!
            Preset            = "ClubArrangement",
        });

UseGeneratedMusic():
  1. creates a GeneratedMusicProvider with a copy of the options,
  2. registers it as the engine's streaming music provider
     (Engine.Instance.Managers.StreamingMusic),
  3. when StartImmediately is on (the default) plays it through
     MusicManager.Instance as a StreamingMusicTrack keyed TrackKey, fading in
     over FadeIn,
  4. returns the provider, for follow-ups, diagnostics and state.

The music is rendered at the engine output's own sample rate (the one
AudioSystem.Initialize pinned). While the model loads and writes its first
bars the engine plays silence — that is normal and never an error.

TO START IT LATER instead (a title screen that is silent until the player
presses a key):

    Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions
    {
        Preset = "AmbientElectronica",
        StartImmediately = false,              // registered, not playing
    });

    // ... later:
    MusicManager.Instance.PlayStreaming(TimeSpan.FromSeconds(2));

PlayStreaming is idempotent while the music streams, so calling it on every
screen change keeps the music going rather than restarting it.

EVERYTHING ELSE IS THE ENGINE'S MUSIC SYSTEM. Stop(fadeOut), Pause/Resume,
PushDuck under dialogue, PlayStinger for a level-up jingle, the music volume
slider and the global engine pause all apply to generated music unchanged — see
the MUSIC section of the engine's AGENT-README.txt.

GeneratedMusicOptions
=====================
Every property is optional. Names are resolved WHEN THE MUSIC STARTS, not when
the options are built.

  Property           Type            Default            Meaning
  -----------------  --------------  -----------------  ---------------------------
  Generator          string?         null               the registered generator to
                                                        play; null = the FIRST
                                                        registered generator that is
                                                        not a built-in replay, and
                                                        the embedded replay when
                                                        there is none
  InstrumentLibrary  string?         null               the registered instrument
                                                        library; null = the
                                                        registry's default, which is
                                                        the FIRST one registered
  Preset             string?         null               a preset of the generator's
                                                        family (see THE PRESETS);
                                                        its suggested voicing comes
                                                        with it
  Text               string?         null               words; wins over Preset (see
                                                        WORDS)
  BeatsPerMinute     double?         null               hold the whole session at
                                                        this tempo (never sent to
                                                        the generator, so it works
                                                        with any generator)
  Seed               int?            null               the same options write the
                                                        same music again
  SeamCrossfade      TimeSpan        4 s                overlap where the music
                                                        moves on to a FRESH piece;
                                                        TimeSpan.Zero = a cut at a
                                                        bar line
  SegmentPriming     SegmentPriming? null               Primed / Fresh / Alternate;
                                                        null = what listening
                                                        settled on per family:
                                                        Alternate for SkyTNT, Fresh
                                                        for MuPT, the music
                                                        package's default otherwise
  MasterVolume       float           1                  the music's own level, under
                                                        the engine's music bus,
                                                        ducking and master volume
  StartImmediately   bool            true               also play it now through
                                                        MusicManager
  TrackKey           string          "generated-music"  the music track's key
  FadeIn             TimeSpan        1.5 s              the fade-in StartImmediately
                                                        uses

  Clone()            an independent copy.
  Constants: DefaultTrackKey, DefaultFadeIn, DefaultSeamCrossfade.

WHICH MUSIC PLAYS
=================
THE GENERATOR. Generator names one; left null, the FIRST registered generator
that is not one of the music package's built-in replays plays - so a game that
registered one model package hears that model without naming it. (This is on
purpose unlike MusicGeneration's own options, where null always means the
embedded replay.) Name the generator whenever more than one model is
registered.

NO MODEL = THE EMBEDDED REPLAY. With no model registered and none named, the
music package's embedded replay plays: a recording of what a model once wrote,
the same piece every time, looping. It is the expected degraded path, not an
error, and ONE Information line in the engine log says so and names the fix
(register a model package's generator). The parts of the options meant for a
model - Preset, Text, Seed - are left out on that path, and the log line lists
them; BeatsPerMinute still applies. provider.ActiveSource.IsReplay is true.

THE INSTRUMENT LIBRARY. InstrumentLibrary names one; left null, the registry's
DEFAULT plays, and THE DEFAULT IS THE FIRST LIBRARY REGISTERED - there is no
priority. A game that registers both GeneralMidiInstrumentLibrary and
FluidR3GmInstrumentLibrary on different start-up paths can sound different
depending on which ran first. NAME THE LIBRARY whenever more than one is
registered ("ModestSynthGm" or "FluidR3Gm"), or move the default with
InstrumentLibraryRegistry.SetDefault(name).

NO LIBRARY = SILENCE AND A FAULT. With no instrument library registered the
music cannot play: the provider reports StreamingMusicState.Faulted with a Fault
whose message names GeneralMidiInstrumentLibrary.Register(), the engine log
carries the same line, and the game keeps running in silence.

THE PRESETS
===========
A preset is a ready-made request written for one generator FAMILY, with the
voicing its music was rated through. Names are matched without regard to case.

  SkyTNT family (electronica, with drums unless the preset says otherwise):
    FourOnTheFloor      a kick on every beat under a synthesized bass
    ClubArrangement     drums, synth bass, sawtooth lead, warm pad
    AmbientElectronica  two pads and a bell-like lead, no percussion

  MuPT family (folk and classical, in parts, NO DRUMS AT ALL):
    ReelInGMinor, JigInD, WaltzInAMinor, AirInDMixolydian, HornpipeInG,
    OpenInC, DuetInC, WaltzDuetInAMinor
    (the two duets are the ones that write two-part music)

SkyTNTPresets.All / MuPTPresets.All (CodeBrix.Audio.MusicGeneration.Presets)
list them at run time.

A NAME THAT IS NO PRESET OF ANY FAMILY is refused AT ONCE - by the
GeneratedMusicProvider constructor, and so by UseGeneratedMusic - with an
ArgumentException listing every preset. A preset of ANOTHER family than the
generator's (a SkyTNT preset with MuPT playing) faults the music when it starts,
with the presets that fit in the message.

WORDS
=====
Text is used INSTEAD of Preset when set.

  * Words that are ALL known CHARACTER WORDS - "dark", "driving", "calm, minor",
    "heroic" - are passed on as character words, which both model families turn
    into a tempo, a mode or both. The vocabulary is
    CodeBrix.Audio.MusicGeneration.Generation.MusicCharacterWords.Known.
  * Anything else is passed on as FREE TEXT. Neither built-in model family reads
    prose, so free text is REFUSED BY NAME: at start-up that faults the music
    (never the game); in a follow-up it throws and the music plays on.

CHANGING THE MUSIC WHILE THE GAME RUNS
======================================
TWO WAYS, for two different needs.

1. THE SAME GENERATOR, NEW MUSIC - a new level, a boss, a calmer menu:
   provider.FollowUp(...). The new music is generated alongside what is
   playing and TAKES OVER AT A BAR LINE once it has its own pre-roll; the music
   never stops meanwhile. It is not instant - there is no fixed latency.

       music.FollowUp("FourOnTheFloor");          // a preset of the playing
                                                  // generator's family
       music.FollowUp("dark driving");            // character words
       music.FollowUp(new GeneratedMusicOptions   // preset + seed + tempo, or
       {                                          // another generator
           Preset = "ClubArrangement",
           Seed = level,
           BeatsPerMinute = 132,                  // ASKED OF THE GENERATOR here
       });

   FollowUp(options) reads Generator (null = the one playing carries on),
   Preset, Text, Seed and BeatsPerMinute. The instrument library, volume, seam,
   priming and engine settings belong to the session: a follow-up does not
   change them.

   Made while the music is still STARTING, a follow-up is kept and made as soon
   as the music plays (a second one before then replaces the first).

   FollowUp throws - and THE MUSIC CARRIES ON as it was - when:
     ArgumentException          blank words; a preset that does not exist or
                                belongs to another family (the message lists
                                the ones that fit)
     InvalidOperationException  the music is not started or has faulted; a
                                named generator is not registered (the message
                                lists what is)
     MusicGenerationException   the generator refuses the request, e.g. free
                                text (MusicRequestNotHonouredException names
                                what it will not honour)
     ObjectDisposedException    the provider was disposed

   A follow-up that is accepted but fails LATER, inside the generator, is
   reported in provider.GenerationError while the music plays on.

2. A DIFFERENT MODEL, LIBRARY OR ANY SESSION SETTING - a settings screen:
   CALL UseGeneratedMusic AGAIN with the new options. The provider the earlier
   call created is stopped, unregistered and DISPOSED (releasing its model),
   and the new one is registered and - under StartImmediately - started.

       Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions
       {
           Generator = MuPTModel.GeneratorName,
           InstrumentLibrary = "FluidR3Gm",
           Preset = "JigInD",
       });

   The new music starts from its own beginning, with its own fade-in.

STATES - WHAT THE GAME SEES
===========================
provider.State (and StreamingMusicTrack.State while the track plays it):

  Stopped    not running (before Start, after Stop, after Dispose)
  Starting   loading the model and writing the first bars; the engine plays
             silence. Normal - it can take a moment, longer on a slow machine.
  Playing    music is being heard
  Starved    the generator fell behind and the music waits; it returns to
             Playing by itself. NORMAL, never a failure: the game comes first
             and the music waits. On a machine that cannot compose as fast as
             it plays, the music package switches to whole phrases with rests
             between them, which sounds intentional.
  Faulted    the music cannot play; provider.Fault says why. Never thrown into
             the engine. The track plays silence until the game stops it or
             calls UseGeneratedMusic again.

provider.StateChanged is raised after every change, on ANY thread - including
the engine's audio fill thread. Keep handlers quick and marshal to the game with
Engine.Instance.EngineDispatcher.Post before touching game state. The engine
also logs every change at Information.

GeneratedMusicProvider - API REFERENCE
======================================
  const string ProviderName = "Generated music"

  GeneratedMusicProvider(GeneratedMusicOptions options)
      Keeps a COPY of the options. Throws ArgumentNullException, and
      ArgumentException for an unknown preset or a negative MasterVolume.
      Construct one yourself only to register it by hand:
          Engine.Instance.Managers.StreamingMusic.Register(provider);

  The engine's contract (IStreamingMusicProvider) - called BY THE ENGINE:
      string Name, string Description, StreamingMusicState State,
      Exception? Fault, event StateChanged,
      void Start(int sampleRate, int channels)   returns at once; never throws
      void Stop()                                prompt; safe when stopped
      int Render(Span<float> left, Span<float> right)
                                                 audio fill thread; no
                                                 allocation; never throws

  Beyond the contract - called by THE GAME:
      GeneratedMusicOptions Options   a copy of the options it plays with
      int SampleRate                  the rate it last started at (0 before)
      ActiveMusicSource? ActiveSource what is REALLY playing: GeneratorName,
                                      GeneratorFamily, IsReplay,
                                      InstrumentLibraryName, RenditionName,
                                      Voicing. Null until the music has started.
                                      After a follow-up it changes at the
                                      switch, not at the call.
      string ActiveSourceSummary      ActiveSource as one line ("" before)
      MusicDiagnostics? Diagnostics   starvation gaps, RealTimeFactor (null =
                                      not measured yet, never "slow"), Mode,
                                      Lead, seams; cheap every frame
      Exception? GenerationError      what the generator last threw, or null
      void FollowUp(string presetOrText)
      void FollowUp(GeneratedMusicOptions options)
      void Release()                  give a model's memory back; it stays
                                      registered and loads again on the next
                                      start. Throws InvalidOperationException
                                      while the music is starting or playing -
                                      stop it first.
      void Dispose()                  stops, ends the session, releases the
                                      model; safe to repeat. A disposed provider
                                      reports Faulted if started.

EngineGeneratedMusicExtensions
==============================
  GeneratedMusicProvider UseGeneratedMusic(this Engine engine)
  GeneratedMusicProvider UseGeneratedMusic(this Engine engine,
                                           GeneratedMusicOptions options)

  Throws ArgumentNullException, and ArgumentException for an unknown preset or a
  negative MasterVolume - before anything in the engine changes. Everything that
  goes wrong once the music starts is a Faulted state instead.

  OWNERSHIP: the provider UseGeneratedMusic created is disposed by the next
  UseGeneratedMusic call, or when the engine is disposed. The game never has to
  dispose it. (A provider the game constructed itself is the game's to dispose,
  after unregistering it.)

THREADING
=========
  * Render runs on the engine's audio fill thread; start-up (the model load and
    the session's first bars) runs on a worker; every other member may be called
    from any thread. The provider is safe for all of that.
  * FollowUp, Release and Dispose may block briefly while the music session
    changes over; call them from game code, not from inside a StateChanged
    handler on the audio thread.
  * Inference shares the processors with the game loop. Each model takes a
    conservative share of the machine by default (a quarter of the processors,
    at most four threads).

PLATFORM NOTES
==============
  * Every platform the engine runs on - Windows, macOS, Linux (Debian-family),
    Linux Frame Buffer - plays generated music through the engine's own audio
    output; this package adds no audio backend and no device of its own.
  * SkyTNT runs fully managed (ONNX on the managed road): no native library.
  * MuPT runs on the model runner's BUNDLED NATIVE ENGINE, carried in the
    standard runtimes/<rid>/native layout for win-x64, win-arm64, osx-x64,
    osx-arm64, linux-x64, linux-arm64 and linux-riscv64. Keep the native library
    for the platform you ship to when trimming or publishing self-contained.
  * FluidR3Gm is managed code plus one SoundFont file; ModestSynthGm is managed
    code and nothing else.
  * No Python, no download at run time, nothing to install on the target
    machine for the music.

SHIPPING
========
  * The model packages and the FluidR3Gm package copy their files (model
    weights, the SoundFont) into the build AND publish output of every project
    that reaches them, beside the executable. Deploy those folders with the
    game; they are not embedded in an assembly.
  * THE SIZE OF WHAT SHIPS IS THE GAME'S DECISION. The model and SoundFont
    packages are large; ModestSynthGm and the embedded replay cost almost
    nothing. A game can ship one model, both, or none (the embedded replay), and
    either instrument library - see each package's own AGENT-README.txt for its
    files and the build switches that keep them elsewhere.
  * A loaded model costs working memory well beyond its file size while it
    generates; measure on the hardware you ship to. provider.Release() (music
    stopped) or disposing the provider gives it back.

COMMON PITFALLS TO AVOID
========================
  * EXPECTING SOUND WITH NO INSTRUMENT LIBRARY REGISTERED. This package, the
    music package and the model packages register none. Call
    GeneralMidiInstrumentLibrary.Register() at start-up.
  * LETTING REGISTRATION ORDER CHOOSE THE INSTRUMENTS. The first library
    registered is the default. Name InstrumentLibrary when more than one is
    registered.
  * EXPECTING A MODEL WITH NONE REGISTERED. No model registered = the embedded
    replay, the same piece every time. Check provider.ActiveSource.IsReplay (or
    the one log line) before believing a model is playing.
  * ASKING FOR DRUMS FROM MuPT. ABC notation has no percussion; pick a SkyTNT
    preset for drums.
  * WRITING PROSE. Neither model reads sentences; use a preset or character
    words.
  * TREATING Starting OR Starved AS A FAILURE. They are ordinary states. Only
    Faulted is a failure, and it never stops the game.
  * EXPECTING A FOLLOW-UP TO BE HEARD AT ONCE. It takes over at a bar line once
    its own music is ready.
  * CHANGING THE MODEL OR LIBRARY WITH FollowUp. A follow-up keeps the session's
    instrument library and settings; call UseGeneratedMusic again instead.
  * RELEASING WHILE PLAYING. Release() is refused while the music plays; stop
    it, or dispose the provider, first.
  * CROSSFADING TWO TRACKS OVER THE SAME PROVIDER. One provider is one stream;
    a second track over it restarts it. Crossfading generated music with a file
    or MIDI track works as usual.
  * NOT PINNING THE AUDIO FORMAT. Call AudioSystem.Initialize(rate, channels)
    at start-up; the music renders at whatever the engine's output runs at.
  * TOUCHING GAME STATE FROM StateChanged. It can arrive on the audio thread;
    post to the engine dispatcher.

WHAT THIS PACKAGE DOES NOT DO
=============================
  * It registers no instrument library and no generator - ever.
  * It carries no model and no instruments; those are their own packages.
  * It does not open an audio device; the engine owns the output.
  * It does not render music to a file. Use MusicSession.RenderToFileAsync from
    CodeBrix.Audio.MusicGeneration for title music decided in advance, and play
    the file with the engine's FileMusicTrack.
  * It does not promise gapless music on a machine that generates slower than it
    plays: the music waits, in whole bars, and Diagnostics says so.

WORKING EXAMPLES ON GITHUB
==========================
Repository root: https://github.com/ellisnet/CodeBrix.Platform.GameEngine

  https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.GeneratedMusic.Tests
      EngineGeneratedMusicExtensionsTests.cs — the one call, all the way
          through the engine: registered, played by MusicManager as a
          StreamingMusicTrack and heard through the music voice; starting later
          with PlayStreaming; calling it again to replace the music; running on
          in silence with no instrument library.
      GeneratedMusicProviderTests.cs — the provider driven the way the audio
          thread drives it: its states, audible music from the embedded replay,
          stop and restart, the degraded paths, follow-ups by preset, by words
          and by options, and the model memory rules.
      GeneratedMusicProviderRenderStateTests.cs — how the state follows what
          is actually rendered: Playing from the first audible block (even
          while the session still reports itself starved), Starting before
          any music is heard, Starved only for silence after music, and
          Faulted / Stopped when the session ends.
      GeneratedMusicRequestsTests.cs — how the options map onto the music
          package's session and request: generator choice, presets, words,
          tempo, seed, seam and priming, and that the session never opens an
          audio device.
      GeneratedMusicOptionsTests.cs — the documented defaults.
      ModelPlaybackOptInTests.cs — OPT-IN: SkyTNT and MuPT through ModestSynthGm
          and FluidR3Gm, skipped unless GENERATEDMUSIC_MODEL_TESTS=1.

QUICK REFERENCE CARD
====================
INSTALL
    dotnet add package CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever
    + a model:       CodeBrix.Audio.MusicGeneration.SkyTNT.ApacheLicenseForever
                     and/or CodeBrix.Audio.MusicGeneration.MuPT.ApacheLicenseForever
    + (optional)     CodeBrix.Audio.Samples.FluidR3Gm.MitLicenseForever

REGISTER (once, at start-up, before the music starts)
    AudioSystem.Initialize(48000, 2);
    GeneralMidiInstrumentLibrary.Register();     // ALWAYS one library
    SkyTNTModel.Register();  MuPTModel.Register();  FluidR3GmInstrumentLibrary.Register();

PLAY
    GeneratedMusicProvider music = Engine.Instance.UseGeneratedMusic(
        new GeneratedMusicOptions { Generator = "SkyTNT", InstrumentLibrary = "ModestSynthGm",
                                    Preset = "ClubArrangement" });

CHANGE (same generator, at a bar line)     music.FollowUp("FourOnTheFloor");
SWITCH (model / library / settings)        Engine.Instance.UseGeneratedMusic(newOptions);
LATER START                                StartImmediately = false, then
                                           MusicManager.Instance.PlayStreaming(fadeIn);
WHAT IS PLAYING                            music.ActiveSourceSummary / ActiveSource.IsReplay
HOW IT IS DOING                            music.State, music.Diagnostics, music.Fault

TOP FIVE MISTAKES
    1. No instrument library registered (silence, Faulted).
    2. Two libraries registered and InstrumentLibrary left null.
    3. No model registered and believing a model plays (it is the replay).
    4. A SkyTNT preset with MuPT (or the reverse), or prose in Text.
    5. Treating Starting / Starved as errors.

================================================================================
END OF AGENT-README
================================================================================
