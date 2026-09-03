# Release notes — 2026-09-03

`CodeBrix.Platform.GameEngine.MitLicenseForever` (engine core + CodeBrix.Platform host layer).

This release merges the upstream engine's fixes and enhancements published since the port's
original vendoring, adds three new subsystems (display effects, direct lighting, collision
profiles), adds two ready-made components and two new samples, and fixes a long list of defects.
`THIRD-PARTY-NOTICES.txt` records the upstream revision the core now tracks.

The engine builds with 0 warnings and 0 errors; the test suites stand at 456 engine-core tests,
45 gamepad tests and 2 host tests.

---

## BREAKING CHANGES

Read this section before upgrading. Everything here either fails to compile or silently changes
what a game does.

### 1. `CollisionDetectionAdjustment` is now `CollisionAdjust`, and positive values INSET every edge

The type moved and its sign convention changed.

* `CodeBrix.Platform.GameEngine.Physics.Collisions.CollisionDetectionAdjustment` →
  `CodeBrix.Platform.GameEngine.Physics.Collisions.CollisionAdjust`. `Tile.AdjustCollisionArea`
  changes type accordingly; `CollisionDetectionAdjustment.None` → `CollisionAdjust.None`.
* **The sign convention changed on two of the four edges.** A positive value on ANY edge now moves
  that edge INWARD and shrinks the collision box; a negative value moves it outward. Previously a
  positive `Bottom` or `Right` pushed the far edge OUTWARD. Concretely, the collision rectangle is
  now `Rectangle.FromLTRB(L + Left, T + Top, R - Right, B - Bottom)`, and
  `Tile.CollisionArea == AdjustCollisionArea.ApplyTo(DrawLocationWorld)`.
* Save files are unaffected — the member names `Top`/`Bottom`/`Left`/`Right` did not change — but
  **any hand-written adjustment must be re-read under the inset rule**, including values loaded
  from an existing save.
* `CollisionAdjust` gains `ApplyTo(Rectangle)`, `IEquatable<CollisionAdjust>` and `==` / `!=`.

### 2. `CoordinateSystemTypes.Oblique` is now `ObliqueRight`

Source-breaking only. The numeric value is still `5` and the enum serializes as an int, so existing
saved layers load unchanged. A new `ObliqueLeft` (`6`) is its mirror — a left-receding sheared
lattice. `ObliqueCoordinates` was renamed to `ObliqueRightCoordinates`.

### 3. Three unused public collision types were removed

`CollisionDirectionHelper`, `CollisionDirectionFrom` and `CollisionResult` are gone (upstream
removed them; nothing in this repository or its samples referenced them). Collision response
remains automatic — query `SceneLayer.ColliderRegistry.QueryAabb` for game logic.

### 4. `RenderSurfaceHost.Bind` throws when the scene belongs to another host

A `Scene` belongs to exactly one render-surface host. Binding a scene that is already bound
elsewhere now throws `InvalidOperationException`; a failed bind leaves both hosts on the scenes they
already had. For several camera perspectives into one scene, add **views** to the one host
(`ViewManager.AddView`), not a second host.

Related: disposing a host — or the scene bound to it — releases the binding and unsubscribes the
host from the scene's `SceneDisposing` event, and a disposed host's `Scene` reverts to
`Scene.Empty`.

### 5. Sprites and layer tiles get default collision profiles

* New sprites take `SpriteManager.Instance.DefaultCollisionProfile` — `"Actor"` (group `Actors`,
  collides with `WorldStatic | Actors | Projectiles | Triggers`) — instead of the previous
  all-groups / all-masks collider.
* A layer's fixed tiles take `SceneLayer.DefaultTileCollisionProfile` — `"World"` (group
  `WorldStatic`, collides with `Actors | Projectiles`) — instead of none/none.
* Games that relied on everything colliding with everything must either set
  `SpriteManager.Instance.DefaultCollisionProfile` / `SceneLayer.DefaultTileCollisionProfile`, pass
  `collisionProfileName` to `SpriteManager.CreateSprite`, or call `Tile.SetCollisionProfile(name)`.
