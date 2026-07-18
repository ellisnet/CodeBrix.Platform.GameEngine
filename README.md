# CodeBrix.Platform.GameEngine

A fully managed, cross-platform 2D and 2.5D game engine for .NET. CodeBrix.Platform.GameEngine provides tile maps, sprites, layered scenes, camera/view systems, animation, physics and collision, input handling, audio, and SkiaSharp-based rendering — designed for tile-based worlds and a practical engine architecture.

This repository ships **two** libraries:

* **`CodeBrix.Platform.GameEngine`** — the platform-agnostic engine core. It has no UI-framework dependency and is headless-testable; its rendering seam is SkiaSharp `SKImage` plus a render-surface-adapter abstraction.
* **`CodeBrix.Platform.GameEngine.Host`** — the host layer that runs the engine on **CodeBrix.Platform**, across all six heads (Windows Win32-Skia, Windows WPF-Skia, Linux X11, Linux Wayland, Linux Frame Buffer, macOS). It provides the CPU and GPU render-surface adapters, pointer/keyboard input adapters, and a UI dispatcher.

CodeBrix.Platform.GameEngine is provided as .NET 10 libraries, shipped as a single `CodeBrix.Platform.GameEngine.MitLicenseForever` NuGet package that bundles both the engine-core (`CodeBrix.Platform.GameEngine.dll`) and host (`CodeBrix.Platform.GameEngine.Host.dll`) assemblies.

CodeBrix.Platform.GameEngine supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## CodeBrix.Platform.GameEngine supports:

* Tile maps, tilesheets, and layered scenes with camera/view systems
* Sprites, composite sprites, and frame-based animation cycles
* Direct drawing primitives (images, rectangles, SVG, text, particles)
* Physics: movement, easing, scripted motion, and collision detection
* Input: keyboard, mouse, gamepad, and touch (with gestures)
* Audio playback and mixing (via CodeBrix.Audio)
* Save/load of engine state as JSON (via System.Text.Json + CodeBrix.Json.Extensions), including shared-reference and by-id object graphs
* A UI-agnostic core with a render-surface-adapter seam for headless unit testing

## Sample Code

### Creating and configuring the engine

```csharp
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Configuration;

// Load engine configuration (defaults to "gameengine.json" if present).
var config = EngineConfigurationFile.Load();

// The engine is created and driven by the host layer
// (CodeBrix.Platform.GameEngine.Host) on a CodeBrix.Platform surface.
```

## License

The project is licensed under the MIT License. see: https://en.wikipedia.org/wiki/MIT_License

The engine core is a port of the open-source Gondwana game engine (MIT); see `THIRD-PARTY-NOTICES.txt` for full attribution.
