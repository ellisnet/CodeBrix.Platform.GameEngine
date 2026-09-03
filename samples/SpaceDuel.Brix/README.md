# SpaceDuel.Brix

A top-down spaceship duel: the player's ship against three AI raiders on a 90 x 54 wrap-around
world, drawn on the engine's GPU render tier.

Destroy all three raiders before they destroy the player's ship. Ships and laser fire wrap across
every world edge.

## Play

| Key | Action |
| --- | --- |
| `A` / `D`, or Left / Right | rotate |
| `W`, or Up | thrust |
| `Space` | fire |
| `R` | restart |

The game surface takes keyboard focus as soon as it loads, and again whenever it is clicked.

## Run it

```
dotnet run --project src/SpaceDuel.Brix.LinuxX11 -c Debug     # Linux (X11)
dotnet run --project src/SpaceDuel.Brix.MacOS    -c Debug     # macOS
dotnet run --project src/SpaceDuel.Brix.Win32Skia -c Debug    # Windows (Win32/Skia)
```

Set `SPACEDUEL_USE_CPU=1` to run the identical game on the CPU render tier instead, for a
side-by-side comparison of the two tiers. The console reports which tier is live, and the on-screen
overlay reports cycles per second and the frame rate (`GpuFps` on the GPU tier, `NetCPS` on the CPU
tier).

## What the sample exercises

- centre-anchored `Sprite.Rotation` on ships and laser bolts, on the GPU backbuffer
- `MovementController.WrapX` / `WrapY` wrapping in both axes
- two code-built star layers at different parallax factors (0.16 and 0.42)
- runtime bitmap tilesheets: an embedded ship sprite sheet plus a procedurally painted effects sheet
- integrated acceleration, maximum speed, and frame-rate-independent coasting drag
- simple steering and firing behaviour for three enemy ships
- sprite-based laser projectiles and axis-aligned hit tests
- the engine's `HealthBar` component, one per ship, following its sprite in world space
- the engine's `SplashOverlay` component with a procedurally drawn title card
- particle explosions through `ParticleSurface.Burst`
- camera follow, view-space HUD drawing, and keyboard input through the engine's poller

Sprite rotation is visual. Collision bounds stay axis-aligned: the duel resolves ship-versus-ship
and laser-versus-ship hits itself with rectangle tests against `Sprite.CollisionArea`, and registers
no collider with the engine's collision system. The `CollisionProfileNames.Actor` and
`CollisionProfileNames.Projectile` profiles assigned to the sprites therefore only declare each
sprite's role; the ship and laser `CollisionAdjust` insets are what actually shape the hit boxes.

## How it is hosted

`MainViewModel.CanvasFirstStart` sets `GameSurfaceCanvas.UseGpuRendering` **before** the first
access to `canvas.Host` — the render tier cannot change once the scene pipeline exists — and then
constructs and initializes `SpaceDuelGameHost`. The render resolution tracks the window, so there is
no `SetRenderResolution` call and the sample resizes freely.

`SpaceDuelGameHost` subscribes to `Engine.InitializationComplete` from `OnInitializing`, and there
sets `TargetFPS = 0` (unthrottled), `VSync = false` and `MsaaSampleCount = 4`. The splash is created
in `OnInitialized`, and its completion callback starts the duel.

## Layout

```
SpaceDuel.Brix.slnx
src/libs/SpaceDuel.Brix.Game/    the game: host, procedural art, ship/projectile state
src/SpaceDuel.Brix.Core/         app services and the view model that owns the game host
src/SpaceDuel.Brix.UI/           shared App.xaml and Views/MainPage.xaml
src/SpaceDuel.Brix.LinuxX11/     platform heads - exactly one head package each
src/SpaceDuel.Brix.MacOS/
src/SpaceDuel.Brix.Win32Skia/
```

## Provenance

The game rules, tuning constants and art pipeline are ported from the upstream engine's
space-duel demo, which hosted the same game on Windows Forms with a GPU render surface. The
port keeps the upstream constants unchanged and adapts the hosting layer only: the Windows Forms GPU
host becomes `CodeBrixGameHost` over a `GameSurfaceCanvas` with `UseGpuRendering = true`, Windows
Forms `Keys` become `Windows.System.VirtualKey`, the upstream widget-library health bar and splash
screen become the engine's own `HealthBar` and `SplashOverlay` components, and the splash art is
drawn in code rather than loaded from a logo file.

### Ship artwork

The upstream demo ships a `ships.png` extracted from a reference image that identifies Freepik as its
provider, with redistribution rights left unconfirmed. That file is deliberately NOT included here. The
game draws its ship sheet procedurally (`SpaceDuelArt.LoadShipBitmap()`), which is the only art path
the sample uses. If properly licensed ship art becomes available, embed it as
`SpaceDuel.Brix.Game.Assets.ships.png` (an `EmbeddedResource` with that `LogicalName`) and the loader
will pick it up in preference to the procedural sheet.