* `Tile.CollisionsEnabled` is now a projection of the new `Tile.CollisionType`
  (`None`/`Blocking`/`Trigger`): enabling collisions on a `None` tile promotes it to `Blocking`
  (or `Trigger` when the collider already responds as one); disabling resets the type to `None`.
  Existing save files load unchanged — an enabled tile with no recorded type loads as `Blocking`.

### 6. `IsometricAxial` layers pack as true diamonds — tile anchors MOVE

`IsometricAxialCoordinates` now uses a real affine basis (`px = gx*W + gy*W/2`, `py = gy*H/2`) and
its inverse, so rows pack edge to edge. It previously stepped anchors by the full tile size, laying
the layer out like an orthogonal grid. **Hand-placed content on an `IsometricAxial` layer shifts**;
re-check any such layer. Other coordinate systems are unaffected.

### 7. `Viewport.Zoom > 1` now zooms IN

The four `View` coordinate conversions, `View.ZoomAroundScreenPoint` and `TextBlock`'s scene-layer
text scale all changed sign to match the documented meaning: `Zoom > 1` magnifies, `< 1` zooms out,
and a view shows `TargetRectPx / Zoom` of the world. Code written to compensate for the previous,
inverted behaviour must drop the compensation.

### 8. `CompositeSprite.GetPosition()` returns GRID coordinates

It used to return world pixels, which did not match `SetPosition` or `AddChildWithOffset`. Nothing
in this repository relied on the pixel value.

### 9. Desktop mouse input is no longer delivered as touch contact 0

`CodeBrixTouchInputAdapter(UIElement element, bool emulateMouse = false)` ignores mouse pointers by
default, so a desktop click raises mouse events only. To restore the old behaviour, pass
`emulateMouse: true` to the adapter or to `Engine.InitializeCodeBrixTouchAdapter(element,
emulateMouse)`, or override the new `protected virtual bool EmulateMouseAsTouch` on
`CodeBrixGameHost` / `SoftwareRenderedGameHostBase`.

### 10. Mouse events are really throttled now

`EngineConfiguration.TimeBetweenMouseEvents` (default `0.03` seconds) had no effect; it does now, so
at most one mouse event per 30 ms reaches the game. **Set it to `0` for an event every cycle**
(mouse-look, drawing tools, anything sampling the pointer path).

A press and release that both fall inside one throttle window are collapsed. Automated UI tests must
therefore hold the button for roughly 300 ms or more — a synthetic click as short as `xdotool`'s
default (~12 ms press-to-release) is dropped entirely. Human clicks are far longer and unaffected.

### 11. `Timer.Add` validates its length

Both overloads now throw `ArgumentOutOfRangeException` when `length` is not finite (`NaN`,
`±Infinity`), is zero or negative, is shorter than one high-resolution tick, or is so large it does
not fit a positive `Int64` of ticks. `Timer.Add(..., 0)` used to spin the engine thread.

### 12. `TilesheetDefinitionSerializer.Save(path, tilesheet)` mutates a bitmap-only tilesheet

A tilesheet with neither an image file path nor an asset identifier gets its bitmap auto-persisted
as a sibling `.png` next to the `.gts` (same base name), and the sheet's `ImageFilePath` is
re-pointed at that file, so the written definition is loadable. File-backed and asset-backed sheets
are untouched. Pass a path you are happy to have a `.png` appear beside.

### 13. `EngineConfigurationFile.Load` reads the file once

`Load` no longer builds its configuration root with reload-on-change and disposes it after reading,
so repeated loads no longer leave live file watchers behind. The behaviour change: an
`EngineConfigurationFile` obtained from `Load` does **not** track later edits to the file on disk —
call `Load` again to pick them up.

### 14. `gameengine.json`'s root key is `EngineConfig`

The shipped defaults file used the root key `"EngineConfiguration"`, which the loader never looked
at, so the shipped defaults were silently ignored. It is now `"EngineConfig"`, matching the loader.
Any `gameengine.json` in a game — and any documentation or sample showing one — must use that key.

