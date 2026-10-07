================================================================================
MAINTAINER-README: CodeBrix.Platform.GameEngine
Notes for people and agents MAINTAINING this repository — not for package consumers
================================================================================

If you are CONSUMING one of the NuGet packages, this is the wrong file. Read
AGENT-README.txt (repository root) for the game engine,
src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt for gamepads,
src/CodeBrix.Platform.GameEngine.KenneyAssets/AGENT-README.txt for Kenney asset
bundles, or src/CodeBrix.Platform.GameEngine.CardsAndDice/AGENT-README.txt for
cards and dice. See README-INDEX.txt for the map.

PURPOSE AND SCOPE
=================
This repository produces these published NuGet packages.

  CodeBrix.Platform.GameEngine.MitLicenseForever          License: MIT
      A fully managed, cross-platform 2D / 2.5D game engine for .NET, built on
      SkiaSharp, plus its CodeBrix.Platform host layer. Packed by
      src/CodeBrix.Platform.GameEngine.Host, and it CARRIES BOTH ASSEMBLIES —
      the engine core dll is injected into the host's package (see PACKAGING).
      Consumer documentation: AGENT-README.txt (repository root).

  CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever    License: MIT AND Zlib
      SDL2-based game controller (gamepad) support for the engine. Packed by
      src/CodeBrix.Platform.GameEngine.Sdl2. Consumer documentation:
      src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt.

  CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever   License: MIT
      Kenney asset bundle support: it catalogs what a bundle holds and
      materializes images, sprite atlases, audio, fonts, SVG, Tiled maps and
      glTF models into engine objects, through the engine's asset-provider
      contract. Packed by src/CodeBrix.Platform.GameEngine.KenneyAssets.
      Consumer documentation:
      src/CodeBrix.Platform.GameEngine.KenneyAssets/AGENT-README.txt.

  CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever License: MIT
      Generated in-game music: CodeBrix.Audio.MusicGeneration's endless,
      model-generated music played on the engine's music bus. Packed by
      src/CodeBrix.Platform.GameEngine.GeneratedMusic. Consumer documentation:
      src/CodeBrix.Platform.GameEngine.GeneratedMusic/AGENT-README.txt.

  CodeBrix.Platform.GameEngine.CardsAndDice.MitLicenseForever   License: MIT
      Cards, decks, piles, dice and an animated tabletop, with embedded card,
      dice and symbol artwork and sound effects. Packed by
      src/CodeBrix.Platform.GameEngine.CardsAndDice, and it CARRIES BOTH
      ASSEMBLIES — the non-packable CardsAndDice.Assets dll (the embedded
      artwork) is injected into the package (see PACKAGING). Consumer
      documentation: src/CodeBrix.Platform.GameEngine.CardsAndDice/AGENT-README.txt.

The packages are versioned and PUBLISHED INDEPENDENTLY; they do not share a
version number. The add-ons use the public engine API.

REPOSITORY LAYOUT
=================
    src/CodeBrix.Platform.GameEngine/          engine CORE (IsPackable=false)
    src/CodeBrix.Platform.GameEngine.Host/     host layer; PACKS both assemblies
    src/CodeBrix.Platform.GameEngine.Sdl2/     gamepad add-on; packs itself
    src/CodeBrix.Platform.GameEngine.KenneyAssets/
                                               Kenney asset add-on; packs itself
    src/CodeBrix.Platform.GameEngine.CardsAndDice/
                                               cards and dice add-on; PACKS both
                                               itself and the .Assets assembly
    src/CodeBrix.Platform.GameEngine.CardsAndDice.Assets/
                                               embedded artwork (IsPackable=false)
    tests/CodeBrix.Platform.GameEngine.Tests/
    tests/CodeBrix.Platform.GameEngine.Host.Tests/
    tests/CodeBrix.Platform.GameEngine.Sdl2.Tests/
    tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests/
                                               + fixtures/ (CC0 Kenney bundles)
    tests/CodeBrix.Platform.GameEngine.CardsAndDice.Tests/
    samples/                                   complete games/demos
    tools/padcheck/                            hand-run gamepad hardware check
    tools/sdl2_library_building/               SDL2 Windows-ARM64 build script
    tools/cards-and-dice/                      CardsAndDice asset import/notices
    native_libraries/<rid>/                    committed SDL2 binaries + provenance
    global.json                                selects the test runner; pins no SDK
    CodeBrix.Platform.GameEngine.slnx          product projects + tests only

Engine-core source is grouped into sub-folders that mirror the sub-namespaces
(Assets, Audio, Configuration, Drawing, Extensibility, Input, Logging, Physics,
Rendering, Scenes, Serialization, SkiaSharp, Timers); entry types (Engine.cs,
EngineState.cs, EngineDispatcher.cs, ...) sit at the project root. The asset
PROVIDER contract lives in Assets/Providers/ and the format-neutral model data
types in Assets/Models/. The Sdl2 project follows the same rule with Gamepad/ and
Native/ sub-folders and EngineGamepadExtensions.cs at the root; the KenneyAssets
project with Sources/ (archive readers and the pack catalog), Parsing/ (the
classifier and the atlas and Tiled document parsers), Materialize/ (tilesheet,
audio, font and Tiled-import work) and Models/ (the glTF reader and the software
model sprite renderer), and EngineKenneyAssetsExtensions.cs,
KenneyGameAssetProvider.cs, KenneyAssetsOptions.cs and KenneyPackSummary.cs at
the root. The CardsAndDice project groups its source into Cards/ (definitions,
cards, piles, decks, the built-in decks and the card composer), Dice/, Layout/
(layout kinds and the pure layout calculations) and Table/ (CardsAndDiceTable,
split into partial files by concern, and the table-side types), with
EngineCardsAndDiceExtensions.cs at the root; like the other projects, each
sub-folder is its own sub-namespace (CodeBrix.Platform.GameEngine.CardsAndDice
.Cards, .Dice, .Layout, .Table) and the extension class sits in the root
namespace. Its Assets project holds
AssetCatalog.cs and AssetEntry.cs at the root, the embedded files under
Resources/, catalog.json, and the provenance records (Provenance/ and
tarot-provenance.json), which are not embedded.

