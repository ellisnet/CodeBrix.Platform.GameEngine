================================================================================
EXTRAS-README: CodeBrix.Platform.GameEngine
Samples, tools and other content in this repository that is not part of a NuGet package
================================================================================

Nothing described in this file ships in either NuGet package, and nothing here
builds as part of the library build, the test run or the packaging build. None
of it is in CodeBrix.Platform.GameEngine.slnx, deliberately: that solution holds
only the product projects and their tests.

  samples/    seven complete games/demos — the living reference for the engine
  tools/      two hand-run developer utilities

SAMPLES
=======
samples/ holds seven complete CodeBrix.Platform applications. Each has the same
shape: a shared .UI shared-project (App.xaml, Views/MainPage.xaml), a .Core
library holding the view models and the engine reference, a .Game library
holding the game itself, and three executable heads — LinuxX11, Win32Skia and
MacOS. Each sample carries its OWN .slnx, so it is built and run on its own:

    dotnet run --project samples/<Name>/src/<Name>.LinuxX11/<Name>.LinuxX11.csproj

Swap the head project for <Name>.Win32Skia or <Name>.MacOS on the other
platforms. Each sample is the reference consumer for the subsystems it
exercises.

samples/Spot.Brix
-----------------
Mode A via CodeBrixGameHost — the recommended hosting shape. Scenes, sprites,
tilesheets, engine mouse and keyboard input, the toolbar/focus recipe
(src/Spot.Brix.UI/Views/MainPage.xaml.cs) and per-move callbacks
(src/libs/Spot.Brix.Game/SpotBrixGameHost.cs). Start here to see what a normal
game host looks like.

samples/Slider
--------------
Mode A driving Engine directly, with no host base class. Sprites built on the
engine thread via EngineDispatcher.Post, engine mouse events, and rebuilding the
board while the engine keeps running.

samples/CoordinateTest
----------------------
Mode A, direct Engine. Coordinate systems — orthogonal, isometric and hex — plus
cameras and views.

samples/ParticleTest
--------------------
Mode A, direct Engine. ParticleSurface and emitters, DirectComposite /
TextBlock / DirectRectangle, movement easing, and the campfire click that
toggles the GLOBAL pause — UI-level pointer input with letterbox mapping
(src/libs/ParticleTest.Game/ParticleTestGame.cs, OnCanvasPointerPressed).
Set the environment variable PARTICLETEST_USE_GPU=1 to run it on the GPU
rendering path instead of the CPU one.

samples/SoftRender
------------------
Mode B end-to-end, via SoftwareRenderedGameHostBase: a 320x200 / 70 Hz plasma
and starfield, the pixel-frame presenter, InputPump, raw-PCM blips
(SoundChannel), a streamed drone (StreamingAudioSource), a zero-allocation frame
loop and loop-health statistics (src/libs/SoftRender.Game/SoftRenderGameHost.cs).
This is the shape a software-rendered port uses.

samples/GpuRender
-----------------
Mode A, direct Engine — the GPU-rendering showcase and SoftRender's GPU-first
counterpart. A resolution-independent SkSL plasma and starfield drawn by a custom
DirectDrawingBase subclass (PlasmaBackdrop), a stats TextBlock with live GPU FPS,
click-anywhere pause with a pause overlay (the paused-frame and snapshot demo),
and window-tracking resolution with resize handling. Set GPURENDER_USE_CPU=1 to
run the identical scene on the CPU path for comparison.

samples/MusicDemo
-----------------
The MUSIC SYSTEM reference: volume buses, fades and equal-power crossfades,
ducking (both the fire-and-forget and the held-handle forms), stingers,
playlists, layered adaptive stems (MusicStemSet) and the MIDI per-channel route,
transitions quantised to the next bar, marker jump points, and the global pause
freezing music and fades together. It GENERATES every asset it plays on first
run (src/libs/MusicDemo.Game/MusicAssetFactory.cs builds the stems, two tracks, a
stinger, an SFZ instrument and a MIDI file with markers), so the repository
carries no binary music and the sample runs anywhere.

NOTE: no sample wires up gamepads. Controller behavior is verified with
tools/padcheck instead, because the thing that has to be checked is physical
hardware rather than an on-screen result.

TOOLS
=====

tools/padcheck
--------------
An interactive HARDWARE check for the SDL2 gamepad add-on. It drives the real
SdlGamepadManager — it is not a reimplementation — so what it prints is what a
game would actually see. It is deliberately not in the repository .slnx.

It exists because the unit tests cover everything that can be asserted without
hardware (the loader shim, the availability and reason logic, the button-name
table ordering, the axis conversions) but cannot cover what is most likely to be
wrong in practice: whether each PHYSICAL button reports under the CORRECT NAME,
whether pushing a stick UP actually reports Up, whether the LEFT trigger is the
one reporting as left, and whether a controller that SLEEPS AND WAKES is picked
back up.

    cd tools/padcheck
    dotnet run -- 45                  # direct mode; seconds to poll (default 30)
    dotnet run -- 45 --pump           # the InputPump (Mode-B) path

RUN BOTH MODES, EVERY TIME. Direct mode refreshes the manager itself, which is
exactly why it once could not detect that NOTHING refreshed the manager on the
InputPump path — gamepads were completely dead in Mode B (frozen state, no
events, no hotplug) while every hardware check passed. A check that supplies its
own driver cannot see a missing driver.