---

## NEW

### Display effects

A new `CodeBrix.Platform.GameEngine.Effects` namespace and a new `RenderSurfaceHostBase.Effects`
property drive presentation-level transitions over a whole `View` or a whole `SceneLayer`.

```csharp
host.Effects.Run(hudView, new FadeInEffect(0.4f));
host.Effects.Run(mapLayer, new EraseEffect(EffectDirection.FromLeftToRight, 0.8f));
```

* `EffectsManager`: `Run<TEffect>(View|SceneLayer, TEffect)`, `Cancel(effect)`, `CancelAll()`,
  `ActiveEffects`.
* Effects: `FadeInEffect`, `FadeOutEffect`, `SlideInEffect`, `SlideOutEffect`, `FillEffect`,
  `EraseEffect` (view or layer); `ZoomInEffect`, `ZoomOutEffect`, `EarthquakeEffect` (view only).
* `DisplayEffect` carries `Id`, `DurationSeconds`, `Easing`, `Status` (`EffectStatus`:
  `Pending` → `Running` → `Completed` | `Cancelled`), `Progress`, the `Completed` and `Cancelled`
  events, and `Cancel()`. `EffectDirection` supplies the eight directions.
* **One effect per target per channel** (Transform / Opacity / Reveal / Zoom). Running a second on
  the same pair replaces the first *without* restoring its state, so the new effect continues from
  the current value; `Cancel` / `CancelAll` / host disposal *do* restore it. An effect instance runs
  once. An effect whose target the host no longer owns is dropped silently.
* A view with a running presentation effect no longer clips the views beneath it, so a translucent,
  wiped or slid view reveals what is below.
* While any view-level effect runs, the CPU dirty-rectangle optimisation is suspended and the
  surface is recomposed in full each frame. Layer-only effects keep dirty-rect rendering.
* View-mode direct drawings shift with their view's effect offset.
* Effects advance on the render cadence, so `Engine.Pause()` freezes one mid-effect and `Resume()`
  shifts its time baseline: nothing bursts to completion across a pause.
* All of this API is additive.

### Direct lighting

* `DirectRadialLight` — a scene-layer radial light: colour, intensity, hotspot and midpoint gradient
  ratios, `SKBlendMode` (`Screen` by default), optional flicker (`FlickerEnabled`, `FlickerAmount`,
  `FlickerRefreshHz`), a `Changed` event, and fluent `MoveTo` / `SetRadius` / `SetIntensity`.
* `DirectLightLayer` — the logical owner of a group of lights on one `SceneLayer`:
  `AddTorchLight`, `Remove`, `Clear`, `Lights`, `DefaultZOrder` (10,000), `LightAdded` /
  `LightRemoving`.
* `DirectDarknessOverlay` — a view-mode darkness/fog quad punched through by world-space reveal
  sources. `TrackLight` / `TrackLightLayer` keep the holes attached to lights (idempotent per light
  and per light layer) and drop them when a light is disposed.
* `DirectSceneLayerDarknessOverlay` — the scene-layer sibling: a world-bounded darkness region
  (`DarknessWorldBounds`) that scrolls with its layer and is visible to every view looking at that
  part of the world. It refuses lights belonging to a different layer.
* Overlays default to ZOrder 20,000 so darkness composites over the lights. A flickering light does
  not jump phase across a pause.

### Collision profiles

* `Scene.CollisionProfiles` is a `CollisionProfileRegistry` carrying four standard profiles
  (`CollisionProfileNames.World` / `Actor` / `Projectile` / `Sensor`) and persisted with the scene.
  `Define(name, collisionGroup, collidesWith, collidesWithAll)`, `Get`, `TryGet`,
  `GetProfileNames()`.
* `CollisionProfile`: `Name`, `CollisionGroup`, `CollidesWith`, `CollidesWithAll`,
  `ResolveCollisionGroup(CollisionGroupRegistry)`, `ResolveCollidesWith(CollisionGroupRegistry)`.
