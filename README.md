# CodeBrix.Platform.GameEngine

A fully managed, cross-platform 2D and 2.5D game engine for .NET. CodeBrix.Platform.GameEngine provides tile maps, sprites, layered scenes, camera/view systems, animation, physics and collision, input handling, audio, and SkiaSharp-based rendering — designed for tile-based worlds and a practical engine architecture.

This repository ships **two** core libraries, plus one optional add-on:

* **`CodeBrix.Platform.GameEngine`** — the platform-agnostic engine core. It has no UI-framework dependency and is headless-testable; its rendering seam is SkiaSharp `SKImage` plus a render-surface-adapter abstraction.
* **`CodeBrix.Platform.GameEngine.Host`** — the host layer that runs the engine on **CodeBrix.Platform**, across all six heads (Windows Win32-Skia, Windows WPF-Skia, Linux X11, Linux Wayland, Linux Frame Buffer, macOS). It provides the CPU and GPU render-surface adapters, pointer/keyboard input adapters, and a UI dispatcher.
* **`CodeBrix.Platform.GameEngine.Sdl2`** — *optional* game controller (gamepad) support; see below.

CodeBrix.Platform.GameEngine is provided as .NET 10 libraries and two NuGet packages: `CodeBrix.Platform.GameEngine.MitLicenseForever`, which bundles both the engine-core (`CodeBrix.Platform.GameEngine.dll`) and host (`CodeBrix.Platform.GameEngine.Host.dll`) assemblies, and the optional `CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever` for gamepads.

CodeBrix.Platform.GameEngine supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Platform.GameEngine.MitLicenseForever
```

```
dotnet add package CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever
```

Note that the NuGet package IDs and the namespaces are different - there is no package named plain `CodeBrix.Platform.GameEngine`:

* NuGet package ID: `CodeBrix.Platform.GameEngine.MitLicenseForever`
  * Assemblies and primary namespaces: `CodeBrix.Platform.GameEngine` and `CodeBrix.Platform.GameEngine.Host` - i.e. `using CodeBrix.Platform.GameEngine;`
  * One reference gives you both assemblies; there is no separate `.Host` package.
* NuGet package ID: `CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever`
  * Assembly and primary namespace: `CodeBrix.Platform.GameEngine.Sdl2` - i.e. `using CodeBrix.Platform.GameEngine.Sdl2;`

**Which one do I reference?** Every game references `CodeBrix.Platform.GameEngine.MitLicenseForever`. Add `CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever` only when you want game controller (gamepad) support - it is a separate package precisely so that games which do not want a native SDL2 dependency do not inherit one.

XML documentation (IntelliSense) ships alongside the assemblies.

The engine package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.Platform.ApacheLicenseForever` - the UI platform
* `CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever` - the XAML canvas the engine renders into
* `CodeBrix.Platform.Graphics3DGL.ApacheLicenseForever` - the GPU render path
* `CodeBrix.Platform.Svg.ApacheLicenseForever` and `CodeBrix.SkiaSvg.MitLicenseForever` - SVG drawing
* `SkiaSharp` - the rendering engine
* `CodeBrix.Audio.MitLicenseForever` - device audio I/O
* `CodeBrix.Compression.MitLicenseForever` and `CodeBrix.Json.Extensions.MitLicenseForever` - save/load
* `Microsoft.Extensions.Configuration` (plus `.Binder` and `.Json`) and `Microsoft.Extensions.Logging.Console` / `.Debug`

Your game is a CodeBrix.Platform application, so each executable project also adds exactly one CodeBrix.Platform head package - for example `CodeBrix.Platform.Runtime.Skia.X11.ApacheLicenseForever` for Linux X11 - and that head supplies the SkiaSharp native libraries. Add `CodeBrix.Audio.Opus.BsdLicenseForever` if you want `.opus` audio assets.

## CodeBrix.Platform.GameEngine supports:

