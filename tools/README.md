# tools

Developer tools for maintaining `CodeBrix.Platform.GameEngine`.

**Nothing in this folder runs as part of the library build, the test run, or the
NuGet packaging.** These are one-time, run-by-hand utilities. They may reach out
to the network; the library build never does. Everything they produce is
committed to the repository, and the packaging build then reads those committed
files straight off disk.

---

## `build-sdl2-windows-arm64.ps1`

Builds the Windows-on-ARM64 `SDL2.dll` that ships inside the SDL2 gamepad
add-in package.

### Why this exists

SDL2 provides the gamepad/controller backend. libsdl.org publishes official
prebuilt SDL2 binaries for some platforms but not all, so the natives we ship
come from three different places:

| Target | Where the binary comes from |
| --- | --- |
| Windows x64 | Official download — `SDL2-<ver>-win32-x64.zip` |
| Windows x86 | Official download — `SDL2-<ver>-win32-x86.zip` |
| macOS (Intel + Apple Silicon) | Official download — `SDL2-<ver>.dmg`, one universal dylib covers both |
| **Windows ARM64** | **Built by this script** — upstream ships no ARM64 binary |
| Linux | Not shipped; the system-provided `libSDL2-2.0.so.0` is used |

This script covers the one gap. It builds from the official SDL2 source
release, which — unlike the prebuilt binaries — is GPG-signed by upstream.

### You do not need an ARM64 machine to run it

The script **cross-compiles**. Run it on an ordinary Windows x64 machine; MSVC's
ARM64 toolchain produces the binary there. An ARM64 device is only useful for
*testing* the result. It will also run natively on an ARM64 Windows host if you
prefer.

### Prerequisites

On the machine you build from:

* Windows 10 version 1803 or later, or Windows 11 (for the in-box `tar.exe`)
* Visual Studio 2022 or newer, with these individual components:
  * **MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools**
    (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`)
  * **C++ CMake tools for Windows**
    (`Microsoft.VisualStudio.Component.VC.CMake.Project`)

The *Desktop development with C++* workload includes the CMake component, but
the ARM64 build tools usually have to be ticked separately under
**Modify → Individual components**. If they are missing, the script says so and
names the component rather than failing deep inside CMake.

### Usage

```powershell
cd tools
.\build-sdl2-windows-arm64.ps1
```

Building a version that is not yet pinned in the script:

```powershell
.\build-sdl2-windows-arm64.ps1 -SdlVersion 2.32.12 -ExpectedSha256 <sha256-of-source-tarball>
```

Full help, including every parameter:

```powershell
Get-Help .\build-sdl2-windows-arm64.ps1 -Full
```

Useful switches: `-OutputPath` to write somewhere other than the default
natives folder, `-WorkPath` to control the scratch folder, and `-KeepWorkDir`
to leave the build tree behind for troubleshooting.

### What it does

1. Locates Visual Studio through `vswhere`, *requiring* the ARM64 component, and
   picks a CMake generator by asking CMake which ones it supports.
2. Downloads the official source tarball and verifies it against a SHA-256
   pinned in the script. A mismatch aborts the build. If `gpg` is installed it
   additionally verifies upstream's signature.
3. Configures for ARM64: shared library only, no tests, and
   `SDL_FORCE_STATIC_VCRT=ON`.
4. Builds Release.
5. Verifies the artifact — see below.
6. Copies `SDL2.dll` into the natives folder and writes
   `SDL2.dll.provenance.txt` beside it, recording the SDL2 version, source URL,
   source and output hashes, build host, toolchain, and CMake options.

### Why `SDL_FORCE_STATIC_VCRT=ON` matters

The official x64 and x86 `SDL2.dll` binaries import **no C runtime at all** —
only OS libraries such as `KERNEL32`, `USER32` and `SETUPAPI`. That is why they
need no Visual C++ Redistributable on an end user's machine.

Our ARM64 build has to match. Without the static CRT it would be the one native
in the package that demands the ARM64 redistributable, producing a load failure
on clean machines that is thoroughly unpleasant to diagnose. The script checks
this after building and warns if any CRT import shows up.

### Verification the script performs

* **PE machine type** — reads the PE header and confirms `0xAA64` (ARM64). A
  misconfigured cross-compile that silently emits an x64 image is the classic
  failure here, and it would otherwise go unnoticed until the DLL failed to
  load on an actual ARM64 device.
* **CRT imports** — runs `dumpbin /dependents` and warns on any
  `vcruntime` / `ucrtbase` / `msvcp` / `api-ms-win-crt` entry.

### Afterwards

1. Commit the produced `SDL2.dll` together with its `.provenance.txt`.
2. Test on an actual ARM64 device before shipping. Cross-compiled output that
   builds and verifies cleanly can still fail at runtime.

### When to re-run

Only when bumping the SDL2 version. SDL2 is in maintenance mode and sees a
handful of releases a year; we are not obliged to track every one. When you do
bump it, add the new source tarball's SHA-256 to the `$PinnedHashes` table in
the script rather than passing `-ExpectedSha256` each time, so the pin stays
under version control alongside the binary it produced.

Remember to refresh the downloaded natives for the other platforms at the same
time, so every shipped binary is the same SDL2 version.

---

## Licensing

SDL2 is licensed under the zlib license, Copyright © 1997-2025 Sam Lantinga.
Binaries produced or downloaded for redistribution are covered by that license;
the full text is reproduced in `THIRD-PARTY-NOTICES.txt` in the repository root.
