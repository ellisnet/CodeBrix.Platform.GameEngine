================================================================================
AGENT-README: CodeBrix.Platform.GameEngine
A Comprehensive Guide for AI Coding Agents
================================================================================

OVERVIEW
--------------------------------------------------------------------------------
CodeBrix.Platform.GameEngine is a fully managed, cross-platform 2D / 2.5D game
engine for .NET, built on SkiaSharp. It provides tile maps, tilesheets, sprites,
layered scenes, camera/view systems, animation, physics/collision, input, audio,
and a save/load system.

The repository contains TWO libraries that mirror the classic core/host split:

  * CodeBrix.Platform.GameEngine        -- the platform-agnostic engine CORE.
        No UI-framework dependency; headless-testable. Its rendering seam is a
        SkiaSharp SKImage plus the RenderSurfaceAdapterBase abstraction.

  * CodeBrix.Platform.GameEngine.Host   -- the HOST layer that runs the engine
        on CodeBrix.Platform (all six heads: Win32-Skia, WPF-Skia, X11, Wayland,
        Frame Buffer, macOS). Contains the CPU (Tier A) and GPU (Tier B) render-
        surface adapters, pointer/keyboard input adapters, and a UI dispatcher.

The engine core is a vendored port of the open-source Gondwana game engine 
version 2.5.0 (MIT, (c) 2025 Michael Adkins). See THIRD-PARTY-NOTICES.txt for 
more info.

INSTALLATION
--------------------------------------------------------------------------------
NuGet package IDs (note the license suffix):

    CodeBrix.Platform.GameEngine.MitLicenseForever
    CodeBrix.Platform.GameEngine.Host.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.MitLicenseForever
    dotnet add package CodeBrix.Platform.GameEngine.Host.MitLicenseForever

The namespaces are CodeBrix.Platform.GameEngine[.*] and
CodeBrix.Platform.GameEngine.Host[.*] (WITHOUT the license suffix).

Target framework: .NET 10.0 or higher.

KEY NAMESPACES
--------------------------------------------------------------------------------
    using CodeBrix.Platform.GameEngine;                 // Engine, EngineState, dispatchers
    using CodeBrix.Platform.GameEngine.Assets;          // AssetsFile
    using CodeBrix.Platform.GameEngine.Configuration;   // EngineConfiguration[File]
    using CodeBrix.Platform.GameEngine.Drawing;         // Tile, ImageFilterQuality, SvgResource
    using CodeBrix.Platform.GameEngine.Drawing.Sprites; // Sprite, CompositeSprite
    using CodeBrix.Platform.GameEngine.Drawing.Direct;  // DirectImage, DirectText, TextBlock, ...
    using CodeBrix.Platform.GameEngine.Rendering;       // render-surface hosts + backbuffers
    using CodeBrix.Platform.GameEngine.Scenes;          // Scene, SceneLayer, SceneLayerTile
    using CodeBrix.Platform.GameEngine.Physics;         // movement, easing, collisions
    using CodeBrix.Platform.GameEngine.Input;           // keyboard/mouse/gamepad/touch

CORE API REFERENCE (high level)
--------------------------------------------------------------------------------
    Engine                 -- top-level engine object; owns managers, dispatch, state.
    EngineState            -- serializable engine state (scenes, sprites, cycles, assets).
    Scene / SceneLayer     -- layered scene graph; SceneLayerTile places tiles on a layer.
    Tile / Sprite          -- drawable tiles and sprites; Sprite : Tile.
    Tilesheet / GTS        -- tilesheet definitions loaded from ".gts" files.
    DirectImage / DirectText / TextBlock / DirectRectangle / DirectSvg
                           -- immediate-mode drawables.
    ImageFilterQuality     -- engine sampling-quality enum (None/Low/Medium/High);
                              maps to SkiaSharp SKSamplingOptions (SkiaSharp 4 replacement
                              for the removed SKFilterQuality).
    AssetsFile             -- zip-backed asset container (via CodeBrix.Compression).
    EngineConfigurationFile-- loads/saves engine configuration (default "gameengine.json").

CODING CONVENTIONS (CodeBrix family)
--------------------------------------------------------------------------------
  * Target net10.0 only; never multi-target.
  * File-scoped namespaces; usings at the top (System.* first), never global usings.
  * XML doc comments on public/protected members (GenerateDocumentationFile=true).
  * xUnit v3 + SilverAssertions for tests; coverlet.collector for coverage.
  * No project-wide warning suppression except the documented port exceptions below.

  PORT EXCEPTIONS (this repository):
  * Nullable reference types are ENABLED (<Nullable>enable</Nullable>) on both
    libraries. The upstream source relies on "?" annotations throughout;
    stripping them would change observable public signatures and reduce fidelity.
    This is the same sanctioned exception used by CodeBrix.Platform.OpenGL.
    Because NRT is on, "?" on reference types and the "!" null-forgiveness
    operator are permitted in this repository (unlike the family default).

ARCHITECTURE
--------------------------------------------------------------------------------
  CodeBrix.Platform.GameEngine (core)
    deps: SkiaSharp, CodeBrix.SkiaSvg, CodeBrix.Compression, CodeBrix.Audio,
          System.Text.Json + CodeBrix.Json.Extensions, Microsoft.Extensions.*
    No CodeBrix.Platform UI dependency. Rendering seam = SKImage + adapter base.

  CodeBrix.Platform.GameEngine.Host
    refs: CodeBrix.Platform.GameEngine
    deps: CodeBrix.Platform, CodeBrix.Platform.SkiaSharp.Views,
          CodeBrix.Platform.Graphics3DGL (GPU path), SkiaSharp,
          CodeBrix.Platform.Svg (platform-integrated SVG)
    Tier A = CPU BitmapBackbuffer adapter (default, all heads).
    Tier B = GPU adapter via Graphics3DGL offscreen GL + readback.

  Source is grouped into sub-folders that mirror the sub-namespaces
  (Drawing, Rendering, Scenes, Physics, Input, Audio, Assets, ...).