Neither samples/ nor tools/ is in the .slnx, deliberately: the solution holds
only product projects and their tests. Each sample carries its own .slnx.
The C# tool under tools/ (padcheck) follows the family's tools/<name>/<name>.csproj
shape: an Exe with IsPackable=false that ships in no package and is built and
run by hand.

The solution's Solution Items folder carries .gitignore, AGENT-README.txt,
EXTRAS-README.txt, global.json, icon-codebrix-128.png, LICENSE,
MAINTAINER-README.txt, README-INDEX.txt, README.md, the current
RELEASE-NOTES-<yyyy-MM-dd>.md and THIRD-PARTY-NOTICES.txt; the Tests folder
carries every test project. The add-on packages' own AGENT-README.txt files
are not listed there because each is a <None> item of its own project and is
already visible inside it.

INTERNALS SEAMS
---------------
    CodeBrix.Platform.GameEngine  -> .Host and .Tests
    CodeBrix.Platform.GameEngine.Host -> .Host.Tests
    CodeBrix.Platform.GameEngine.Sdl2 -> .Sdl2.Tests
    CodeBrix.Platform.GameEngine.KenneyAssets -> .KenneyAssets.Tests
    CodeBrix.Platform.GameEngine.CardsAndDice -> .CardsAndDice.Tests
    CodeBrix.Platform.GameEngine.CardsAndDice.Assets -> .CardsAndDice.Tests
Every packable project ships an InternalsVisibleTo.cs to its own .Tests
assembly (the CardsAndDice.Assets project, which ships inside the CardsAndDice
package, does too). Neither add-on project has a seam into the engine core. The
KenneyAssets project keeps most of its own types internal — only six are public
— so its test suite reaches the archive readers, the parsers, the materializers
and the model code through that seam.

BUILDING
========
    dotnet build CodeBrix.Platform.GameEngine.slnx

Every project is net10.0 only; never multi-target. Every product project sets
<Nullable>enable</Nullable> and turns on GenerateDocumentationFile. The Sdl2
project additionally sets <AllowUnsafeBlocks>true</AllowUnsafeBlocks> —
the SDL2 bindings are function-pointer based and pass byte* strings; the unsafe
context is confined to the Native folder. The KenneyAssets project carries NO
NoWarn list of any kind, so an undocumented public member or a broken XML cref
there fails the build rather than passing quietly as it would in the engine core.
The same holds for both CardsAndDice projects.

The build NEVER reaches the network for native binaries. Everything under
native_libraries/ is committed and read straight off disk at pack time.

THE ADD-ON PROJECTS BUILD AGAINST THE PUBLISHED ENGINE
------------------------------------------------------
src/CodeBrix.Platform.GameEngine.Sdl2,
src/CodeBrix.Platform.GameEngine.KenneyAssets and
src/CodeBrix.Platform.GameEngine.CardsAndDice consume the engine as a PUBLISHED
PackageReference, not a ProjectReference. They have to: the engine project is
IsPackable=false and its dll is embedded into the Host project's package, so a
ProjectReference would compile locally but produce a package with an unsatisfied
runtime dependency (NuGet emits no dependency for a non-packable
ProjectReference, and the engine dll would not be inside the add-on package
either).

Consequences to keep in mind:

  * If a change in an add-on needs the engine core to expose something new:
    publish the engine package FIRST, wait for it to index, bump the pinned
    version in the add-on csproj, then build and publish the add-on.
  * NEVER configure a local folder as a NuGet source holding a freshly-built
    engine package. Every engine build restamps its version, so a local source
    could shadow nuget.org and an add-on would be built against an engine
    version that was never published.

LOCAL VERIFICATION ESCAPE HATCH
-------------------------------
    dotnet build src/CodeBrix.Platform.GameEngine.Sdl2/CodeBrix.Platform.GameEngine.Sdl2.csproj \
        -p:UseLocalEngineProject=true

That swaps the engine PackageReference for a ProjectReference so a local run
tests THIS repository's engine source, and it forces GeneratePackageOnBuild off.
A _BlockPackWithLocalEngineProject target makes Pack fail outright while the
flag is set, because the resulting package would carry no engine dependency and
no engine dll and must never be published. Every add-on project carries the
switch and the guard.

The same flag applies to padcheck, which sits downstream of Sdl2:

    dotnet build tools/padcheck/padcheck.csproj -p:UseLocalEngineProject=true

Without it, a local engine fix appears to have no effect, which is
indistinguishable from the fix not working. This is exactly how gamepad support
once shipped "fully hardware-verified" while being completely dead on the
InputPump path — nothing that ran against real hardware had ever been built from
local engine source.

EVERY ADD-ON DEFAULTS THAT FLAG TO FALSE
---------------------------------------
Every add-on project (Sdl2, KenneyAssets, GeneratedMusic, CardsAndDice)
defaults UseLocalEngineProject to false and pins a PUBLISHED engine version in
its csproj, so an ordinary build, test run or pack uses the engine from
nuget.org. Local mode is opt-in, per command, for verifying an engine change
that is not published yet; it can never be packed (the guard above) and it
forces GeneratePackageOnBuild off.

KenneyAssets and GeneratedMusic once defaulted the flag to true while the
engine seams they implement (the asset-provider contract and the
streaming-music seam) were co-developed with them; both have been handed over
to the published engine since, and the switch is now the same escape hatch as
on Sdl2.

