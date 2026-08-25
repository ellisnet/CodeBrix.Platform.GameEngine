================================================================================
MAINTAINER-README: CodeBrix.Platform.GameEngine
Notes for people and agents MAINTAINING this repository — not for package consumers
================================================================================

If you are CONSUMING one of the NuGet packages, this is the wrong file. Read
AGENT-README.txt (repository root) for the game engine, or
src/CodeBrix.Platform.GameEngine.Sdl2/AGENT-README.txt for gamepads. See
README-INDEX.txt for the map.

PURPOSE AND SCOPE
=================
This repository produces TWO published NuGet packages from THREE projects.

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

The two packages are versioned and PUBLISHED INDEPENDENTLY; they do not share a
version number. That is intentional — the Sdl2 project uses only PUBLIC engine
API and has no InternalsVisibleTo seam into the engine core.

REPOSITORY LAYOUT
=================
    src/CodeBrix.Platform.GameEngine/          engine CORE (IsPackable=false)
    src/CodeBrix.Platform.GameEngine.Host/     host layer; PACKS both assemblies
    src/CodeBrix.Platform.GameEngine.Sdl2/     gamepad add-on; packs itself
    tests/CodeBrix.Platform.GameEngine.Tests/
    tests/CodeBrix.Platform.GameEngine.Host.Tests/
    tests/CodeBrix.Platform.GameEngine.Sdl2.Tests/
    samples/                                   seven complete games/demos
    tools/padcheck/                            hand-run gamepad hardware check
    tools/sdl2_library_building/               SDL2 Windows-ARM64 build script
    native_libraries/<rid>/                    committed SDL2 binaries + provenance
    CodeBrix.Platform.GameEngine.slnx          product projects + tests only

Engine-core source is grouped into sub-folders that mirror the sub-namespaces
(Assets, Audio, Configuration, Drawing, Extensibility, Input, Logging, Physics,
Rendering, Scenes, Serialization, SkiaSharp, Timers); entry types (Engine.cs,
EngineState.cs, EngineDispatcher.cs, ...) sit at the project root. The Sdl2
project follows the same rule with Gamepad/ and Native/ sub-folders and
EngineGamepadExtensions.cs at the root.

Neither samples/ nor tools/ is in the .slnx, deliberately: the solution holds
only product projects and their tests. Each sample carries its own .slnx.

INTERNALS SEAMS
---------------
    CodeBrix.Platform.GameEngine  -> .Host and .Tests
    CodeBrix.Platform.GameEngine.Host -> .Host.Tests
    CodeBrix.Platform.GameEngine.Sdl2 -> .Sdl2.Tests
Every packable project ships an InternalsVisibleTo.cs to its own .Tests
assembly. The Sdl2 project has NO seam into the engine core.

BUILDING
========
    dotnet build CodeBrix.Platform.GameEngine.slnx

All three projects are net10.0 only; never multi-target. All three set
<Nullable>enable</Nullable> and turn on GenerateDocumentationFile. The Sdl2
project additionally sets <AllowUnsafeBlocks>true</AllowUnsafeBlocks> —
the SDL2 bindings are function-pointer based and pass byte* strings; the unsafe
context is confined to the Native folder.

The build NEVER reaches the network for native binaries. Everything under
native_libraries/ is committed and read straight off disk at pack time.

THE Sdl2 PROJECT BUILDS AGAINST THE PUBLISHED ENGINE
-----------------------------------------------------
src/CodeBrix.Platform.GameEngine.Sdl2 consumes the engine as a PUBLISHED
PackageReference, not a ProjectReference. It has to: the engine project is
IsPackable=false and its dll is embedded into the Host project's package, so a
ProjectReference would compile locally but produce a package with an unsatisfied
runtime dependency (NuGet emits no dependency for a non-packable
ProjectReference, and the engine dll would not be inside the Sdl2 package
either).

Consequences to keep in mind:

  * If a change in Sdl2 needs the engine core to expose something new: publish
    the engine package FIRST, wait for it to index, bump the pinned version in
    the Sdl2 csproj, then build and publish Sdl2.
  * NEVER configure a local folder as a NuGet source holding a freshly-built
    engine package. Every engine build restamps its version, so a local source
    could shadow nuget.org and Sdl2 would be built against an engine version
    that was never published.

LOCAL VERIFICATION ESCAPE HATCH
-------------------------------
    dotnet build src/CodeBrix.Platform.GameEngine.Sdl2/CodeBrix.Platform.GameEngine.Sdl2.csproj \
        -p:UseLocalEngineProject=true

That swaps the engine PackageReference for a ProjectReference so a local run
tests THIS repository's engine source, and it forces GeneratePackageOnBuild off.
A _BlockPackWithLocalEngineProject target makes Pack fail outright while the
flag is set, because the resulting package would carry no engine dependency and
no engine dll and must never be published.

The same flag applies to padcheck, which sits downstream of Sdl2:

    dotnet build tools/padcheck/padcheck.csproj -p:UseLocalEngineProject=true

Without it, a local engine fix appears to have no effect, which is
indistinguishable from the fix not working. This is exactly how gamepad support
once shipped "fully hardware-verified" while being completely dead on the
InputPump path — nothing that ran against real hardware had ever been built from
local engine source.

TESTING
=======
    dotnet test CodeBrix.Platform.GameEngine.slnx

xUnit v3 + SilverAssertions, with coverlet.collector for coverage. No opt-in
environment variables are required; head-dependent host behavior is env-gated or
skipped with a reason.

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
(CollectionBehavior(DisableTestParallelization = true) in AssemblyInfo.cs): the
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

PACKAGING AND PUBLISHING
========================
Both packable projects set GeneratePackageOnBuild=true, so an ordinary Release
build produces the .nupkg.

VERSIONING SCHEME (both packages, independently)
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

Two packaging sharp edges recorded in the csproj, worth not re-learning:

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

PUBLISH ORDER
-------------
Engine first, Sdl2 second, and only when Sdl2 needs something new from the
engine. Publish the engine package, wait for it to index, bump the pinned engine
version in the Sdl2 csproj, then build and publish Sdl2. Never pack Sdl2 with
UseLocalEngineProject=true (the build blocks it).

PROVENANCE AND VENDORED SOURCES
===============================
ENGINE CORE — a vendored port of the open-source Gondwana game engine version
2.5.0 (MIT, (c) 2025 Michael Adkins). Namespaces are
CodeBrix.Platform.GameEngine[.*]; the upstream namespaces are not used. Ported
files carry "//was previously:" markers on the changed namespace lines.

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

THIRD-PARTY-NOTICES.txt (repository root) carries the full license texts and
ships inside BOTH packages.

CODING CONVENTIONS
==================
  * Target net10.0 only; never multi-target.
  * File-scoped namespaces; usings at the top (System.* first), never global
    usings.
  * XML doc comments on public/protected members (GenerateDocumentationFile
    = true; fix CS1591 at the source, never suppress).
  * xUnit v3 + SilverAssertions for tests; coverlet.collector for coverage.
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
  * Documentation files in this repository: see README-INDEX.txt. The
    repository-root AGENT-README.txt ships in the engine package; the Sdl2
    project's local AGENT-README.txt ships in the Sdl2 package; this file and
    EXTRAS-README.txt ship in neither.

================================================================================
END OF MAINTAINER-README
================================================================================