* `TileCollisionType` (`None` / `Blocking` / `Trigger`), serialized as a **string** in both `.gts`
  files and engine save files.
* `SceneLayer.DefaultTileCollisionProfile`, `SpriteManager.DefaultCollisionProfile`,
  `SpriteManager.CreateSprite(..., collisionProfileName)`, `Tile.SetCollisionProfile(name)`,
  `Tile.CollisionTypeByFrame`, the protected `Tile.AttachCollider(ICollider)` and
  `Tile.CopyCollisionSettingsFrom(Tile)`, `CollisionGroupRegistry.GetMask(IEnumerable<string>)`.
* Upstream-parity quirk worth knowing: adding a layer to a scene — which also happens during a load
  — re-applies that layer's `DefaultTileCollisionProfile` to every fixed tile, so a per-tile
  `SetCollisionProfile` on a **layer tile** does not survive a save/load or a re-add. Sprites are
  unaffected.

### Collision metadata on tilesheets and `.gts` files

* `TilesheetRegion.CollisionAdjust` and `TilesheetRegion.CollisionType` are region-wide defaults;
  every cell may override either one:
  `GetFrameCollisionAdjust`, `TryGetFrameCollisionAdjustOverride`, `SetFrameCollisionAdjust`,
  `ClearFrameCollisionAdjustOverride`, `GetFrameCollisionArea`, and the matching
  `…FrameCollisionType` quartet.
* Assigning a frame value **always** records an override, even when it equals the region default;
  changing the region default re-applies only to frames without an override, so hand-tuned cells
  survive a region-wide edit.
* `Frame` mirrors the same view of one cell: `CollisionAdjust`, `CollisionArea`,
  `HasCollisionAdjustOverride`, `ClearCollisionAdjustOverride()`, `CollisionType`,
  `HasCollisionTypeOverride`, `ClearCollisionTypeOverride()`.
* `Tile.AdjustCollisionAreaByFrame` (default `false`, persisted): the first frame assigned to a tile
  seeds its collision adjustment and type unless one was set explicitly first, and later frame
  changes move them only when the matching by-frame flag is on.
* `Tilesheet.AddRegion(..., CollisionAdjust? collisionAdjust = null, TileCollisionType collisionType
  = None)` — source-compatible, binary-breaking for already-compiled callers. The copy constructor
  copies region defaults and every per-frame override.
* `Tilesheet.PersistImageToFile(path, format = Png, quality = 100)` promotes a bitmap-only tilesheet
  to file-backed.
* `.gts` schema additions are **additive and backward compatible**: `TilesheetRegionDefinition`
  gains `CollisionAdjust`, `CollisionType` and a `Frames` array of
  `{ XTile, YTile, CollisionAdjust?, CollisionType? }`, where a missing or null value means
  "inherits the region default". Files written without them load with `CollisionAdjust.None`,
  `TileCollisionType.None` and an empty `Frames` list. `TilesheetDefinitionSourceKind` moved to its
  own file (same namespace; no source change for callers).

### Sprite rotation

* `Sprite.Rotation` — float degrees, clockwise about the centre of the render rectangle, normalised
  to `0 <= r < 360`; a non-finite value throws `ArgumentOutOfRangeException`. It is a rendering
  property (the collision rectangle stays axis-aligned) and it round-trips through save files.
* `Sprite.VisualBoundsWorld` and `Sprite.GetVisualBoundsScreen(View)` are the axis-aligned bounds
  enclosing the rotated sprite. Dirty-region invalidation, `SpriteManager`'s hit tests
  (`GetSpritesInWorldRectRange`, `GetSpritesInViewRectRange`, `GetSpritesAtViewPixel`) and the
  `SceneLayer` sprite query all use them, so a rotated sprite is picked and repainted correctly.
* `MovementController.WrapX` / `WrapY` setters are now public.

### Ready-made components