* Tile maps, tilesheets, and layered scenes with camera/view systems — seven tile geometries (orthogonal, two isometric, two hex, oblique-right and oblique-left)
* Sprites, composite sprites, sprite rotation, and frame-based animation cycles
* Direct drawing primitives (images, rectangles with pattern and image fills, SVG, text, particles, image-instance layers)
* Radial lights and darkness/fog overlays that lights carve holes in
* Display effects over a whole view or layer: fades, wipes, slides, zooms and an earthquake shake
* Ready-made components: a self-disposing splash overlay and a sprite-tracking health bar
* Physics: movement, easing, scripted motion, and collision detection — with named collision profiles, per-tile and per-animation-frame collision shapes and types, all authorable in a `.gts` tilesheet definition
* Input: keyboard, mouse, gamepad, and touch (with tap, swipe and pinch gestures)
* Audio playback and mixing (via CodeBrix.Audio): master/music/sfx volume buses, a preload-to-PCM sound-effect voice pool, and support for WAV, MP3, Ogg Vorbis and FLAC out of the box (plus any other format registered with CodeBrix.Audio, such as Opus)
* MIDI music rendered live through a sampled instrument — SoundFont, SFZ and Decent Sampler instruments — with per-channel layering, a tempo control that does not change pitch, and MPE
* A music system: fades and equal-power crossfades, reference-counted ducking, stingers, playlists, layered adaptive stems (including the stems of a Suno download, loaded straight from the zip or folder), and transitions quantised to the next beat or bar — exactly, through the tempo map, even where the music changes tempo
* Save/load of engine state as JSON (via System.Text.Json + CodeBrix.Json.Extensions), including shared-reference object graphs
* A global pause that parks the whole engine at near-zero CPU and shifts every time baseline on resume, so nothing bursts or teleports
* A UI-agnostic core with a render-surface-adapter seam for headless unit testing

## Samples

Nine complete games and demos live under `samples/`, each with Linux X11, Windows Win32-Skia and macOS heads and its own `.slnx`:

* `Spot.Brix` — the recommended hosting shape end to end: splash overlay, a XAML New Game dialog driving the engine, option persistence and save-on-game-over
* `Platformer.Brix` — a side-view platform game: tile colliders, collision profiles, `CollisionAdjust` insets, gravity and jumping, camera follow, and a pinned 960x576 letterboxed render resolution
* `SpaceDuel.Brix` — a GPU-tier space duel: rotated sprites, a wrap-around world, parallax star layers, particle explosions, health bars and a splash
* `Slider`, `CoordinateTest`, `ParticleTest`, `SoftRender`, `GpuRender`, `MusicDemo` — focused references for the engine-direct hosting path, coordinate systems, particles, the software-rendered (Mode B) path, GPU rendering and the music system

See `EXTRAS-README.txt` for what each one demonstrates.

## Sample Code

### Creating and configuring the engine

```csharp
using System;
using System.IO;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Configuration;

// Load engine configuration. The settings live under an "EngineConfig" root key,
// and the default file name is relative to the process working directory — pass
// an absolute path for a predictable location.
var configPath = Path.Combine(AppContext.BaseDirectory, "gameengine.json");
var config = EngineConfigurationFile.Load(configPath, autoSave: true);

// The engine is created and driven by the host layer
// (CodeBrix.Platform.GameEngine.Host) on a CodeBrix.Platform surface.
```

## Gamepad support (optional)

Game controller support ships as a **separate** NuGet package, so that games which do not want a native SDL2 dependency do not inherit one:

```
dotnet add package CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever
```

One call attaches it to the engine, which then polls it every frame:

```csharp
using CodeBrix.Platform.GameEngine.Sdl2;

var gamepads = Engine.Instance.InitializeSdlGamepadManager();
```

It works on all six heads — including Frame Buffer — because SDL2 is initialized as a headless joystick backend with no video subsystem. It never throws when SDL2 or a controller is missing; ask `gamepads.IsAvailable` and `gamepads.UnavailableReason` instead.

The package carries the SDL2 native binaries for Windows and macOS. On Linux it uses the system SDL2, which is its one prerequisite: `sudo apt install libsdl2-2.0-0`.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

The gamepad package carries its own `AGENT-README.txt`, covering the controller API and the gotchas worth knowing before wiring a game to it; point your agent at that file as well when the game uses gamepads.

Additional sample code and usage examples are available in the `CodeBrix.Platform.GameEngine.Tests` project:
https://github.com/ellisnet/CodeBrix.Platform.GameEngine/tree/main/tests/CodeBrix.Platform.GameEngine.Tests

## License

CodeBrix.Platform.GameEngine is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Platform.GameEngine/blob/main/LICENSE) file.

The optional `CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever` package is licensed `MIT AND Zlib`, because it redistributes the SDL2 native libraries.

For licensing and provenance information about the open source code included in
these packages, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Platform.GameEngine/blob/main/THIRD-PARTY-NOTICES.txt).
