# Release notes — 2026-09-24

`CodeBrix.Platform.GameEngine.MitLicenseForever` (engine core + CodeBrix.Platform host layer), and
the first release of `CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever`.

This release adds endless streaming music to the music system: a small provider contract,
`IStreamingMusicProvider`, for music that is produced as it plays — generated music, a procedural
score — and a `StreamingMusicTrack` that plays any such provider on the music bus, so fades,
crossfades, ducking, stingers, pause and the music slider all work on it unchanged. A library
that supplies a provider registers it with the engine, and a game plays it with one call. It also
fixes a streaming voice that ignored the mixer until a volume changed.

The first provider arrives with it as a new, optional package: `CodeBrix.Platform.GameEngine.GeneratedMusic`
plays `CodeBrix.Audio.MusicGeneration`'s endless, model-generated music through that contract, started
with one `Engine.UseGeneratedMusic(options)` call.

The solution builds with 0 warnings and 0 errors in Debug and Release; the test suites stand at
870 engine-core tests, 19 host tests (one skipped), 45 gamepad tests, 384 Kenney-asset tests
(one opt-in corpus scan, skipped unless an environment variable points at a Kenney collection) and
77 generated-music tests (four opt-in model tests, skipped unless `GENERATEDMUSIC_MODEL_TESTS=1`).

---

## BREAKING CHANGES

None. Everything below is additive, apart from the fix to `StreamingAudioSource`'s starting
level, which makes a stream obey the mixer from its first sample as every other voice already did.

---

## NEW

### Streaming music providers

* **`IStreamingMusicProvider`** (namespace `CodeBrix.Platform.GameEngine.Audio`): `Name`,
  `Description`, `State`, `Fault`, `StateChanged`, `Start(sampleRate, channels)`, `Stop()` and
  `int Render(Span<float> left, Span<float> right)`. The contract, documented on the interface:
  `Start` returns promptly and slow preparation continues in the background; `Render` runs on the
  audio fill thread, must not allocate or block, and may return fewer frames than asked at any
  time; a provider never throws into the engine — it reports `Faulted` and its `Fault` instead.
  The engine guarantees `Render` is never called concurrently, never before `Start` returns and
  never after `Stop` returns, and it never disposes a provider.
* **`StreamingMusicState`**: `Stopped`, `Starting`, `Playing`, `Starved`, `Faulted`. `Starting` and
  `Starved` are ordinary states: the engine plays silence through them and keeps pulling.
* **`StreamingMusicTrack : MusicTrack`** wraps a provider in a `StreamingAudioSource` on the music
  bus. It interleaves the provider's two planes into the output format (down-mixing for a mono
  output), pads a short render with silence, renders large requests in pre-allocated chunks so the
  fill thread never allocates, and never throws from the fill thread — an exception escaping
  `Render` is caught and treated as a fault. `Position` is the time pulled since the last start,
  silence included; `Duration` is `TimeSpan.Zero` (the "not known" convention, read as endless);
  `IsLooping` is always true and `Seek` does nothing. `Ended` is raised, once per start, when the
  provider ends the stream by itself (it reports `Stopped` or `Faulted`, or is stopped by someone
  else); stopping the track through `MusicManager` does not raise it, as for every track. Each
  state change is logged once at Information, with `Starved`/`Playing` flapping limited to one
  line every few seconds.
* **The sample-rate rule.** The provider is started at the output's real format: the pinned
  `AudioSystem` format when the game called `AudioSystem.Initialize`, otherwise the rate the
  shared output is running at or was configured for, and only when nothing has claimed the output
  yet `StreamingMusicTrack.UnclaimedOutputSampleRate` (48 kHz), which the output then adopts.
* **One stream per provider.** Only one track plays a provider at a time: starting a second track
  over the same provider takes the stream over (the provider is stopped and started afresh) and
  the first goes silent.
* **`StreamingMusicRegistry`**, reached as `Engine.Instance.Managers.StreamingMusic`: `Provider`,
  `HasProvider`, `Register(provider)`, `Unregister()` and `CreateTrack()`. It holds one active
  provider; registering a different one stops the old one and replaces it, and nothing in the
  registry ever disposes a provider. `CreateTrack()` with nothing registered throws an
  `InvalidOperationException` that says how to register one. `Engine.Dispose()` unregisters (and so
  stops) the active provider.
* **`MusicManager.PlayStreaming(fadeIn)`** plays the registered provider in one call and returns
  the track. It is idempotent while that provider is streaming, so calling it on every screen
  change keeps the music going rather than restarting it.

### Generated music — new package `CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever`

* **`Engine.UseGeneratedMusic()` / `UseGeneratedMusic(GeneratedMusicOptions)`** (namespace
  `CodeBrix.Platform.GameEngine.GeneratedMusic`): creates a `GeneratedMusicProvider`, registers it
  with `Engine.Managers.StreamingMusic` and — unless `StartImmediately` is off — plays it through
  `MusicManager` with a fade-in. Calling it again stops, unregisters and disposes the provider the
  earlier call created and starts the new one, which is how a game switches model, instrument library
  or any session setting. The provider it created is disposed with the engine.
