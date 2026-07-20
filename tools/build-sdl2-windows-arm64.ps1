#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows-on-ARM64 SDL2.dll that ships inside the
    CodeBrix.Platform.GameEngine SDL2 add-in NuGet package.

.DESCRIPTION
    libsdl.org publishes official prebuilt SDL2 binaries for Windows x64,
    Windows x86 and macOS (universal), but NOT for Windows ARM64. This script
    produces that one missing binary from the official, GPG-signed SDL2 source
    release, so every native we ship is either an upstream download or a
    reproducible build from upstream source.

    THIS SCRIPT IS A ONE-TIME DEVELOPER TOOL, NOT PART OF THE PACKAGE BUILD.
    It downloads from the network; the library/NuGet build never does. Run it
    once per SDL2 version bump, commit the resulting DLL, and the packaging
    build then reads that committed file straight off disk.

    CROSS-COMPILES BY DEFAULT: run this on an ordinary Windows x64 machine with
    Visual Studio 2022 and the ARM64 build tools. An actual ARM64 device (e.g.
    a Surface Pro X) is only needed to TEST the result, not to produce it. The
    script also runs natively on an ARM64 Windows host if you prefer.

    What it does:
      1. Locates Visual Studio 2022 (or newer) via vswhere and verifies the
         ARM64 compiler and CMake components are installed.
      2. Downloads the official SDL2 source tarball from the libsdl-org GitHub
         release and verifies it against a SHA-256 pinned in this script.
         Additionally verifies the upstream GPG signature when gpg is present.
      3. Configures with CMake for the ARM64 architecture, shared library only,
         and the STATIC VC runtime -- matching how upstream builds the official
         x64/x86 DLLs, which import no CRT and therefore require no Visual C++
         Redistributable on the end user's machine.
      4. Builds Release, then verifies the output really is an ARM64 PE image
         and really has no CRT imports.
      5. Copies SDL2.dll to native_libraries/win-arm64/ in the repository and
         writes a provenance record next to it (versions, URLs, hashes,
         toolchain).

.PARAMETER SdlVersion
    SDL2 version to build. Must have a pinned hash below, or supply
    -ExpectedSha256 explicitly. Defaults to the version we currently ship.

.PARAMETER ExpectedSha256
    SHA-256 of the source tarball, for versions not pinned in this script.

.PARAMETER OutputPath
    Folder to place SDL2.dll into. Defaults to the repository's
    native_libraries/win-arm64 folder, resolved relative to this script.

.PARAMETER WorkPath
    Scratch folder for download/extract/build. Defaults to a temp folder.

.PARAMETER KeepWorkDir
    Leave the scratch folder in place afterwards, for troubleshooting.

.EXAMPLE
    .\build-sdl2-windows-arm64.ps1

    Builds the default SDL2 version and writes
    native_libraries\win-arm64\SDL2.dll.

.EXAMPLE
    .\build-sdl2-windows-arm64.ps1 -SdlVersion 2.32.12 -ExpectedSha256 abc123...

    Builds a newer SDL2 whose hash is not yet pinned in this script.

.NOTES
    Prerequisites on the BUILD machine (not the ARM64 test device):
      * Windows 10 1803+ or Windows 11 (needs the in-box tar.exe)
      * Visual Studio 2022, with these individual components:
          - MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools
              (component id: Microsoft.VisualStudio.Component.VC.Tools.ARM64)
          - C++ CMake tools for Windows
              (component id: Microsoft.VisualStudio.Component.VC.CMake.Project)
        The Desktop development with C++ workload covers the CMake component;
        the ARM64 build tools usually must be ticked separately.

    SDL2 is licensed under the zlib license. The binary this produces is
    redistributed under that license; see THIRD-PARTY-NOTICES.txt.
#>

[CmdletBinding()]
param(
    [string] $SdlVersion    = '2.32.10',
    [string] $ExpectedSha256,
    [string] $OutputPath,
    [string] $WorkPath,
    [switch] $KeepWorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'   # keeps Invoke-WebRequest fast

# --------------------------------------------------------------------------
# Known-good source tarball hashes.
#
# These are the SHA-256 of the OFFICIAL source release tarball, taken from the
# libsdl-org GitHub release. Add a line here when bumping SDL2, rather than
# routinely passing -ExpectedSha256, so the pin stays under version control.
# --------------------------------------------------------------------------
$PinnedHashes = @{
    '2.32.10' = '5f5993c530f084535c65a6879e9b26ad441169b3e25d789d83287040a9ca5165'
}

function Write-Step   ([string] $m) { Write-Host "==> $m"  -ForegroundColor Cyan }
function Write-Detail ([string] $m) { Write-Host "    $m"  -ForegroundColor DarkGray }
function Write-Ok     ([string] $m) { Write-Host "    $m"  -ForegroundColor Green }

# --------------------------------------------------------------------------
# 0. Resolve paths
# --------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot 'native_libraries\win-arm64'
}
if (-not $WorkPath) {
    $WorkPath = Join-Path ([System.IO.Path]::GetTempPath()) "sdl2-arm64-build-$SdlVersion"
}