THE IN-REPO SAMPLES NEED THE FLAG when verifying an unpublished engine change,
exactly like padcheck: KenneyAssetsDemo references the engine, the host and the
KenneyAssets project from source, so build it with the flag so that the add-on
compiles against the same engine source as the sample:

    dotnet build samples/KenneyAssetsDemo/src/KenneyAssetsDemo.LinuxX11 \
        -p:UseLocalEngineProject=true

TESTING
=======
    dotnet test CodeBrix.Platform.GameEngine.slnx

xUnit v3 + SilverAssertions. No opt-in environment variable is required for the
suites to pass; head-dependent host behavior is env-gated or skipped with a
reason, and the ONE opt-in test in the repository (the Kenney corpus scan, below)
skips itself with a reason when its variable is unset.

Test dependencies, every suite: xunit.v3, xunit.runner.visualstudio,
Microsoft.NET.Test.Sdk and SilverAssertions.ApacheLicenseForever; the
engine-core suite adds SkiaSharp.NativeAssets.Linux and CodeBrix.Audio.Opus, and
the Kenney suite adds SkiaSharp.NativeAssets.Linux for the same reason (it
decodes pictures and rasterizes models headlessly), and so does the CardsAndDice
suite (it rasterizes every embedded SVG). No coverage collector is
referenced by any test project.

THE TEST RUNNER IS Microsoft.Testing.Platform (MTP), selected by global.json at
the repository root. That file does NOT pin an SDK version, so the newest
installed .NET 10 SDK is still used; it exists solely to select the runner:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

Because the setting lives in global.json rather than in the csprojs, it applies
to every `dotnet test` run anywhere in the repository, including CI. Keep the
file committed. Each test assembly is therefore also a self-contained
executable. Running the built dlls
directly is the reliable fallback when `dotnet test` discovers nothing — on
.NET SDK 10.0.400 it reports zero tests for this repository — and it is the way
to filter down to one class or method while iterating:

    dotnet build CodeBrix.Platform.GameEngine.slnx -c Debug --nologo -v q
    dotnet tests/CodeBrix.Platform.GameEngine.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.Tests.dll
    dotnet tests/CodeBrix.Platform.GameEngine.Sdl2.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.Sdl2.Tests.dll
    dotnet tests/CodeBrix.Platform.GameEngine.Host.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.Host.Tests.dll
    dotnet tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.KenneyAssets.Tests.dll
    dotnet tests/CodeBrix.Platform.GameEngine.CardsAndDice.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.CardsAndDice.Tests.dll

    # filters
    ... .Tests.dll -class 'CodeBrix.Platform.GameEngine.Tests.TimerTests'
    ... .Tests.dll -method 'CodeBrix.Platform.GameEngine.Tests.TimerTests.Add_with_a_sub_tick_length_throws'

The engine-core suite is by far the largest; the Kenney suite is next, and the
gamepad and host suites are small. One host test skips without a UI head and one
Kenney test skips unless its environment variable is set. Report the counts a run
actually printed rather than a number from this file — every wave of work moves
them.