* **`GeneratedMusicOptions`**: `Generator` (null = the first registered generator that is not a
  built-in replay), `InstrumentLibrary` (null = the registry default), `Preset` (a preset of the
  generator's family, with its suggested voicing), `Text` (known character words become character
  words, anything else free text), `BeatsPerMinute` (a session tempo, carried across fresh pieces and
  never sent to the generator), `Seed`, `SeamCrossfade` (four seconds by default), `SegmentPriming`
  (null = Alternate for SkyTNT, Fresh for MuPT), `MasterVolume`, `StartImmediately`, `TrackKey` and
  `FadeIn`, plus `Clone()`.
* **`GeneratedMusicProvider : IStreamingMusicProvider`** owns a `MusicSession` that opens no audio
  device (`ApplicationOwnsAudioOutput`) and renders at the engine output's rate. `Start` returns at
  once and loads the model and first bars on a worker (`Starting`); the state becomes `Playing` when
  music is first heard, `Starved` while the generator has fallen behind, and `Faulted` — never an
  exception — when the music cannot play. Beyond the engine contract: `FollowUp(string presetOrText)`
  and `FollowUp(GeneratedMusicOptions)` (the new music takes over at a bar line; a change asked for
  while starting is made once the music plays), `ActiveSource` / `ActiveSourceSummary`,
  `Diagnostics`, `GenerationError`, `Options`, `SampleRate` and `Release()`.
* **Degraded paths, by design**: with no model registered the music package's embedded replay plays
  and one Information log line names the fix; with no instrument library registered the provider
  faults with a message naming `GeneralMidiInstrumentLibrary.Register()` and the game runs on in
  silence; a preset of another family, an unregistered generator or library, or words a model will
  not read fault the music, never the game. A preset name that no family has is refused at once with
  an `ArgumentException` listing the presets.
* The package registers no instrument library and no generator: those are the game's choices, one
  `Register()` line each.

---

## FIXES

### Audio

* **`StreamingAudioSource` ignored the mixer until a volume changed.** Its gain stage started at
  full level, so a stream created while the music slider was down, or while a duck was active,
  played at full volume until something moved a volume. It now applies the mixer's gain from
  construction, as `SoundChannel` and `AudioResource` always did.

### Input

* **`Engine.Initialize` discarded a gamepad manager assigned before it.** The hosts wire the gamepad
  manager (`InitializeSdlGamepadManager`) while configuring input and only then call `Initialize`,
  which overwrote `Input.GamepadManager` with its own null argument. The engine then never refreshed
  the manager the game was reading: every controller looked connected and stayed frozen. A null
  argument now means "keep what is assigned", as it already did for the keyboard, mouse and touch
  adapters.

### KenneyAssets

* **A decorative rule at the top of a licence file was taken as the pack title.** Some packs open
  `License.txt` with a row of `#` characters before the title line. `KenneyNames.TryParseLicenseTitle`
  took that row as the title, so the pack's slug came out as `pack` and its display name and credit
  line were just `#` characters. Lines with no letter or digit are now skipped before the title line
  is chosen; a title line that is then too long still falls back to the file name, as before.
* **The local-mode pack guard left a package behind.** `UseLocalEngineProject=true` correctly refuses
  to pack, but the guard ran after the package had already been written, so an engine-less `.nupkg`
  was left in the output folder. The guard now runs before the nuspec is generated (KenneyAssets and
  Sdl2; the GeneratedMusic package was written this way from the start).

---

## DOCUMENTATION

* `src/CodeBrix.Platform.GameEngine.GeneratedMusic/AGENT-README.txt`: the consumer guide of the new
  package — the package references and Register lines, the one call, the options table, which music
  plays (and the first-registered-library pitfall), presets, words, follow-ups, states, the API,
  threading, platform and shipping notes.
* `README.md`, `README-INDEX.txt`, `THIRD-PARTY-NOTICES.txt` and the root `AGENT-README.txt` (OTHER
  PACKAGES, and a pointer from STREAMING MUSIC PROVIDERS) list the new package.
* `AGENT-README.txt`: a new STREAMING MUSIC PROVIDERS subsection in the music section (the
  contract, the one-call usage, the gap-tolerance and sample-rate rules, the registry and its
  pitfalls), the fill thread's new caller in the threading model, and the quick reference card.

---

## REPOSITORY

* New projects `src/CodeBrix.Platform.GameEngine.GeneratedMusic` and
  `tests/CodeBrix.Platform.GameEngine.GeneratedMusic.Tests`, both in `CodeBrix.Platform.GameEngine.slnx`.
  The library's `UseLocalEngineProject` switch DEFAULTS TO TRUE, because no published engine package
  carries the streaming-music seam yet; the project comment names the two edits that hand it over to
  the published engine once one does. Local mode cannot be packed.
* The engine core grants `InternalsVisibleTo` to the new test project, so its tests play generated
  music through the engine's music manager with a fake music voice — no audio device is opened.
* The new tests pull in the SkyTNT, MuPT and FluidR3Gm packages; only four opt-in tests exercise them,
  skipped unless `GENERATEDMUSIC_MODEL_TESTS=1`. The fast tests use the embedded replay and the
  synthesized General MIDI library.