SOFTWARE-RENDERED (FRAMEBUFFER) GAMES
--------------------------------------------------------------------------------
  A parallel enablement path for games that render whole CPU frames at a fixed
  tic rate (35/70 Hz) and never touch the scene/sprite pipeline. All surfaces
  are original, generic engine code.

  PRESENTATION (core CodeBrix.Platform.GameEngine.Rendering + Host):
    PixelFramePresenter      -- Configure(width, height, PixelBufferFormat
                                {Rgba8888,Bgra8888}, FrameOrientation
                                {Identity,Rotate90}, PixelFrameScaleMode
                                {Fit,Stretch,PixelPerfect,Center},
                                ImageFilterQuality); PresentFrame(bytes/uints)
                                from ANY thread, once per tic: one full-frame
                                copy, zero per-frame managed allocations,
                                latest-frame-wins triple buffering. Rotate90
                                shows column-major buffers with NO CPU
                                transpose. WindowToBuffer/BufferToWindow map
                                pointer coordinates across the letterbox.
    GameSurfaceCanvas.UsePixelFramePresenter()
                             -- enters presenter mode WITHOUT creating the
                                scene/backbuffer pipeline. Presenter mode and
                                .Host are mutually exclusive per canvas (the
                                other one throws).

  AUDIO (core CodeBrix.Platform.GameEngine.Audio; CodeBrix.Audio unmodified):
    AudioSystem.Initialize(44100, 2)
                             -- OPT-IN device-rate pinning; never automatic.
                                Once pinned, odd-rate sources are rate-converted
                                (AudioResource inserts the converter itself).
    AudioResourceManager.LoadFromPcm(key, data, rate, bits {8u,16s}, channels)
                             -- headerless raw-PCM lumps, no container needed.
    SoundChannel             -- fixed channel: SetClip(key) (swap constantly),
                                Play(volume, pan, pitch), live Volume/Pan/Pitch,
                                State (NOTE: Stopped detection lags ~25 ms via
                                the shared output's sweep timer), approximate
                                Position/Duration. Requires AudioSystem.Initialize.
    VariableRateSampleProvider -- linear-interpolation rate converter with a
                                live pitch multiplier (0.05-20).
    StreamingAudioSource     -- endless pull-model stream (synth music, emulated
                                sound chips): FillAudioBuffer(Span<float>)
                                callback or ISampleProvider, pulled on the AUDIO
                                CALLBACK THREAD (fast, allocation-free, never
                                block); Start/Stop + Volume.

  INPUT (core CodeBrix.Platform.GameEngine.Input + Host):
    InputPump.PollNow()      -- public, thread-safe pump for game-owned loops;
                                THROWS while the engine loop runs (mutually
                                exclusive; double-pumping corrupts poller state).
    KeyDownEventArgs.KeyCode -- numeric key code on every poller event.
    KeyboardEventPoller.StartMonitoringKeys(IEnumerable<int>) /
    StartMonitoringAllKeys() -- bulk registration (whole binding sets / any-key).
    IKeyboardAdapter.IsDown(int)
                             -- CONTRACT: lock-free, any thread, per-tic polled
                                gameplay path.
    GameSurfaceCanvas.EnsureFocus()
                             -- the IsTabStop + focus-on-load + refocus-on-press
                                recipe, one call.
    RelativeMouseSession     -- FPS mouse look over MouseDevice.MouseMoved:
                                Begin() (hide cursor + confine + accumulate),
                                per-tic ConsumeDelta() -> (dx, dy), End().
                                Inactive (logged) on platform versions without
                                relative mouse support.

  GAME LOOP (core CodeBrix.Platform.GameEngine.Timers + Host):
    FixedRateGameLoop        -- dedicated-thread fixed-Hz callback host:
                                non-drifting timestep, bounded catch-up
                                (MaxCatchUpTics, dropped tics counted),
                                Pause/Resume without a burst, sleep+yield hybrid
                                pacing (no busy loop), ActualTicsPerSecond stats.
    SoftwareRenderedGameHostBase
                             -- the GameHostBase sibling for these games: wires
                                canvas presenter + input pump + fixed-rate loop;
                                virtuals OnLoadContent (must Configure the
                                presenter), OnTic, OnRenderFrame(Span<byte>),
                                OnShutdown, ConfigureAudio (opt-in), and
                                ConfigureInput.

  The samples/SoftRender demo exercises this whole stack (plasma + starfield at
  320x200/70 Hz, raw-PCM blips with random pitch, a streamed drone) and is the
  reference consumer for these surfaces. These surfaces are EXPERIMENTAL until
  the first two consuming games ship, then frozen.

TESTING
--------------------------------------------------------------------------------
  Core tests are headless unit tests (the UI-agnostic core makes this clean),
  including round-trip save/load tests. Host tests cover what can run without a
  live UI head; head-dependent behavior is env-gated or skipped with a reason.

    dotnet test CodeBrix.Platform.GameEngine.slnx

================================================================================
END OF AGENT-README
================================================================================