ENGINE CORE TESTS
-----------------
Headless unit tests (the UI-agnostic core makes this clean): populated-graph
save/load round-trips (EngineStateRoundTripTests: scenes/layers/tile grids,
shared sprite references, cycles, loose-file and asset-pack audio, compression,
merge semantics), the global pause suite (EnginePauseTests: park/resume
semantics, no-burst time shifting, audio suspend rules, snapshot capture), the
audio SFX suites (CachedSoundTests, SfxVoicePoolTests — decode-once preload and
the pool's cull-policy selection logic; nothing in them opens the audio device),
and the music suites (AudioMixerTests, MusicManagerTests, MusicStemSetTests,
MusicTimelineTests, MusicQuantizedTransitionTests, MidiMusicTrackLayerTests —
fades advanced by hand through MusicFadeTicker.ManualTickingForTests rather than
slept through, so the assertions are exact instead of racy; the stem mixer's
actual output samples are read and summed; and the MIDI fixtures are BUILT IN
CODE — a MidiEventCollection exported through MidiFile.Export, and a one-region
SFZ over a generated tone — so there is no committed binary).

THE CORE TEST ASSEMBLY RUNS ITS COLLECTIONS SERIALLY
([assembly: Parallelization(Mode = ParallelMode.None)] in AssemblyInfo.cs, from
Xunit.v3 + Xunit.Sdk; the gamepad suite carries the same attribute): the
engine under test is a process-global singleton machine (Engine.Instance plus
the scene/sprite/cycle/tilesheet/audio registries), so tests that populate or
clear that state cannot overlap. Keep new test classes compatible with that
assumption — clean up global state you create.

THE SHARED AUDIO OUTPUT IS PART OF THAT GLOBAL STATE, and it is the easiest one
to leak. The shared output ADOPTS A SAMPLE RATE from the first thing that plays,
and it keeps it. So a test that loads anything with its own rate — a
MidiMusicTrack builds a synthesizer and an output voice merely by LOADING,
without ever being played — leaves every later test whose source has a different
rate failing at WaveOutEvent.Init with "this source is N Hz but the shared audio
output runs at M Hz". The symptom is nasty: a VARYING number of unrelated
failures depending on the order the suite happened to run in, and nothing at all
when the offending class is run on its own. Any test class that causes a rate to
be adopted must call AudioSystem.Shutdown() in its Dispose to hand the output
back unclaimed (MidiMusicTrackLayerTests is the worked example). Tests that only
build CachedSounds or drive a StemMixSampleProvider directly never touch the
device and need none of this.

HOST TESTS
----------
Cover what can run without a live UI head (CodeBrixPlatformUiDispatcherTests).

Sdl2 TESTS
----------
THE Sdl2 TEST ASSEMBLY ALSO RUNS SERIALLY, for the same class of reason: SDL2
keeps its initialization state and device list in process-global native state,
and those tests start and shut the subsystem down. Overlapping them would have
one test calling SDL_Quit while another is mid-poll.

Its tests are written as INVARIANTS THAT HOLD WITH OR WITHOUT SDL2 INSTALLED and
with or without a controller attached — asserting that SDL2 loads would turn a
machine without it into a test failure, which is the very outcome the loader
exists to prevent. The conversion logic that real hardware cannot be made to
exercise on demand (axis inversion, the -32768 edge) is tested directly with
fabricated raw values, and the /dev/input ACL scan is tested with fabricated
/proc/bus/input/devices content.

What the unit tests CANNOT cover is what is most likely to be wrong in practice:
whether each physical button reports under the correct name, whether pushing a
stick up actually reports Up, whether the left trigger is the one reporting as
left, and whether a controller that sleeps and wakes is picked back up. Those
need a person with a controller — see tools/padcheck (EXTRAS-README.txt), and
RUN BOTH OF ITS DRIVE MODES: the default mode supplies its own refresh and
therefore cannot detect a missing refresh on the InputPump path.

KenneyAssets TESTS
------------------
THIS ASSEMBLY ALSO RUNS SERIALLY, for the engine's reason: materializing an asset
registers it in the process-global tilesheet, audio and font registries and in
the provider registry, so tests that populate those cannot overlap. Test classes
clear what they registered, unload the audio keys they created and call
AudioSystem.Shutdown() on dispose, exactly as the core suites do.

IT TESTS AGAINST REAL BUNDLES. The suite's fixtures/ folder holds five Kenney
bundles, copied to the test output directory and read exactly as they are:

    kenney_puzzle-pack-1.zip        sprite atlases, identically named images in
                                    sibling folders, an SVG, and the bundle the
                                    extracted-FOLDER tests unzip
    kenney_sci-fi-sounds.zip        .ogg audio, plus a stray desktop.ini that
                                    must be classified rather than break anything
    kenney_blocky-characters_20.zip the same models in .fbx, .glb and .obj form,
                                    animated, with per-model textures
    kenney_brick-kit.zip            a larger model pack whose three sibling
                                    colormap.png files are why dependency
                                    resolution must never guess by file name
    simulated_bundle.zip            a small mixed bundle: audio, three fonts
                                    (one of them an ICON font), images, SVG,
                                    three glTF models and a Tiled map with two
                                    tile sizes, a tileoffset and flip bits

Four of them came from the KenneyAssetBrowser sample's bundle folder and the
mixed one was assembled from Kenney content; ALL FIVE ARE CC0 and are credited in
fixtures/FIXTURES-LICENSE.txt, which also says what each one exercises. They are
test data only and ship in no package. Synthetic bundles — a stale atlas, a
missing tile set image, a damaged zip, a base64 map — are BUILT IN CODE by the
suite's TestFixtures helper into the test output folder, so no further binary
needs committing; prefer that route for a new edge case.

THE OPT-IN CORPUS SCAN. KenneyAllInOneCorpusScan registers a WHOLE Kenney
collection as one source (the "all in one" layout, one pack per child folder) and
asserts that registering hundreds of packs at once raises nothing, that every
sprite atlas parses, that every tile map either parses or is refused with a
message saying why, that every audio asset carries an extension the engine has a
reader for, that no two packs share a slug and no two assets share a key, and
that the asset count agrees with what Describe lists. It is skipped WITH A REASON
unless an environment variable points at such a collection:

    KENNEY_ALLIN1_DIR="/path/to/Kenney Game Assets All-in-1" \
        dotnet tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests/bin/Debug/net10.0/CodeBrix.Platform.GameEngine.KenneyAssets.Tests.dll \
        -class 'CodeBrix.Platform.GameEngine.KenneyAssets.Tests.KenneyAllInOneCorpusScan'

It reads no asset file — only the listing and the small documents — so it takes a
couple of seconds over a corpus of hundreds of packs and tens of thousands of
assets, and it is the check worth running after any change to the catalog, the
classifier or the key scheme. It is opt-in because the collection is a purchased
download that cannot live in the repository; the committed fixtures cover every
rule it exercises, at a smaller scale. RUN IT BEFORE PUBLISHING the KenneyAssets
package: both defects it has found so far (a tile set hiding the sprites it
lists, and an unclassified satellite buffer) were invisible at fixture scale.

CardsAndDice TESTS
------------------
Headless: the table is driven through Update(seconds), Draw(DrawList) and
Pointer(...) with no engine attached, so animation, dealing, selection and drag
are asserted from the published draw commands. AssetCatalogTests re-hashes
EVERY embedded resource against catalog.json, checks that the catalog and the
manifest resources list exactly the same keys, that every licence identifier is
one of MIT, CC0-1.0 or Public-Domain, and rasterizes every embedded SVG, so an
asset added without its catalog entry (or a catalog entry left behind after its
file was removed) fails the suite. Run update-notices.py (below) after any asset
change so THIRD-PARTY-NOTICES.txt carries the same hashes.

The suite does not touch the audio device (sound effects are opt-in and off in
every test) and runs in parallel.

THE CardsAndDiceDemo WALKTHROUGH
--------------------------------
samples/CardsAndDiceDemo carries an unattended pass over every table mode,
switched on by the environment variable CARDSDICE_WALKTHROUGH=1; it needs a
graphical desktop, prints "CARDSDICE WALKTHROUGH PASS" at the end and stops the
engine. See the sample's README.md.

    dotnet build samples/CardsAndDiceDemo/src/CardsAndDiceDemo.LinuxX11

THE MusicDemo WALKTHROUGH
------------------------
The music system's most important behaviours are audible-only: which instrument
format actually loaded, what a file said about its own tempo, and whether a
bar-quantised transition across a TEMPO CHANGE lands where the tempo map says it
should. None of that can be asserted from a screenshot, and a headless test
cannot play anything.

samples/MusicDemo therefore carries an unattended pass over exactly those
behaviours (src/libs/MusicDemo.Game/MusicDemoWalkthrough.cs). It is OFF unless
the environment variable MUSICDEMO_SELFTEST is set to 1, so a person running the
sample never meets it. Build the head and run it with a timeout, then read the
log:

    dotnet build samples/MusicDemo/src/MusicDemo.LinuxX11 -c Release
    cd samples/MusicDemo/src/MusicDemo.LinuxX11/bin/Release/net10.0
    DISPLAY=:0 MUSICDEMO_SELFTEST=1 timeout 90 ./MusicDemo.LinuxX11 > /tmp/musicdemo.log 2>&1

Every check writes a PASS/FAIL line prefixed MUSICDEMO-SELFTEST, and the last
line is "RESULT PASS (n/n checks)". Delete the head's GeneratedMusic folder first
when the asset factory has changed; assets are written only when missing.

PACKAGING AND PUBLISHING
========================
Every packable project sets GeneratePackageOnBuild=true, so an ordinary Release
build produces the .nupkg — except while UseLocalEngineProject is true, which
forces it off (see LOCAL VERIFICATION ESCAPE HATCH above).

VERSIONING SCHEME (every package, independently)
------------------------------------------------
Date-stamped and auto-incrementing: 1.<x>.<y>.<z>, every field derived from UTC
"now" — major always 1; minor = whole years since the _VersionBaseYear property
in the csproj; build = day of year (1-based, UTC); revision = minute of day
(0..1439, UTC). Strictly increasing over time. Notes:

  * Every build produces a NEW version, so with GeneratePackageOnBuild=true each
    build yields a fresh .nupkg — and two builds in the SAME UTC minute produce
    the SAME version, so do not publish two packages from within one minute.
  * This is date-stamp versioning, not SemVer: minor encodes the year and major
    is pinned, so major/minor do not signal API compatibility.
  * Re-baseline by changing _VersionBaseYear.

WHAT SHIPS IN CodeBrix.Platform.GameEngine.MitLicenseForever
-------------------------------------------------------------
Packed by src/CodeBrix.Platform.GameEngine.Host.

  * BOTH assemblies in lib/<tfm>: the host's own output, plus
    CodeBrix.Platform.GameEngine.dll and its .xml injected by the
    _IncludeEngineInPackage target (TargetsForTfmSpecificBuildOutput). This is
    necessary because the engine project is IsPackable=false AND the
    ProjectReference to it uses PrivateAssets="all", so NuGet would otherwise
    ship neither a dependency nor the dll.
  * icon-codebrix-128.png, README.md (PackageReadmeFile),
    THIRD-PARTY-NOTICES.txt, and THE REPOSITORY-ROOT AGENT-README.txt.
  * PackageLicenseExpression MIT; PackageRequireLicenseAcceptance true.

WHAT SHIPS IN CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever
-------------------------------------------------------------------
Packed by src/CodeBrix.Platform.GameEngine.Sdl2.

  * Its own assembly, plus a PackageReference dependency on the engine package.
  * icon-codebrix-128.png, the repository-root README.md and
    THIRD-PARTY-NOTICES.txt, and ITS OWN LOCAL AGENT-README.txt (the file in the
    Sdl2 project folder, NOT the repository-root one).
  * SDL2 natives from native_libraries/, packed to runtimes/<rid>/native for
    win-x64, win-x86, win-arm64, osx-x64 and osx-arm64. NO LINUX BINARY — the
    system libSDL2 is used there instead, deliberately.
  * PackageLicenseExpression "MIT AND Zlib". The suffix rationale: the managed
    binding code in Native/ is derived from Veldrid and is MIT, while the SDL2
    native binaries are zlib; the suffix tracks the more notice-demanding of the
    two.

WHAT SHIPS IN CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever
--------------------------------------------------------------------------
Packed by src/CodeBrix.Platform.GameEngine.KenneyAssets.

  * Its own assembly, plus PackageReference dependencies on the engine package
    and on CodeBrix.Graphics3D.Gltf2.MitLicenseForever (the ONLY direct
    third-party reference; everything else — SkiaSharp, CodeBrix.Compression for
    zip reading, CodeBrix.SkiaSvg, CodeBrix.Audio — arrives transitively through
    the engine).
  * icon-codebrix-128.png, the repository-root README.md and
    THIRD-PARTY-NOTICES.txt, and ITS OWN LOCAL AGENT-README.txt (the file in the
    KenneyAssets project folder, NOT the repository-root one).
  * NO ASSETS. No Kenney bundle, no fixture and no native binary: the package is
    the reader, and a game ships the CC0 bundles it uses.
  * PackageLicenseExpression MIT; PackageRequireLicenseAcceptance true.

Two packaging sharp edges recorded in the Sdl2 csproj, worth not re-learning:

  * The runtimes PackagePath values use FORWARD SLASHES WITH NO TRAILING
    SEPARATOR. A trailing separator produced package paths with an empty segment
    ("runtimes/win-x64/native//SDL2.dll"), and NuGet matches RID-specific assets
    on the exact "runtimes/{rid}/native/{file}" shape — an empty segment risks
    the native never being selected, which looks identical to SDL2 simply being
    absent.
  * macOS: the SAME universal (x86_64 + arm64) binary is packed under BOTH macOS
    RIDs. .NET's real macOS RIDs are osx-x64 and osx-arm64, and a single copy
    under runtimes/osx/native/ would depend on RID-graph fallback, which .NET 8+
    de-emphasized by default.

WHAT SHIPS IN CodeBrix.Platform.GameEngine.CardsAndDice.MitLicenseForever
--------------------------------------------------------------------------
Packed by src/CodeBrix.Platform.GameEngine.CardsAndDice.

  * BOTH assemblies in lib/<tfm>: its own output, plus
    CodeBrix.Platform.GameEngine.CardsAndDice.Assets.dll and its .xml injected
    by the _IncludeCardsAndDiceAssets target (TargetsForTfmSpecificBuildOutput)
    — the same shape the Host project uses for the engine core, and for the
    same reason: the Assets project is IsPackable=false and is referenced with
    PrivateAssets="all", so NuGet would otherwise ship neither a dependency nor
    the dll. There is NO Assets package and the package must carry no
    dependency on one.
  * A PackageReference dependency on the engine package, and nothing else.
  * The artwork, the sound effects and catalog.json are EMBEDDED RESOURCES of
    the Assets dll ("cardsdice/<path>"): no contentFiles, no loose SVG/Ogg, no
    build target and no extraction step ship.
  * icon-codebrix-128.png, the repository-root README.md and
    THIRD-PARTY-NOTICES.txt (which carries the per-file provenance of every
    embedded asset), and ITS OWN LOCAL AGENT-README.txt.
  * PackageLicenseExpression MIT; PackageRequireLicenseAcceptance true.

Check a fresh package with unzip -l: both dlls and both .xml files under
lib/net10.0, README.md, AGENT-README.txt, icon-codebrix-128.png and
THIRD-PARTY-NOTICES.txt at the root, and in the .nuspec exactly one dependency
(the engine package).

    dotnet pack src/CodeBrix.Platform.GameEngine.CardsAndDice -c Release -o <folder>

Source consumers (the in-repo sample and tests) reference the Assets project
explicitly when they use AssetCatalog, because the library's own reference to
it is private.

PUBLISH ORDER
-------------
ENGINE FIRST, then whichever add-on needs something new from it. Publish the
engine package, wait for it to index, bump the pinned engine version in the
add-on csproj, then build and publish that add-on. Never pack an add-on with
UseLocalEngineProject=true (the build blocks it). Consuming projects downstream
of an add-on (padcheck, the samples) are re-pinned afterwards.

RELEASE NOTES
-------------
A release that changes behaviour or API gets a dated release-notes file at the
repository root, named RELEASE-NOTES-<yyyy-MM-dd>.md. The current one is
RELEASE-NOTES-2026-09-17.md, which is not published yet and is therefore still
being amended in place. Conventions:

  * BREAKING changes come FIRST, each with the old shape, the new shape and
    what a consumer has to do. Behaviour changes that need no code edit but
    change what the game does (a reversed sign, a moved anchor, a new default)
    count as breaking and belong in that section.
  * Then new features, then fixes, then the sample/repository changes.
  * Say member names, default values and file names explicitly; a reader should
    be able to grep their own code from the notes.
  * The files are additive history — write a NEW dated file for the next
    release rather than rewriting an old one.
  * Put the new file in the .slnx Solution Items folder (replacing the previous
    one there) so it is visible in the IDE.

Release-notes files are repository content only: they are not packed into any
NuGet package (only README.md, THIRD-PARTY-NOTICES.txt and the relevant
AGENT-README.txt are — see PACKAGING). Update AGENT-README.txt in the same pass,
because that file DOES ship and consumers read it as the current truth.

PROVENANCE AND VENDORED SOURCES
===============================
ENGINE CORE — a vendored port of an open-source, MIT-licensed game engine
((c) 2025 Michael Adkins). THIRD-PARTY-NOTICES.txt names the upstream project
and carries the exact revision this port tracks, plus the vendoring history;
update the "Version:" line there whenever upstream changes are merged in.
Namespaces are CodeBrix.Platform.GameEngine[.*]; the upstream namespaces are not
used. Ported files carry "//was previously:" markers on the changed namespace
lines, and files written for this port rather than ported carry
"//CodeBrix (not from Gondwana)" in the same position. Keep both markers
accurate: they are how a later merge tells ported code from ours.

SDL2 MANAGED BINDINGS — the files under
src/CodeBrix.Platform.GameEngine.Sdl2/Native are vendored from the Veldrid
project's Veldrid.SDL2 bindings (MIT, (c) 2017 Eric Mellino and Veldrid
contributors), file by file with the source path recorded in each file's header
and every divergence marked inline with "for CodeBrix". The substantive
divergences are: block namespaces converted to file-scoped and re-rooted at
CodeBrix.Platform.GameEngine.Sdl2.Native; the NativeLibraryLoader package
dependency removed in favour of Sdl2Library (built on the shared framework's
NativeLibrary type, so the binding layer carries no NuGet dependencies at all);
loaded delegates made
nullable and null-checked so a missing SDL2 is reported rather than thrown;
GetErrorString(), SDL_Quit, SDL_WasInit and SDL_JoystickGetDeviceInstanceID
added; SDLInitFlags marked [Flags]; XML doc comments added throughout. Only the
joystick and game controller subsystems are bound.

