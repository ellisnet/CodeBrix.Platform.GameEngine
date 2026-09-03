# Platformer.Brix

A small, self-contained platform game built directly on CodeBrix.Platform.GameEngine's public
APIs, hosted on a CodeBrix.Platform surface.

## Provenance

This sample is a port of the self-contained platformer demo from the upstream engine project that
CodeBrix.Platform.GameEngine was derived from. The upstream demo was hosted in a fixed-size
WinForms window; the level layout, art, physics constants, collision adjustments, HUD text and
game rules here are the upstream ones. What changed in the port:

* `WinFormsGameHost` became `CodeBrixGameHost`, and the fixed 960x576 window became a
  `GameSurfaceCanvas` with `SetRenderResolution(960, 576)` — the engine always renders 960x576
  and the surface letterboxes it into whatever size the window happens to be.
* `System.Windows.Forms.Keys` became `Windows.System.VirtualKey`, and the key handler reads
  `KeyDownEventArgs.KeyCode` instead of parsing the key's display string.
* `SKFilterQuality.None` became `ImageFilterQuality.None` (SkiaSharp 4.x); the engine's
  `BitmapBackbuffer` already defaults to it, so the assignment is only there to say so out loud.
* `SKPath` mutation in the procedural art became `SKPathBuilder` + `Snapshot()`.
* Colliders are configured through the engine's collision-profile API rather than by writing
  collider group/mask fields directly — see below.
* Esc was handled by the WinForms window; here the game host raises `ExitRequested`, the view
  model forwards it, and the page closes the application window.

## Play

* `A` / `D` or Left / Right: move
* `W`, Up, or Space: jump
* `R`: restart
* `Esc`: quit

Collect all five sun relics, then reach the red flag. Falling into a pit or touching spikes
returns the player to the start; collected relics stay collected until the level is restarted.

## Running it

```
dotnet run --project samples/Platformer.Brix/src/Platformer.Brix.LinuxX11/Platformer.Brix.LinuxX11.csproj
```

Swap the head project for `Platformer.Brix.Win32Skia` or `Platformer.Brix.MacOS` on the other
platforms. The sample carries its own `Platformer.Brix.slnx`; like every other sample it is not
part of the repository's product solution.

## What the sample exercises

* a code-built `Scene` with a parallax background layer (0.35) and a world layer
* bitmap-backed runtime tilesheets — `Tilesheets.LoadFromBitmap` over a bitmap painted in code by
  `src/libs/Platformer.Brix.Game/Art/PlatformerArt.cs`, so the sample has no image assets and no
  external licensing to track
* fixed layer-tile colliders (`SceneLayerTile.Collider` through `SetCollisionProfile` and
  `CollisionType`) alongside a dynamic sprite collider
* `CollisionAdjust` in its inset form: positive values on any edge shrink the collision box. The
  player is inset 4/0/7/7 (top/bottom/left/right), the spikes 14/1/3/3 so only their points bite,
  and each relic 5/5/5/5 so it has to be properly touched
* collision profiles: the world layer keeps the standard `World` profile, and the game defines a
  `Player` profile (the `Actors` group, colliding with `WorldStatic`) in `CreateInitialScene`
* integrated sprite velocity and acceleration for running, gravity and jumping, plus a foot probe
  through `ColliderRegistry.QueryAabb` to decide when the player is grounded
* horizontal camera follow with a dead zone and world-bound clamping
* view-bound `DirectRectangle` and `TextBlock` HUD elements
* keyboard input through the engine's keyboard poller — which only sees keys while the game
  surface holds keyboard focus, so `Views/MainPage.xaml.cs` focuses the canvas as soon as the
  engine starts

## Layout

```
Platformer.Brix.slnx
src/libs/Platformer.Brix.Game/    the game: PlatformerGameHost + Art/PlatformerArt
src/Platformer.Brix.Core/         view model and host-builder helper; owns the engine reference
src/Platformer.Brix.UI/           shared App.xaml and Views/MainPage.xaml
src/Platformer.Brix.LinuxX11/     executable heads — one platform package each
src/Platformer.Brix.MacOS/
src/Platformer.Brix.Win32Skia/
```