* `SplashOverlay` — a view-sized splash that fades in, holds, fades out and disposes itself, then
  raises `onSplashCompleted` on the engine thread.
  `TryCreate(string imagePath | Stream imageStream, host, view, fadeInSeconds = 0.45f,
  holdSeconds = 3f, fadeOutSeconds = 0.45f, onHolding, onHoldingAsync, onSplashCompleted, nickname)`
  returns `null` (and logs a warning) when the image is missing or undecodable or the host has no
  views, so a game can start without a splash. The hold ends when the hold timer **and** the
  `onHolding` work are both finished.
* `HealthBar` — a world-space bar that follows a sprite: `Value` / `MaxValue` / `Fraction` /
  `SetValue`, `BarSize`, `OffsetPx`, opt-in threshold colours (`UseThresholdColors`,
  `SetThresholdColors`, `SetThresholds`), `SetTrackColors`, `Show()` / `Hide()`,
  `RefreshPosition()`. It disposes itself with its target sprite.

Both are port-native components built on `DirectComposite` — there is no widget framework to adopt.

### `DirectRectangle` image fills

`SetFillImage(SKBitmap | SKImage, ImageFillMode mode = Stretch, float scale = 1f,
SKPoint? offsetPx = null, ImageFilterQuality filterQuality = Medium)` and `ClearFillImage()`.
`DirectRectangle.ImageFillMode` is `Stretch`, `Fit`, `Fill`, `Center`, `PixelPerfect` or `Repeat`;
the fill is clipped to the rectangle including its rounded corners. `scale` and `offsetPx` apply to
`Repeat` only. The image source stays caller-owned. Image fill and pattern fill are mutually
exclusive — setting one clears the other. Both throw `ArgumentOutOfRangeException` for a scale that
is not finite and positive, and `SetFillImage` also rejects an undefined `ImageFillMode`; a rejected
call leaves the existing fill intact.

### `ImageInstanceLayer` scene-layer mode

Two new world-space constructors attach an `ImageInstanceLayer` to a `SceneLayer`, so instances move
with the camera, parallax and zoom. In scene-layer mode the initializer / should-recycle / recycle
hooks receive `WorldBounds` instead of `ScreenBounds`, `ImageInstance.Bounds` is world pixels, and
dirty rectangles go to that layer's own refresh queue.

View-mode drawing now maps instance bounds *into* the destination rectangle instead of using
absolute screen coordinates. With matching origins and equal sizes — the ordinary case — output is
identical; letterboxed or scaled destinations now render correctly.

### Touch, gestures and poller teardown

* `ITouchAdapter.ConsumeBeganTouches()` is a new **default-interface method**, so existing custom
  adapters still compile.
* `PinchGestureRecognizer` gains `PinchStarted` and `PinchEnded` alongside `PinchUpdated`, plus the
  new `PinchPhase` enum. `PinchedEventArgs` gains `Phase`, `TouchIds`, `Center`,
  `StartingDistance`, `PreviousDistance` and `TotalScale`; the old two-argument constructor is kept.
* `SwipeGestureRecognizer.MinimumSwipeDistancePixels` (default 30).
* `MouseEventPoller` and `TouchEventPoller` are `IDisposable` and expose a static `Reset()`;
  `Initialize` disposes whatever instance was there before.

---

## FIXES

### Input

* A touch that begins and ends between two engine polls is no longer lost: touch began / ended /
  cancelled events are never throttled — `TimeBetweenTouchEvents` paces `TouchMoved` only. A contact
  first seen mid-gesture is normalized to `Began`.
* Pausing or stopping the touch poller clears contact and gesture state, so a finger held across
  `Pause`/`Resume` can no longer complete into a phantom tap or swipe.
* Tap and swipe timing is engine-tick based, so paused time no longer counts toward a gesture; a
  second contact cancels both candidates; a tap is distance-checked at its END position even when no
  `TouchMoved` event arrived; and a short fast tap is no longer also reported as a swipe.
* The phantom "scroll delta 0" mouse event that followed every real scroll is gone: a scroll event
  is raised only when the delta is non-zero and differs from the previous poll's.
