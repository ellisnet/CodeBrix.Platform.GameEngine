# CodeBrix.Platform.GameEngine

A fully managed, cross-platform 2D and 2.5D game engine for .NET. CodeBrix.Platform.GameEngine provides tile maps, sprites, layered scenes, camera/view systems, animation, physics and collision, input handling, audio, and SkiaSharp-based rendering — designed for tile-based worlds and a practical engine architecture.

This repository ships **two** core libraries, plus one optional add-on:

* **`CodeBrix.Platform.GameEngine`** — the platform-agnostic engine core. It has no UI-framework dependency and is headless-testable; its rendering seam is SkiaSharp `SKImage` plus a render-surface-adapter abstraction.
* **`CodeBrix.Platform.GameEngine.Host`** — the host layer that runs the engine on **CodeBrix.Platform**, across all six heads (Windows Win32-Skia, Windows WPF-Skia, Linux X11, Linux Wayland, Linux Frame Buffer, macOS). It provides the CPU and GPU render-surface adapters, pointer/keyboard input adapters, and a UI dispatcher.
* **`CodeBrix.Platform.GameEngine.Sdl2`** — *optional* game controller (gamepad) support; see below.

CodeBrix.Platform.GameEngine is provided as .NET 10 libraries, shipped as a single `CodeBrix.Platform.GameEngine.MitLicenseForever` NuGet package that bundles both the engine-core (`CodeBrix.Platform.GameEngine.dll`) and host (`CodeBrix.Platform.GameEngine.Host.dll`) assemblies.

CodeBrix.Platform.GameEngine supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

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
* A music system: fades and equal-power crossfades, reference-counted ducking, stingers, playlists, layered adaptive stems, and transitions quantised to the next beat or bar
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

See `AGENT-README.txt` for the full documentation, including the gotchas worth knowing before wiring a game to it.

## License

The project is licensed under the MIT License. see: https://en.wikipedia.org/wiki/MIT_License

The engine core is a port of an open-source, MIT-licensed game engine; see `THIRD-PARTY-NOTICES.txt` for full attribution, including the upstream project and the exact revision this port tracks.

The optional `CodeBrix.Platform.GameEngine.Sdl2` package is licensed `MIT AND Zlib`: its managed SDL2 bindings are adapted from Veldrid (MIT), and it redistributes the SDL2 native libraries (zlib). Both are attributed in full in `THIRD-PARTY-NOTICES.txt`.