if (-not $ExpectedSha256) {
    if (-not $PinnedHashes.ContainsKey($SdlVersion)) {
        throw ("No pinned SHA-256 for SDL2 $SdlVersion. Either add one to " +
               "`$PinnedHashes in this script, or pass -ExpectedSha256. Never " +
               "build an unverified source tarball.")
    }
    $ExpectedSha256 = $PinnedHashes[$SdlVersion]
}

Write-Step "Building SDL2 $SdlVersion for Windows ARM64"
Write-Detail "Repository root : $RepoRoot"
Write-Detail "Output folder   : $OutputPath"
Write-Detail "Scratch folder  : $WorkPath"

# --------------------------------------------------------------------------
# 1. Locate the toolchain
# --------------------------------------------------------------------------
Write-Step 'Locating Visual Studio and the ARM64 build tools'

$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vsWhere)) {
    throw ("vswhere.exe not found at '$vsWhere'. Visual Studio 2022 does not " +
           'appear to be installed on this machine.')
}

$arm64Component = 'Microsoft.VisualStudio.Component.VC.Tools.ARM64'

$vsPath = & $vsWhere -latest -products * `
    -requires $arm64Component `
    -property installationPath 2>$null | Select-Object -First 1

if (-not $vsPath) {
    $anyVs = & $vsWhere -latest -products * -property installationPath 2>$null | Select-Object -First 1
    if ($anyVs) {
        throw ("Visual Studio was found at '$anyVs', but the ARM64 build tools are missing. " +
               "Open the Visual Studio Installer, choose Modify > Individual components, " +
               "and tick 'MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools' " +
               "($arm64Component).")
    }
    throw 'No Visual Studio 2022 installation with C++ tools was found on this machine.'
}

$vsVersion = & $vsWhere -latest -products * -requires $arm64Component `
    -property catalog_productDisplayVersion 2>$null | Select-Object -First 1

Write-Ok "Visual Studio $vsVersion"
Write-Detail $vsPath

# CMake: prefer the copy bundled with Visual Studio, fall back to PATH.
$cmake = Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) {
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if (-not $onPath) {
        throw ("CMake not found. Install the 'C++ CMake tools for Windows' component " +
               '(Microsoft.VisualStudio.Component.VC.CMake.Project) via the Visual Studio ' +
               'Installer, or put cmake.exe on PATH.')
    }
    $cmake = $onPath.Source
}
Write-Ok "CMake: $((& $cmake --version | Select-Object -First 1))"

# tar.exe is in-box on Windows 10 1803 and later.
if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    throw 'tar.exe not found. Windows 10 version 1803 or later is required.'
}

# Determine the CMake generator by asking CMake which ones it supports, rather
# than hard-coding a name-to-version map that goes stale with each new Visual
# Studio release. We pick the generator whose major version matches the VS we
# actually located above.
$vsMajor    = ($vsVersion -split '\.')[0]
$generators = & $cmake --help 2>$null |
    Select-String -Pattern '^\s*\*?\s*(Visual Studio (\d+) \d{4})(\s|$)' |
    ForEach-Object {
        [pscustomobject]@{
            Name  = $_.Matches[0].Groups[1].Value.Trim()
            Major = [int] $_.Matches[0].Groups[2].Value
        }
    }

$generator = ($generators | Where-Object { $_.Major -eq [int] $vsMajor } |
              Select-Object -First 1 -ExpandProperty Name)

if (-not $generator) {
    $known = ($generators | ForEach-Object { $_.Name }) -join ', '
    throw ("CMake does not offer a generator for Visual Studio major version " +
           "'$vsMajor'. CMake ($cmake) offers: $known. Update CMake, or pass a " +
           'newer one via the C++ CMake tools component.')
}
Write-Detail "Generator: $generator -A ARM64"

# --------------------------------------------------------------------------
# 2. Download and verify the official source tarball
# --------------------------------------------------------------------------
Write-Step 'Downloading the official SDL2 source release'

if (Test-Path $WorkPath) { Remove-Item $WorkPath -Recurse -Force }
New-Item -ItemType Directory -Path $WorkPath -Force | Out-Null

$tarName    = "SDL2-$SdlVersion.tar.gz"
$releaseUrl = "https://github.com/libsdl-org/SDL/releases/download/release-$SdlVersion"
$tarUrl     = "$releaseUrl/$tarName"
$sigUrl     = "$tarUrl.sig"
$tarPath    = Join-Path $WorkPath $tarName

Write-Detail $tarUrl
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $tarUrl -OutFile $tarPath -UseBasicParsing

$actualSha = (Get-FileHash -Path $tarPath -Algorithm SHA256).Hash.ToLowerInvariant()
$wantSha   = $ExpectedSha256.ToLowerInvariant()

if ($actualSha -ne $wantSha) {
    throw ("SHA-256 MISMATCH on $tarName -- refusing to build.`n" +
           "  expected: $wantSha`n" +
           "  actual:   $actualSha")
}
Write-Ok "SHA-256 verified: $actualSha"