* `Engine.Dispose()` now resets both pollers, so the platform adapters really unsubscribe; assigning
  `Engine.Instance.Input.TouchAdapter = null` tears the touch poller down.

### Rendering and views

* Anchored zoom (`View.ZoomAroundScreenPoint`) no longer wobbles or snaps at the end: the view owns
  the anchor, the camera is re-derived every update, and `Viewport.ZoomToOverDuration` is a true
  fixed-duration eased tween that lands exactly on the target. An anchored zoom cancels an explicit
  pan but leaves camera follow intact.
* GPU (Tier B) rendering no longer mixes transforms within one frame: `RenderContext` snapshots
  camera position, viewport rect, screen offset and zoom per render pass, and `View`'s conversions
  read that snapshot inside the pass. Game code on the engine thread is unaffected.
* GPU surfaces no longer accumulate dirty rectangles. Binding a scene to a GPU host clears every
  layer's refresh queue and closes it, and the full-frame GL path no longer force-refreshes each
  view overlay every frame — removing unbounded per-layer `List<Rectangle>` growth plus an O(n)
  containment scan per invalidation in long GPU sessions. `Scene.UsesDirtyRegionRendering` reports
  which regime a scene is in. Nothing visual changes.
* `RenderSurfaceHost.PresentBackbufferRect` presents the dirty rect clamped to the backbuffer bounds.
* The collision-box debug overlay (`SceneLayer.ShowCollisionBoxes`) is drawn only for tiles whose
  collisions are enabled.
* `OnSceneBound` fires exactly once per `CodeBrixGameHost` (it fired twice, so a subclass that built
  its game there built it twice and double-hooked its events).

### Movement and timing

* Direct-drawing movement is no longer capped at 33 ms of simulated time per engine update (the
  240 Hz fixed-step accumulator is gone): `MoveTo` / `MoveBy` / `Follow` run in real time at any
  update rate. Side effect: a non-pause stall (a debugger break, a long GC) now advances movement by
  the real elapsed time instead of slowing it down. `Engine.Pause` is unaffected.
* An overdue `TimerCycles.Once` timer fires exactly once instead of once per elapsed interval.
* `MovementController.StopAllMovement()` clears follow state as well, as its doc comment always
  promised; `MovementController.Dispose()` clears scripted-completion callbacks.
* `DirectDrawingMovableBase.Update` no longer throws when the engine thread observes a drawing
  before its derived constructor has assigned `Movement`.
* Hex layers (`HexAxialFlatTop`, `HexAxialPointedTop`) map fractional grid positions to interpolated
  pixel anchors, including a half stagger, instead of rounding to the nearest cell. Integer
  positions are unchanged.

### Collisions, sprites and tilesheets

* `SceneLayerTile` no longer shadows `Tile.Collider`: `layer[x, y].Collider` is non-null after layer
  construction, and `CollisionsEnabled` really registers and unregisters on fixed layer tiles (it
  was a silent no-op).
* `Sprite.TranslateWorldPx` applies a grid-space delta, so collision push-out no longer snaps a
  sprite to whole pixels on both axes.
* `SpriteManager.CloneSprite(sprite, layer)` binds the clone's `MovementController` and collider to
  the **destination** layer, carries the source's rotation and collision settings, registers the
  clone's collider when the source had collisions enabled, and registers the clone with
  `SpriteManager` only once it is fully built.
* `Sprite.ZOrder` is an `override` of `Tile.ZOrder` (was `virtual new`), so the minimum-of-1 clamp
  and the refresh enqueue apply when `ZOrder` is set through a `Tile`-typed reference.
* `Tile.Dispose()` no longer throws when the tile has no scene layer or no collider.
* Asset-backed `.gts` tilesheets register under the definition's `Name` instead of the asset entry
  name; a malformed image definition (a `FilePath` combined with `AssetsFilePath` /
  `AssetEntryName`, or an `AssetsFilePath` without an `AssetEntryName`) throws
  `InvalidOperationException` instead of being silently resolved.