THE "NEVER THROWS" RULE IS THE POINT OF THE FORK. Veldrid assigned its library
handle from a static field initializer that threw when SDL2 could not be found,
so a missing SDL2 surfaced as a TypeInitializationException from the first
unrelated member touched on the bindings class. Here a failed load is recorded
and reported through Sdl2Library.IsLoaded / LoadFailureDetail instead, and every
resolved function simply comes back null. Preserve that property in any change.

SDL2 NATIVE BINARIES — zlib licensed, (c) Sam Lantinga. Committed under
native_libraries/<rid>/ with a .provenance.txt beside each binary recording the
SDL2 version, origin URL, archive and output SHA-256, sizes and vendoring date.
Windows x64/x86 and the macOS universal dylib are OFFICIAL DOWNLOADS from the
SDL release pages; Windows ARM64 is BUILT FROM SOURCE by
tools/sdl2_library_building/build-sdl2-windows-arm64.ps1 because upstream ships
no ARM64 binary. The vendored set is SDL2 2.32.10 (vendored 2026-07-19). When
bumping, refresh every platform in the same pass so all shipped binaries are the
same SDL2 version, and add the new source tarball SHA-256 to the script's
$PinnedHashes table so the pin stays under version control.

KENNEY ASSET SUPPORT — NO VENDORED SOURCE. The KenneyAssets project is code
written for this repository; it vendors nothing and carries no provenance
markers. Its one direct third-party dependency,
CodeBrix.Graphics3D.Gltf2.MitLicenseForever, is an ordinary PackageReference
(MIT, no dependencies of its own) and is recorded in THIRD-PARTY-NOTICES.txt as
a package, not as incorporated source. What it READS — Kenney's bundles — is CC0
content a game supplies. Its test fixtures under
tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests/fixtures/ are credited in
FIXTURES-LICENSE.txt beside them and ship in no package. (The separate
CardsAndDice add-on does embed selected Kenney artwork and sounds; see below.)
When adding or replacing a fixture, keep it small, keep it CC0, and describe in
that file what rule it exercises.