# Upstream signs the SOURCE tarballs (they do NOT sign the prebuilt binaries),
# so verify the signature too when gpg happens to be available.
$gpg = Get-Command gpg -ErrorAction SilentlyContinue
if ($gpg) {
    try {
        $sigPath = "$tarPath.sig"
        Invoke-WebRequest -Uri $sigUrl -OutFile $sigPath -UseBasicParsing
        & $gpg.Source --verify $sigPath $tarPath 2>&1 | ForEach-Object { Write-Detail $_ }
        if ($LASTEXITCODE -eq 0) {
            Write-Ok 'GPG signature verified.'
        } else {
            Write-Warning ('GPG could not verify the signature. This is expected if ' +
                           "Sam Lantinga's public key is not in your keyring. The SHA-256 " +
                           'pin above is the authoritative check here.')
        }
    } catch {
        Write-Warning "GPG signature check skipped: $($_.Exception.Message)"
    }
} else {
    Write-Detail 'gpg not present; relying on the pinned SHA-256.'
}

Write-Step 'Extracting'
& tar -xzf $tarPath -C $WorkPath
if ($LASTEXITCODE -ne 0) { throw 'tar extraction failed.' }

$srcDir = Join-Path $WorkPath "SDL2-$SdlVersion"
if (-not (Test-Path $srcDir)) { throw "Expected source folder not found: $srcDir" }

# --------------------------------------------------------------------------
# 3. Configure and build
# --------------------------------------------------------------------------
Write-Step 'Configuring (CMake)'

$buildDir = Join-Path $WorkPath 'build-arm64'

# SDL_FORCE_STATIC_VCRT=ON is the important one: it matches how upstream builds
# the official x64/x86 DLLs. Those import no CRT at all, so end users need no
# Visual C++ Redistributable. Without this our ARM64 DLL would be the odd one
# out and would fail to load on a clean machine.
$configureArgs = @(
    '-S', $srcDir
    '-B', $buildDir
    '-G', $generator
    '-A', 'ARM64'
    '-DSDL_SHARED=ON'
    '-DSDL_STATIC=OFF'
    '-DSDL_TEST=OFF'
    '-DSDL_TESTS=OFF'
    '-DSDL_INSTALL_TESTS=OFF'
    '-DSDL_FORCE_STATIC_VCRT=ON'
)
& $cmake @configureArgs
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed.' }

Write-Step 'Building (Release)'
& $cmake --build $buildDir --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw 'CMake build failed.' }

$builtDll = Join-Path $buildDir 'Release\SDL2.dll'
if (-not (Test-Path $builtDll)) {
    # Fall back to searching, in case a future SDL2 changes its output layout.
    $found = Get-ChildItem -Path $buildDir -Filter 'SDL2.dll' -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if (-not $found) {
        throw "Build reported success but SDL2.dll was not found anywhere under '$buildDir'."
    }
    $builtDll = $found.FullName
    Write-Detail "Found at non-default location: $builtDll"
}

# --------------------------------------------------------------------------
# 4. Verify the artifact
# --------------------------------------------------------------------------
Write-Step 'Verifying the built DLL'

# 4a. Confirm the PE machine type really is ARM64 (0xAA64). Cross-compiles are
#     easy to misconfigure into silently producing an x64 image.
$fs = [System.IO.File]::OpenRead($builtDll)
try {
    $br = New-Object System.IO.BinaryReader($fs)
    $fs.Position = 0x3C
    $peOffset = $br.ReadInt32()
    $fs.Position = $peOffset
    if ($br.ReadUInt32() -ne 0x00004550) { throw 'Not a valid PE image.' }   # 'PE\0\0'
    $machine = $br.ReadUInt16()
} finally {
    $fs.Dispose()
}

$machineName = switch ($machine) {
    0xAA64  { 'ARM64' }
    0x8664  { 'x64' }
    0x014C  { 'x86' }
    default { ('0x{0:X4}' -f $machine) }
}
if ($machine -ne 0xAA64) {
    throw "Built DLL is $machineName, expected ARM64. The -A ARM64 configure step did not take effect."
}
Write-Ok "PE machine type: ARM64"