### Direct drawings

* The `DirectRectangle` scene-layer constructor works; it previously always threw `ArgumentException`
  ("worldBounds cannot be null when using DirectDrawingMode.SceneLayer").
* `DirectRectangle` and `DirectImage` release their cached paints (and the pattern shader)
  deterministically in `Dispose` instead of leaving them to the finalizer, and both unhook from the
  drawing manager before releasing them.
* `DirectRectangle.SetFillPattern` validates its scale (finite and greater than zero) and rejects a
  null bitmap; a rejected call leaves the existing pattern intact.
* Translucent particle colours render translucent: `ParticleSurface`'s tint multiplies the
  particle's own alpha with the life fade and the tint alpha.

### Configuration, logging and lifecycle

* `autoSaveConfig: true` really saves: the configuration file is written back when it is disposed,
  and `Engine.Dispose()` disposes it before the logging system shuts down.
* The shipped `gameengine.json` defaults are read at last (see breaking change 14).
* `EngineConfiguration.LoggingQueueCapacity` is honoured at `Initialize`, and shutdown stops
  asynchronous logging whenever it is asynchronous, flushing per `FlushAsyncLogsOnShutdown`.
* `AddEngineLogging` no longer registers a circular `ILoggerFactory` — it used to throw on the first
  resolve. An `ImplementationInstance` is adopted directly; a factory or type registration is
  re-registered at the same lifetime around the original one.
* `EngineLogger.SetLogLevel` is a no-op once an application-supplied `ILoggerFactory` is in use, so
  `Initialize(logLevel:)` cannot override an application's own filters.
* `EngineStateParts.All`'s summary no longer lists a member that does not exist.

---

## SAMPLES AND REPOSITORY

* **New: `samples/Platformer.Brix`** — a side-view platform game on `CodeBrixGameHost` +
  `GameSurfaceCanvas` with the render resolution pinned to 960x576. It is the reference consumer for
  fixed layer-tile colliders, collision profiles, `CollisionAdjust` insets and
  `ColliderRegistry.QueryAabb`, and its tilesheet is painted in code, so it ships no image assets.
* **New: `samples/SpaceDuel.Brix`** — a GPU-tier space duel: `Sprite.Rotation` on ships and lasers,
  `WrapX`/`WrapY`, two parallax star layers, `ParticleSurface` bursts, AI raiders, per-ship
  `HealthBar`, a `SplashOverlay` title card, and a view-space HUD with live GPU FPS.
  `SPACEDUEL_USE_CPU=1` runs the identical game on the CPU tier. All of its art is generated in code.
* **`samples/Spot.Brix`** gained a splash overlay, a CodeBrix.Platform XAML New Game dialog (2–4
  players, per-player name / human-or-computer / colour with automatic de-duplication, board 3x3 to
  12x12) reachable from the toolbar or from the first click on the opening screen, option
  persistence in the `"spot"` section of a `gameengine.json` pinned to `AppContext.BaseDirectory`,
  `savegame.json` written when a game ends, and working select / deselect / knock sound effects.
  Fixed there: a computer turn scheduled in a previous game could fire into a new one; an AI turn
  with no legal moves threw; the cloud particle surface could be disposed twice; stale score and
  banner overlays survived a new game; and `SetJiggleEnabled` threw before `Initialize`.
  `SpotBrixGameHost.StartNewGame(NewGameOptions)`, `StartNewGameOnEngineThread`,
  `BeginPostSplashStartup` and the `NewGameRequested` event are public, as are `GameConfig`,
  `NewGameOptions`, `ColorItem`, `Player` and `PlayerType`, so the UI layer can bind to them.
* The sample count is nine. None of them is in the repository `.slnx` — each carries its own — and
  each sample's `Microsoft.Extensions.*` pins were bumped to 10.0.11 to match the engine.
* `THIRD-PARTY-NOTICES.txt` records the upstream revision this port now tracks, together with the
  original vendoring point.