To test engine changes that are not published yet (padcheck sits downstream of
the Sdl2 project, which compiles against the PUBLISHED engine package):

    dotnet build tools/padcheck/padcheck.csproj -p:UseLocalEngineProject=true
    tools/padcheck/bin/Debug/net10.0/padcheck 10 --pump

Suggested physical sequence, with the controller ON: press A, B, X, Y slowly in
that order; push the left stick straight up, hold, then straight down; click both
sticks inward; press both shoulders, all four D-pad directions, Start, Back and
Guide; squeeze both triggers fully. Then run again starting with the controller
OFF and power it on partway through, to exercise reconnect.

What to look for: face buttons print in the order pressed; stick up gives
dir=Up with a POSITIVE Y and stick down gives dir=Down with a negative Y; both
triggers reach 1.00, rest at 0.00, and L is L; "Buttons NOT seen" ends up
"(none - all 15 confirmed)"; hotplug shows a 1 -> 0 line then a 0 -> 1 line;
after reconnect the buttons still work and the GamepadId has CHANGED; and
"Resting-glitch seen" is False.

The face-button ORDER check is the important one. Controller mappings are not
sequential — an Xbox pad over Bluetooth reports A, B, X, Y as b0, b1, b3, b4,
skipping b2 — so an implementation reading raw joystick indices would put X and Y
on the wrong buttons while still looking plausible. Pressing them in a known
order is what catches that. A stick magnitude above 1.00 is EXPECTED, not a
fault; the tool prints a note when it sees one. tools/padcheck/README.md carries
the full tables.

tools/sdl2_library_building
---------------------------
build-sdl2-windows-arm64.ps1 builds the Windows-on-ARM64 SDL2.dll that ships
inside the gamepad package. It is a one-time, run-by-hand utility that reaches
the network; the library build never does. Everything it produces is committed
to native_libraries/ and the packaging build then reads those committed files
straight off disk.

It exists to cover the one gap in what upstream publishes: official prebuilt
SDL2 binaries exist for Windows x64, Windows x86 and macOS (a universal dylib
covering both architectures), but not for Windows ARM64. Linux is not shipped at
all — the system libSDL2 is used there.

It CROSS-COMPILES, so an ARM64 machine is not needed: run it on an ordinary
Windows x64 host with Visual Studio 2022 or newer and two individual components,
"MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools" and "C++ CMake tools for
Windows". The ARM64 build tools usually have to be ticked separately under
Modify -> Individual components; the script names the missing component rather
than failing deep inside CMake.

    cd tools
    .\build-sdl2-windows-arm64.ps1
    .\build-sdl2-windows-arm64.ps1 -SdlVersion <ver> -ExpectedSha256 <sha256>
    Get-Help .\build-sdl2-windows-arm64.ps1 -Full

It locates Visual Studio through vswhere (REQUIRING the ARM64 component),
downloads the official source tarball and verifies it against a SHA-256 pinned
in the script (a mismatch aborts; upstream's GPG signature is checked too when
gpg is installed), configures for ARM64 as a shared library with no tests and
SDL_FORCE_STATIC_VCRT=ON, builds Release, verifies the artifact, then copies
SDL2.dll into native_libraries/win-arm64/ and writes SDL2.dll.provenance.txt
beside it. Useful switches: -OutputPath, -WorkPath, -KeepWorkDir.

SDL_FORCE_STATIC_VCRT=ON MATTERS. The official x64 and x86 SDL2.dll binaries
import no C runtime at all — only OS libraries such as KERNEL32, USER32 and
SETUPAPI — which is why they need no Visual C++ Redistributable on an end user's
machine. The ARM64 build has to match, or it becomes the one native in the
package that demands the ARM64 redistributable, producing a load failure on
clean machines that is thoroughly unpleasant to diagnose. The script checks for
CRT imports after building and warns about any it finds.

The script also reads the PE header and confirms machine type 0xAA64. A
misconfigured cross-compile that silently emits an x64 image is the classic
failure here, and it would otherwise go unnoticed until the DLL failed to load
on an actual ARM64 device — so TEST ON A REAL ARM64 DEVICE before shipping, and
commit the produced SDL2.dll together with its .provenance.txt.

Re-run it only when bumping the SDL2 version, and refresh the downloaded natives
for the other platforms in the same pass so every shipped binary is the same
version. Add the new source tarball's SHA-256 to the $PinnedHashes table in the
script rather than passing -ExpectedSha256 each time, so the pin stays under
version control alongside the binary it produced.
tools/sdl2_library_building/README.md carries the full detail.

OTHER NON-PACKAGE CONTENT
=========================
  native_libraries/    The committed SDL2 native binaries and their
                       .provenance.txt files. Not a tool and not a sample: these
                       ARE packed into the gamepad NuGet package, but they are
                       maintained by hand from the tools above. See
                       MAINTAINER-README.txt.
  tests/               The three test projects. Not shipped in any package; see
                       MAINTAINER-README.txt for how to run them and for the
                       serial-execution and shared-audio-output rules.

================================================================================
END OF EXTRAS-README
================================================================================