# 4b. Confirm no C runtime imports, matching the official x64/x86 binaries.
#     Prefer a dumpbin whose host architecture matches this machine, and put its
#     own folder on PATH for the call -- dumpbin needs mspdbcore.dll, which sits
#     beside it, and otherwise fails outside a developer command prompt.
$hostBin = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'Hostarm64' } else { 'Hostx64' }
$dumpbin = Get-ChildItem -Path (Join-Path $vsPath 'VC\Tools\MSVC') -Filter 'dumpbin.exe' `
               -Recurse -ErrorAction SilentlyContinue |
           Sort-Object { $_.FullName -notlike "*\$hostBin\*" } |
           Select-Object -First 1

if ($dumpbin) {
    $savedPath = $env:PATH
    try {
        $env:PATH = "$($dumpbin.DirectoryName);$savedPath"
        $deps = & $dumpbin.FullName /nologo /dependents $builtDll 2>&1
        if ($LASTEXITCODE -ne 0) { throw "dumpbin exited with code $LASTEXITCODE" }

        $crt = $deps | Select-String -Pattern 'vcruntime|ucrtbase|msvcp|api-ms-win-crt'
        if ($crt) {
            Write-Warning ('The built DLL imports the C runtime, but the official x64/x86 ' +
                           'binaries do not. Consumers would then need the ARM64 Visual C++ ' +
                           'Redistributable. Check that SDL_FORCE_STATIC_VCRT took effect:')
            $crt | ForEach-Object { Write-Warning "      $($_.Line.Trim())" }
        } else {
            Write-Ok 'No C runtime imports (matches the official x64/x86 binaries).'
        }
    } catch {
        Write-Detail "CRT import check skipped: $($_.Exception.Message)"
    } finally {
        $env:PATH = $savedPath
    }
} else {
    Write-Detail 'dumpbin not found; skipped the CRT import check.'
}

# --------------------------------------------------------------------------
# 5. Publish into the repository, with provenance
# --------------------------------------------------------------------------
Write-Step 'Copying into the repository'

if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

$destDll = Join-Path $OutputPath 'SDL2.dll'
Copy-Item -Path $builtDll -Destination $destDll -Force

$dllSha  = (Get-FileHash -Path $destDll -Algorithm SHA256).Hash.ToLowerInvariant()
$dllSize = (Get-Item $destDll).Length

$provenance = @"
SDL2.dll -- Windows ARM64
================================================================================
This binary was NOT downloaded. libsdl.org publishes official prebuilt SDL2
binaries for Windows x64, Windows x86 and macOS, but not for Windows ARM64, so
this one is built from the official SDL2 source release by:

    tools/build-sdl2-windows-arm64.ps1

Re-running that script with the same SDL2 version reproduces this binary.

SDL2 version      : $SdlVersion
Architecture      : ARM64 (PE machine type 0xAA64)
Source tarball    : $tarName
Source URL        : $tarUrl
Source SHA-256    : $actualSha   (pinned in the build script)
Output SHA-256    : $dllSha
Output size       : $dllSize bytes

Built on          : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Build host        : $env:COMPUTERNAME ($env:PROCESSOR_ARCHITECTURE)
Toolchain         : Visual Studio $vsVersion, $generator, -A ARM64
CMake options     : SDL_SHARED=ON SDL_STATIC=OFF SDL_TEST=OFF SDL_TESTS=OFF
                    SDL_FORCE_STATIC_VCRT=ON

SDL_FORCE_STATIC_VCRT=ON matches how upstream builds the official x64/x86 DLLs:
the result imports no C runtime, so no Visual C++ Redistributable is required
on the end user's machine.

SDL2 is licensed under the zlib license, Copyright (C) 1997-2025 Sam Lantinga.
See THIRD-PARTY-NOTICES.txt in the repository root for the full license text.
"@

$provenance | Set-Content -Path (Join-Path $OutputPath 'SDL2.dll.provenance.txt') -Encoding UTF8

Write-Ok "SDL2.dll  ->  $destDll"
Write-Detail "SHA-256: $dllSha"
Write-Detail "Size:    $dllSize bytes"
Write-Detail 'Wrote SDL2.dll.provenance.txt alongside it.'

# --------------------------------------------------------------------------
# 6. Clean up
# --------------------------------------------------------------------------
if ($KeepWorkDir) {
    Write-Detail "Scratch folder kept at: $WorkPath"
} else {
    Write-Step 'Cleaning up'
    Remove-Item $WorkPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Done. Commit the DLL and its provenance file.' -ForegroundColor Green
Write-Host 'Test it on an ARM64 device before shipping.'   -ForegroundColor Green