CARDS AND DICE ASSETS — EMBEDDED, NO VENDORED SOURCE. The CardsAndDice code is
written for this repository. Its Assets project REDISTRIBUTES artwork and sounds
inside the package, so every embedded file needs an identified licence that
permits redistribution from an MIT repository. The sources in use are: Kenney
packs (CC0), Adrian Kennard's traditional playing cards (CC0), the 1909/1910
Rider-Waite-Smith tarot scans on Wikimedia Commons (public domain; traced to
vector paths), the Star of Ishtar SVG on Wikimedia Commons (released to the
public domain by its author), and original artwork generated by this
repository's tools (MIT). THIRD-PARTY-NOTICES.txt holds the authoritative
per-source permissions (section "CARDSANDDICE EMBEDDED ASSETS — PROVENANCE
APPENDIX") and a per-file manifest with source and shipped SHA-256 hashes.
catalog.json carries the same data at run time, and the test suite fails if the
two drift from the files. Copies of the Kenney pack licence files are kept in
the Assets project's Provenance/ folder, and the per-file Wikimedia evidence for
the tarot in tarot-provenance.json; neither is embedded.

The asset tools under tools/cards-and-dice are maintainer utilities: no build,
test, pack or consumer ever runs them, and none of their dependencies ship.

    tools/cards-and-dice/import-assets.py --kenney '/path/to/Kenney All-in-1'
    tools/cards-and-dice/import-assets.py --playing
    tools/cards-and-dice/import-assets.py --tarot
    tools/cards-and-dice/import-assets.py --ishtar
    tools/cards-and-dice/generate-originals.py
    tools/cards-and-dice/update-notices.py

  * import-assets.py copies or downloads third-party sources into Resources/
    and records them in catalog.json; the network imports run only when asked.
    --tarot additionally needs Pillow and vtracer in a Python environment you
    set up yourself (the exact versions used are recorded in the notices). It
    is resumable, throttles its requests, keeps its scans in the system temp
    folder (it honours TMPDIR), and skips tarot cards that already exist; to
    retrace one deliberately, delete that generated SVG first.
  * generate-originals.py regenerates the original MIT artwork.
  * update-notices.py rewrites ONLY the CardsAndDice appendix at the end of
    THIRD-PARTY-NOTICES.txt from catalog.json, re-checking every hash. Run it
    after any asset change, and run it last.
  * Never run two catalog-writing commands at the same time.
  * Verify the rights to any replacement source BEFORE importing it; a
    public-domain metadata field on a new edition is not a review of it. Keep
    every new source CC0, public domain, or authored for the repository unless
    the change is discussed first, and record its provenance in the notices.

THIRD-PARTY-NOTICES.txt (repository root) carries the full license texts and
ships inside EVERY package.

CODING CONVENTIONS
==================
  * Target net10.0 only; never multi-target.
  * File-scoped namespaces; usings at the top (System.* first), never global
    usings.
  * XML doc comments on public/protected members (GenerateDocumentationFile
    = true; fix CS1591 at the source, never suppress).
  * xUnit v3 + SilverAssertions for tests; no coverage collector.
  * No project-wide warning suppression except the documented port exceptions
    below.

PORT EXCEPTIONS (this repository)
---------------------------------
  * NULLABLE REFERENCE TYPES ARE ENABLED (<Nullable>enable</Nullable>) on all
    three projects. The upstream source relies on "?" annotations throughout;
    stripping them would change observable public signatures and reduce
    fidelity. This is the same sanctioned exception used by
    CodeBrix.Platform.OpenGL. Because NRT is on, "?" on reference types and the
    "!" null-forgiveness operator ARE permitted in this repository, unlike the
    family default.
  * The engine core carries a scoped NoWarn list for warning categories inherent
    to the ~31k-line upstream and NOT introduced by the port: 1591 (undocumented
    public members — upstream set the same), 1573/1574/1572/1587/0419 (upstream
    XML-doc param/cref issues), and 8618/8603/8625/8602/8604 (nullable-reference
    FLOW warnings, a direct consequence of keeping NRT on for fidelity). A
    dedicated warning-cleanup pass, especially fixing the doc crefs, is a
    recommended follow-up. The Host and Sdl2 projects carry no such list.

ARCHITECTURE
============
CodeBrix.Platform.GameEngine (core)
    deps: SkiaSharp, CodeBrix.SkiaSvg, CodeBrix.Compression, CodeBrix.Audio,
          System.Text.Json + CodeBrix.Json.Extensions, Microsoft.Extensions.*
    No CodeBrix.Platform UI dependency. Rendering seam = SKImage + adapter base.

CodeBrix.Platform.GameEngine.Host
    refs: CodeBrix.Platform.GameEngine
    deps: CodeBrix.Platform, CodeBrix.Platform.SkiaSharp.Views,
          CodeBrix.Platform.Graphics3DGL (GPU path), SkiaSharp,
          CodeBrix.Platform.Svg (platform-integrated SVG)
    CpuRendering = CPU BitmapBackbuffer adapter (default, all heads).
    GpuRendering = GPU GpuBackbuffer adapter via the backend-neutral
    SkiaGpuContext (Graphics3DGL) + one-copy readback (opt-in:
    GameSurfaceCanvas.UseGpuRendering). GPU-thread surfaces skip the cycle's
    render step; the adapter renders them via GlRenderAndSnapshot on the UI
    thread at TargetFPS cadence. They park during the global pause, are captured
    by the pause snapshot via the adapter's latest presented frame, and get one
    adapter-driven paused-overlay frame after the Paused handlers run. All three
    frame drivers (both adapters and the Mode-B presenter) stop posting to the
    dispatcher while their canvas is unloaded, and the GPU adapter releases its
    surface and context while the window is still alive, rebuilding them lazily
    if the canvas reloads.
    The adapter builds its GRContext through SkiaGpuContext.TryCreate, which
    resolves the head's GPU backend behind one API — OpenGL/GLES on the Windows,
    X11, Wayland and Frame Buffer heads (via OffscreenGLContext), Skia-on-Metal
    on macOS (a separate GRContext on its own command queue, on the window's
    MTLDevice) — and returns false (-> CPU fallback) where none is available.
    Requires CodeBrix.Platform with SkiaGpuContext (the X11 GL wrapper also
    filters the garbage egl* stubs glvnd/Mesa returns from glXGetProcAddress,
    the cause of the earlier assembled-interface segfault).

CodeBrix.Platform.GameEngine.Sdl2
    refs: the PUBLISHED CodeBrix.Platform.GameEngine.MitLicenseForever package.
    Fills the engine's IGamepadManager<T> / IGamepadAdapter seam.
    Gamepad/ holds the managed layer (SdlGamepadManager, SdlGamepadAdapter,
    SdlGamepadButtons, SdlGamepadUnavailableCause, and the internal
    SdlAxisConversion, which is kept separate precisely so the conversions that
    are easy to get wrong — axis inversion and the asymmetric range of a signed
    16-bit value — can be exercised with fabricated raw values). Native/ holds
    the vendored bindings and the loader. EngineGamepadExtensions.cs at the
    project root is the single entry point.
    SDL2 is initialized with the GAME CONTROLLER SUBSYSTEM ONLY, which starts no
    video subsystem, so one implementation serves all six heads with no
    contention for the display connection. SDL2's controller EVENTS are disabled
    at start-up and its event queue is never pumped — state is polled directly,
    so no second event loop runs alongside the CodeBrix.Platform one.

CodeBrix.Platform.GameEngine.CardsAndDice (+ .Assets)
    refs: the PUBLISHED engine package (a ProjectReference only with
          UseLocalEngineProject=true), and the Assets project privately.
    No other dependency. The logical model (CardDefinition, Card, CardPile,
    Deck, Die) is plain .NET with no engine dependency; CardsAndDiceTable is
    the presentation and input layer, drawing through a DrawList and attached
    to an engine view or scene layer by UseCardsAndDice. Outcomes are decided
    by the logic before any animation starts and animation never consumes the
    random source. SVG artwork is rasterized lazily into a table-owned image
    cache, optionally on a worker (ArtworkPreparation) that touches nothing but
    the isolated SVG.

CodeBrix.Platform.GameEngine.KenneyAssets
    refs: the PUBLISHED engine package (a ProjectReference only with
          UseLocalEngineProject=true).
    deps: CodeBrix.Graphics3D.Gltf2 only; SkiaSharp, CodeBrix.Compression,
          CodeBrix.SkiaSvg and CodeBrix.Audio come through the engine.
    Fills the engine's IGameAssetProvider seam for Kenney bundles, implementing
    every capability interface the contract defines. Two layers, deliberately:
    a CATALOG (Sources/ + Parsing/) that reads only the archive listing and the
    small atlas, tile set and map documents, and MATERIALIZING (Materialize/ +
    Models/) that turns one catalogued asset into an engine object registered
    under the asset's own key. All transformation lives here — the engine core
    knows nothing Kenney-shaped, and the contract stays describe-and-materialize.
    Only six types are public; the rest is internal and reached by the test seam.
    The model work is the largest piece: a glTF reader onto the engine's
    GameModel family, and a pure-managed z-buffered software rasterizer that
    pre-renders a model into a sprite sheet, so no GPU and no display is needed
    and the tests run headless.

NOTES
=====
  * The engine core is a process-global singleton machine. Anything that
    populates Engine.Instance or the scene/sprite/cycle/tilesheet/audio
    registries has to clean up after itself — in tests and in tools alike.
  * Engine.Dispose() does NOT dispose an attached gamepad manager; it only stops
    button monitoring. That is documented as a consumer-facing rule in the Sdl2
    AGENT-README, and it is the reason the Sdl2 tests assert Dispose()
    idempotence and post-Dispose Update() safety.
  * samples/ is the living reference for the engine's subsystems and each sample
    carries its own .slnx; see EXTRAS-README.txt.
  * An asset PROVIDER must be callable from more than one thread: the registry
    locks its own bookkeeping but calls a provider outside that lock.
    Materializing also has to be idempotent by key, because the engine's
    registries hold one object per key and re-registering disposes what a game is
    already using — the KenneyAssets provider is the worked example of both.
  * Documentation files in this repository: see README-INDEX.txt. The
    repository-root AGENT-README.txt ships in the engine package; each add-on
    project's local AGENT-README.txt ships in that add-on's package; this file,
    EXTRAS-README.txt and the RELEASE-NOTES-*.md files ship in none of them.

================================================================================
END OF MAINTAINER-README
================================================================================
