# Release notes — 2026-09-17

`CodeBrix.Platform.GameEngine.MitLicenseForever` (engine core + CodeBrix.Platform host layer),
and the first release of `CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever`, a
separately published add-on package built on the new asset-provider contract.

This release separates the resolution a game renders at from the size it is presented at,
makes scene layers genuinely periodic (wrapping worlds with no seam), replaces the
timer-driven loop with a fixed-step simulation, hardens engine start-up and shutdown, and
adds a core asset-provider contract — including 3D model assets, as data and as pre-rendered
sprite frames — so a separate library can catalog and materialize third-party asset
collections. `THIRD-PARTY-NOTICES.txt` records the upstream revision the core now tracks.

The solution builds with 0 warnings and 0 errors; the test suites stand at 813 engine-core
tests, 19 host tests, 45 gamepad tests and 384 Kenney-asset tests (one of those an opt-in
corpus scan, skipped unless an environment variable points at a Kenney collection), and all
nine sample X11 heads that existed before this release were built and exercised live.

---

## BREAKING CHANGES

Read this section before upgrading. Everything here either fails to compile or silently
changes what a game does.

### 1. A window resize no longer resizes the Backbuffer — it letterboxes

A render surface now establishes its logical Backbuffer resolution **once**, from the first
valid surface size times `EngineConfiguration.RenderScale` (new, default 1). Every later
resize changes presentation only: the complete image is fitted into the control, centred,
and the margins are cleared (letterbox / pillarbox).

* Resizing no longer resizes the Backbuffer, no longer rescales Views, and no longer exposes
  more of the world.
* `CodeBrixGameHost.OnRenderSurfaceResized(width, height)` still fires, but the size it
  reports is the **surface's**, not ScreenPx. Content that was re-anchored from that hook
  must be anchored once from `RenderSurface.Host.Backbuffer.Width/Height` (or the new
  `LogicalWidth`/`LogicalHeight`). `Spot.Brix`, `SpaceDuel.Brix` and `GpuRender` show the change.
* To keep the old behaviour — the render resolution follows the window — set
  `GameSurfaceCanvas.TrackWindowSize = true` (or `RenderSurfaceHostBase.TrackAdapterSize`).
  Use that **or** `SetRenderResolution`, not both.
* `ViewManager` sizes every view from the logical resolution instead of from the render
  surface adapter.

### 2. Every ScreenPx API is in logical Backbuffer pixels

`TouchPoint.Position`, `View.ScreenPxToWorldPx` / `ScreenPxToGrid`, `Viewport.TargetRectPx`
and direct-drawing `ScreenBounds` are all logical Backbuffer pixels. The host normalizes
mouse and touch positions through `RenderSurfaceAdapterBase.AdapterPxToScreenPx` before they
reach the pollers, so engine input already arrives in that space; a pointer over the
letterbox margins keeps its outside coordinates (negative, or past the logical size) instead
of being clamped, so capture and drag still work. Relative-mouse deltas are unchanged.

A game doing its own UI-level hit testing on the canvas must call
`canvas.RenderSurfaceAdapter.AdapterPxToScreenPx(point)` rather than re-deriving the fit, and
must not feed an already-normalized value through the transform twice.

### 3. Presentation filtering is Linear by default

The canvas used to present with a hard-coded nearest-neighbour filter. It now honours
`EngineConfiguration.RenderScalingFilter`, whose default is `Linear` — so **a pixel-art game
scaled up into a larger window goes blurry** until it sets
`RenderScalingFilter = RenderScalingFilter.NearestNeighbor`. `Platformer.Brix` shows this.
**Set it in `OnEngineInitialized`, not earlier:** `Engine.Initialize` replaces
`Engine.Configuration` with the configuration it loads, so a value assigned in `OnSceneBound`
or any other hook that runs before it is silently discarded.
Presentation filtering is independent of `Viewport.Zoom` and of tile/image filter quality;
pixel art wants both set.

### 4. Host render-surface adapters lost their fixed-size constructor arguments

`CodeBrixPlatformBitmapRenderSurfaceAdapter` and `CodeBrixPlatformGpuRenderSurfaceAdapter`
no longer take `fixedWidth` / `fixedHeight`. The render resolution belongs to the host
(`EngineConfiguration.RenderScale`, `GameSurfaceCanvas.SetRenderResolution`); an adapter now
always tracks the size of its canvas, because that size is the presentation destination, not
the resolution. `RenderSurfaceAdapterBase` also gained a constructor taking
`initialSizeAvailable`.

### 5. `RenderSurfaceAdapterBase.Width` / `Height` route through presentation state

Both are now backed by the adapter's presentation state: assigning them goes through
`SetDestinationSize` and therefore raises `Resized`, and the first valid layout raises
`Resized` even when it matches the placeholder size.

### 6. `GpuBackbuffer.RequestResize` is live, and the GPU target is allocated once

`RequestResize` was a no-op; it now queues an explicit logical-resolution change, applied by
the new `EnsureInitialized(GRContext)` on the GL thread (or, while the backbuffer is still on
its temporary CPU raster surface, by the next `BeginFrame`). A window resize never reallocates
the GPU render target any more, so **`EngineConfiguration.MsaaSampleCount` takes effect the
next time the target is allocated** — set it before the first GPU frame, as `SpaceDuel.Brix`
does — rather than on the next frame.

### 7. `SceneLayer.WrapGrid(PointF)` only normalizes ENABLED axes

It previously always wrapped both axes. It now normalizes only the axes whose flag is set,
returns its input unchanged when neither axis wraps, rejects a non-finite value with
`ArgumentOutOfRangeException`, and validates the layer's repeat geometry with
`InvalidOperationException` on first operational use.

The ordinary indexer is unchanged and still never wraps: `layer[-1, 0]` is `null` on a
periodic layer too. Use the new `ResolveWrappedTile`.

### 8. `ColliderRegistry.QueryAabb` returns unique canonical colliders

On a periodic layer a collider whose images overlap the query several times is reported
**once**, and inspecting its ordinary bounds does not describe a seam instance. Use the new
`QueryInstances` when the translated per-image bounds matter.

### 9. Custom `BackbufferBase` implementers no longer get sprite rotation for free

Rotation now happens in `Sprite.Draw`, not inside each backbuffer, so every rendering backend
produces the same transformed output. `BitmapBackbuffer.DrawTileFrame` and
`GpuBackbuffer.DrawTileFrame` no longer special-case rotated sprites, and a custom drawable
that overrides `Draw` is responsible for its own visual transforms.

### 10. `GameHostBase.Dispose()` stops and JOINS the engine before any cleanup hook

The order is now `StopEngine()` → `Engine.StopAndWait()` → the port's pause/resume unhooks →
`OnDisposing()` → `UnhookEvents()` → `DisposeEngine()`. The engine used to be stopped *after*
`OnDisposing` / `UnhookEvents`, which let a game's HUD dispose its paints while drawing was
still in flight.

### 11. `IEnginePlugin` hook parameters are `deltaSeconds`

`OnPreCycle`, `OnPreFrameRender`, `OnPostFrameRender` and `OnPostCycle` renamed their
`deltaMs` parameter to `deltaSeconds` (the value was always seconds). Source-compatible
unless a caller used the named argument.

### 12. `Engine.Start` no longer waits forever, and `Engine.Tick` runs fixed steps

`Start(SynchronizationContext)` waits at most `EngineConfiguration.StartInitializationWaitTimeout`
seconds (new, default 30) for an initialization running on another thread, then throws
`InvalidOperationException` — on timeout, on a failure that happened on the other thread, or
when a local initialization attempt did not succeed. `StartTimerDriven` throws the same
"Engine failed to initialize." when its own `Initialize()` did not succeed.

In timer-driven mode `Tick()` is now "one presentation opportunity": zero or more **fixed**
simulation steps (`EngineConfiguration.TimerDrivenSimulationRate`, default 120 Hz, capped by
`EngineConfiguration.MaxTimerDrivenSimulationSteps`, default 8) followed by at most one
foreground render, whose cadence still follows `TargetFPS`. Excess accumulated time is
discarded rather than replayed. Update-side clocks (pre-cycle timers, sprite movement, tile
animation) read the fixed simulation clock in this mode, and pre-cycle timers created there
start from that clock rather than from wall time.

---

## NEW

### Layer wrapping — periodic scene layers

`SceneLayer.WrapHorizontally` / `WrapVertically` (serialized names unchanged) now make a
layer's content periodic across rendering, collision queries, camera follow and layer-bound
world-space drawings. Nothing is cloned: the grid keeps one canonical tile per cell and the
engine draws and collides with translated images of it. A scene saved with a flag already
`true` becomes periodic when it is loaded.

```csharp
world.WrapHorizontally = true;                       // the COLUMN axis repeats
SceneLayerTile? t = world.ResolveWrappedTile(-1, 80); // canonical [99, 0]
PointF canonical   = world.WrapGrid(new PointF(101, -1));
```

* New API: `SceneLayer.ResolveWrappedTile(int column, int row)`,
  `SceneLayer.GetWrappedOffsets(RectangleF contentBounds, RectangleF queryBounds)`,
  `ColliderRegistry.QueryInstances(in Aabb, int, int, List<ColliderInstance>, ICollider?)`
  and the `ColliderInstance` record struct (canonical collider + translated world bounds).
* "Horizontal" means the **column** axis and "vertical" the **row** axis; on a projected grid
  the period vectors may be diagonal. Flat-top hex layers need an even column count to wrap
  columns, pointed-top hex layers an even row count to wrap rows, and pixel-rounded
  projections need a consistent integral period. Geometry is validated on first operational
  use — not in the property setter and not at load — and throws `InvalidOperationException`.
* Adjacency (`GetAdjacentTile` and all seven coordinate strategies) follows the enabled axes.
* Collision: tiles and colliders are queried as a canonical collider plus translated bounds,
  so seams, corners and negative positions all work; collision adjustments and per-frame
  regions are applied before the translation, push-out is computed against the overlapping
  image, and the trigger/solid events still carry the canonical colliders.
* Camera: a followed target is tracked through its nearest equivalent image, topology comes
  from the followed object's layer (otherwise the first visible layer in scene insertion
  order), and clamping to `WorldBoundsPx` happens in the period-vector basis, so wrapped axes
  are unconstrained.
* Layer wrapping does **not** enable `Movement.WrapX` / `WrapY` — sprite normalization stays
  a per-sprite opt-in. View-bound drawings and view-space UI never repeat.
* Bitmap (CPU) hosts force full-view composition while any visible layer wraps; the GPU path
  keeps its existing full-frame behaviour. Wrapped selection scans canonical content, and
  candidate/visible instance ranges are capped at one million, which throws
  `InvalidOperationException` rather than generating unbounded work.

### Render resolution and presentation

* `EngineConfiguration.RenderScale` (float, finite and positive, default 1; above 1
  supersamples) and `EngineConfiguration.RenderScalingFilter` (`Linear` default,
  `NearestNeighbor` for pixel art), with matching `gameengine.json` keys `"RenderScale"` and
  `"RenderScalingFilter"` (the filter binds by name).
* New public `PresentationTransform` record struct (`Fit`, `TryAdapterPxToScreenPx`,
  `ScreenRectToAdapterRect`) and `RenderScalingFilter` enum in
  `CodeBrix.Platform.GameEngine.Rendering`.
* New on `RenderSurfaceAdapterBase`: `Presentation`, `PresentationScale`,
  `AdapterPxToScreenPx(PointF)`, `DrawImage(SKCanvas, SKImage, SKColor)`.
* New on `RenderSurfaceHostBase`: `PresentationScale`, `LogicalWidth` / `LogicalHeight` (the
  resolution the surface renders at, **including** a requested change the rendering thread has
  not applied yet), `RequestRenderResolution(int width, int height)` and `TrackAdapterSize`.
* `GameSurfaceCanvas.SetRenderResolution(w, h)` survives and forwards to
  `RequestRenderResolution`. It is still safe to call before the first access to `Host` — and
  no longer forces the render tier when you do — and a request made before the first layout is
  applied when the control gets one. New `GameSurfaceCanvas.TrackWindowSize` (bool, default
  false) exposes `TrackAdapterSize`.
* The canvas no longer computes its own aspect fit: it presents through
  `RenderSurfaceAdapterBase.DrawImage`, the same transform pointer input is normalized with,
  so what is painted and what is clicked can never disagree.
* `GpuBackbuffer.EnsureInitialized(GRContext)` is new, and `BitmapBackbuffer` coalesces resize
  requests into an immutable record and skips a request that matches the current size.
* Three GPU presentation helpers on `RenderSurfaceHostBase`:
  `GlRenderToCanvas(SKCanvas, bool renderWhilePaused = false)`,
  `GlDrawCurrentFrameToCanvas(SKCanvas)` and `GlSnapshotCurrentFrame()`. They let a GPU
  presenter draw the backbuffer surface straight onto a platform canvas with no intermediate
  `SKImage`, and re-present the current frame on presentation loops that run faster than the
  configured render cadence. All three return `false`/`null` on a surface that is not
  GL-thread rendered, and `GlRenderToCanvas` honours the global pause exactly as
  `GlRenderAndSnapshot` does: while paused it re-presents the frame that was last rendered
  instead of advancing the scene, unless `renderWhilePaused` is passed.

### Engine lifecycle

* New `Engine.StopAndWait()`: stops the engine and blocks until the background cycle has
  finished, so a host can release native drawing resources without racing the cycle. It throws
  when called from the active engine thread, and it releases a cycle loop parked by the global
  pause.
* `Engine.Initialize` now signals completion even when it throws: a failed initialization no
  longer leaves the engine reporting `IsInitializing` forever, and it can simply be retried.
* Disposing the engine from inside an engine cycle (or from the engine thread) no longer
  self-deadlocks: the managed cleanup is deferred to a continuation on the cycle task.
  `Dispose()` is also re-entrancy safe.
* Global pause is fully honoured in timer-driven mode: ticks taken while paused run no
  simulation steps, and the first resumed tick does not replay the paused interval.
* New configuration keys, all present in the shipped `gameengine.json`:
  `StartInitializationWaitTimeout` (30), `TimerDrivenSimulationRate` (120),
  `MaxTimerDrivenSimulationSteps` (8), plus `RenderScale` and `RenderScalingFilter` above.

### Asset providers

`Engine.Managers.AssetProviders` (`CodeBrix.Platform.GameEngine.Assets.Providers.GameAssetProviderRegistry`)
is a new registry for `IGameAssetProvider` implementations that catalog assets held in
bundles, archives or folders and materialize them into the engine's own registries. Asset
keys are namespaced `<providerId>:<provider-relative path>` and are also the registry keys of
the materialized objects.

* The contract: `GameAssetKind`, `GameAssetDescriptor`, `GameAssetQuery`, `IGameAssetProvider`,
  the capability interfaces `ITilesheetAssetSource`, `IAudioAssetSource`, `IFontAssetSource`,
  `ITiledMapAssetSource`, `IModelAssetSource`, the option records
  `TilesheetMaterializeOptions`, `TiledMapImportOptions`, `ModelMaterializeOptions` and
  `ModelRenderOptions` (with `ModelProjection`), the tile-map result types `TiledMapImport`,
  `TiledObjectGroup`, `TiledObject`, `TiledTileInfo`, and `UnsupportedGameAssetException`.
* The registry dispatches `LoadTilesheet(key, options?)`, `LoadAudio(key, volume, pan)`,
  `LoadFont(key)`, `ImportTiledMap(key, scene, options?)`, `LoadModel(key, options?)` and
  `LoadModelAnimation(key, animationName, framesPerSecond = 24)` to the owning provider. An
  asset whose kind the engine cannot represent, or whose provider does not implement the
  matching capability, raises `UnsupportedGameAssetException` ("This type of asset is not
  supported at this time."); a key no provider owns raises `KeyNotFoundException`.
* **3D model assets are part of the contract, by two independent routes.** The engine still
  draws no 3D, so a provider may offer either or both:
  * **Model data** — `IModelAssetSource.MaterializeModel` / `MaterializeModelAnimation`, reached
    through `LoadModel` / `LoadModelAnimation`, returns the new format-neutral model family in
    `CodeBrix.Platform.GameEngine.Assets.Models`: `GameModel` (`Meshes`, `Materials`,
    `BoundsMin` / `BoundsMax` / `BoundsCenter` / `BoundsRadius`, `Pivot`, `TriangleCount`,
    `VertexCount`, `AnimationNames`, `Animations`, `TryGetAnimation`), `GameModelMesh`
    (`Positions`, `Normals`, `TexCoords`, `Indices`, `MaterialIndex`), `GameModelMaterial`
    (`AlphaMode` / `GameModelAlphaMode`, `AlphaCutoff`, `BaseColorFactor`,
    `BaseColorTextureRgba` with its width and height, `MetallicFactor`, `RoughnessFactor`,
    `DoubleSided`), and `GameModelAnimationClip` / `GameModelAnimationFrame` /
    `GameModelFrameMesh` for baked vertex frames. It is built on `System.Numerics` alone and is
    pure data — no disposal, no GPU handles, nothing registered: `LoadModel` hands the model to
    the caller. Three guarantees hold for every clip: a frame's meshes align one-for-one with
    the model's meshes and carry the same vertex counts (`IsCompatibleWith(model)` checks it),
    the payloads are GPU-upload friendly, and a clip carries `Duration` and `FrameRate` so the
    caller owns timing (`GetFrameIndex(timeSeconds, loop)`). `ModelMaterializeOptions`
    (`AnimationNames`, `BakeAllAnimations`, `AnimationFramesPerSecond` = 24) bakes nothing by
    default, while `GameModel.AnimationNames` is always filled, so a caller can come back for
    the clips it wants; `LoadModelAnimation` bakes one later and validates the clip's internal
    consistency, throwing `InvalidDataException` naming the provider and key.
  * **Pre-rendered sprite frames** — `LoadTilesheet`'s kind gate now admits
    `GameAssetKind.Model3D`, and the new `TilesheetMaterializeOptions.ModelRender`
    (`ModelRenderOptions`: `FrameSize` 128x128, `Directions` 8, `StartYawDegrees`,
    `PitchDegrees` 30, `Projection` with `FieldOfViewDegrees`, `AnimationNames`,
    `IncludeRestPose`, `AnimationFramesPerSecond` 12, `Supersample` 2, `LightDirection`,
    `AmbientLight`, `FitPadding`, and the constant `RestPoseRegionName` = `"rest"`) asks a
    provider to render the views once, at load time. The OUTPUT LAYOUT CONTRACT, documented in
    full on that record: one uniform-grid region per rendered animation named after the
    animation, plus a `"rest"` region; columns are frames, rows are camera directions, row `d`
    at yaw `StartYawDegrees + d * 360 / Directions`; every cell is `FrameSize`; one common fit
    scale is shared by the whole sheet. So a frame is `sheet["walk", frame, direction]` and the
    engine's ordinary `FrameSequence` / `Cycle` animation drives it.
* Tile-map object layers arrive as fuller data. `TiledObject` gained `Rotation` (degrees
  clockwise; `Bounds` stays the unrotated rectangle the map wrote), `Visible`, and `Tile` — a
  `TiledTileInfo` for a TILE OBJECT, naming its tile set, its global and local tile id, its
  tile and tile-set properties and its flip flags, and null for a shape, point or text object.
  `TiledObjectGroup` gained `Offset` (pixels, deliberately NOT folded into an object's
  `Bounds`), `Visible`, `Opacity` and `DocumentIndex` (its position among ALL the map's layers,
  so an object layer can be placed relative to the tile layers). `TiledMapImport.Tilesheets` is
  documented as one tilesheet per tile set the map REFERENCES, so the list does not vary with a
  layer filter.
* Registration is explicit and idempotent by `ProviderId`; registering a different instance
  under an identifier that is already taken replaces the old provider and disposes it.
  Providers are disposed on `Unregister`, on `Clear`, and when the engine is disposed.
  Provider registrations are runtime state and are never saved with engine state.
* Registry bookkeeping is locked, but provider calls happen outside the lock, so a provider
  implementation must be callable from more than one thread.

### A new package: `CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever`

The first implementation of that contract, published separately from the engine and versioned
independently of it, like the gamepad package. It reads a Kenney asset bundle — a downloaded
`.zip`, a folder extracted from one, or a folder holding many of those — WHERE IT LIES: nothing
is unpacked, renamed or repacked, and a game ships the bundles it downloaded. Its consumer
guide is `src/CodeBrix.Platform.GameEngine.KenneyAssets/AGENT-README.txt`, which ships inside
the package.

```csharp
KenneyGameAssetProvider kenney = Engine.Instance.UseKenneyAssets(
    "assets/kenney_puzzle-pack.zip", "assets/kenney_sci-fi-sounds.zip");

Tilesheet sheet = Engine.Instance.Managers.AssetProviders.LoadTilesheet(
    "kenney:puzzle-pack/Spritesheet/spritesheet_default");
Frame ball = sheet["ballBlue", 0, 0];
```

* Public surface, six types: `EngineKenneyAssetsExtensions` (`UseKenneyAssets`, in two
  overloads), `KenneyAssetsOptions` (`Sources`, `ProviderId`, `RecursiveFolders`,
  `IgnoreUnreadableSources`), `KenneyGameAssetProvider`, `KenneyPackSummary`,
  `KenneyAssetProperties` (the descriptor property names) and `TiledMapParseException`.
  Everything else — the archive readers, the classifier, the atlas and Tiled parsers, the
  materializers, the glTF reader and the model rasterizer — is internal: transformation logic
  belongs in the provider, and a consumer works in the engine's own types.
* Asset keys are `<providerId>:<pack-slug>/<path-inside-the-pack>`, matched case-insensitively,
  with the extension dropped for an asset that can be materialized and kept for one listed for
  discovery only (a Kenney model ships as `.glb`, `.fbx`, `.obj` and `.mtl`, which would
  otherwise be four files wanting one key). The pack slug comes from the pack's licence title
  line. The key is also the engine registry key of the materialized object.
* Materializes images (a whole-image tile, plus an optional `"grid"` region), sprite atlases
  (one named single-tile region per frame, addressed `sheet["ballBlue", 0, 0]`), audio, fonts,
  SVG (rasterized at load time), Tiled `.tmx` maps (into scene layers of a scene the caller
  owns, one tilesheet per tile set keyed `<map key>#<tile set name>`, flip bits pre-baked as
  extra regions, object layers handed back as data) and glTF models (both contract routes: data
  and pre-rendered sprite sheets).
* Registration is explicit and idempotent, with no module initializer: nothing happens until a
  game calls `UseKenneyAssets`, and calling it again ADDS sources to the provider already
  serving that identifier. An unreadable bundle is a warning rather than a failed start-up by
  default, and `KenneyGameAssetProvider.Warnings` says what could not be done exactly.
* Its one direct third-party dependency is `CodeBrix.Graphics3D.Gltf2.MitLicenseForever`; the
  model sprite renderer is a pure-managed software rasterizer, so a pre-rendered model sheet
  needs no GPU and works headless.
* Kenney's content is CC0 and is not part of the package: the package is the reader.

### Loading and authoring

* New `FontManager.LoadFromStream(key, Stream)` and `FontManager.LoadFromBytes(key, byte[])`
  register a font whose bytes come from an archive, with the same replace semantics as
  `LoadFromFile`; non-seekable streams are supported.
* New `SvgResourceManager.LoadFromStream(key, Stream)`; `SvgResource.Load(Stream)` is now public.
* `Tilesheet.GetRegion(name)` is backed by a case-insensitive name index instead of a linear
  scan, so a sheet carrying hundreds of single-tile regions resolves a region in constant
  time. The public API is unchanged.
* New `TilesheetDefinitionValidator` (`...Drawing.Tilesheets.GTS`):
  `Validate(TilesheetDefinition, int? imageWidth = null, int? imageHeight = null)` returns
  authoring diagnostics without creating runtime tiles or decoding the source image, and
  `GridSize(TilesheetRegionDefinition)` exposes the frame-grid arithmetic (tile size plus tile
  padding per frame, region margin removed from the area first). Negative collision
  adjustments stay legal — they expand the collision area — so only inverted geometry is
  reported.
* New `AssetsFile.Validate(string path, string? password = null, bool testData = true)`: a
  strict bundle check for authoring tools. It rejects an entry key that cannot be parsed,
  names an undefined asset type or an empty asset name, or collides with another entry, and
  then runs the archive integrity check (`testData: false` skips payload verification but
  still checks the keys). Run-time loading stays permissive, and the inspected bundle is not
  added to `AssetsFile.AllAssetsFiles`.

### Drawing, sprites and audio

* `TextBlock.SetPadding(float horizontal, float vertical)` and `TextBlock.SetSize(Size size)`:
  fluent setters that validate their arguments, invalidate the cached line layout and refresh.
  `SetSize` keeps the current location and writes `ScreenBounds` in View mode, `WorldBounds`
  otherwise. Both throw `ArgumentOutOfRangeException` on negative / non-finite padding or a
  non-positive size.
* `DirectDrawingBase.CancelReveal()`: stops an in-flight `RevealTo` animation at its current
  progress and returns the drawing for chaining — the reveal counterpart to `CancelFade`.
* `Sprite.VisualBoundsChanged` (`Action<Sprite>?`): raised whenever a sprite's rotated visual
  bounds change (rotation, alignment, offset, frame size) after the affected region is queued
  for refresh; cleared on disposal alongside `SpriteMoved` and `Disposing`. Use `SpriteMoved`
  for coordinate changes.
* `AudioResource.PlaybackSpeed`: a playback-rate multiplier, 1.0 by default, clamped to
  `AudioResource.MinimumPlaybackSpeed` (0.25) — `AudioResource.MaximumPlaybackSpeed` (4.0);
  NaN is rejected with `ArgumentOutOfRangeException`. Pitch follows speed (resampling, not
  time-stretching). It applies to every loaded resource, may be changed while playing, is
  carried over by `AudioResourceManager.Clone`, and is saved and restored with the engine
  state. A `TryPlaySfx` trigger plays through the shared voice pool rather than the resource's
  own graph, so it keeps the recorded speed; music tracks keep their own `Speed`.

---

## FIXES

### Rendering and presentation

* A view-mode direct drawing created immediately after pinning a render resolution was clipped
  to the resolution the surface had before the request. The logical resolution is now
  published the moment it is **requested**, not when the rendering thread reallocates the
  backbuffer, and views are sized from it.
* A GPU-tier surface on a head that never supplies a GPU context ignored an explicit
  resolution request and stayed at its construction size; its temporary CPU raster surface now
  honours the request.
* Releasing a GPU surface left the backbuffer pointing at the context whose surface had just
  been disposed, so a later re-initialization with that same context short-circuited and the
  backbuffer stayed on its CPU fallback.
* `RenderSurfaceHost` unsubscribes its adapter-resized and backbuffer-size-changed handlers on
  dispose.
* GL-thread-rendered (GPU) surfaces no longer compute sprite visual bounds or maintain a dirty
  rectangle: they always present the full backbuffer.
* Tile sorting captures each tile's sort values once per render pass instead of re-reading the
  virtual `IsPositionFixed` / `DrawLocationWorld` / `Overhang` / `SceneLayerCoordinates`
  members on every comparison. Outside a render pass the historical live-value semantics are
  unchanged. A drawable that moves a tile in the middle of a sort is not seen by that sort — by
  design.
* The post-tile overlay pass (fog / grid lines / collision boxes) exits early for tiles that
  ask for no overlay, and draws orthogonal grid outlines as a rectangle rather than a
  transformed polygon.
* `CyclesPerSecondCalculatedEventArgs.GpuFps` is documented as the GPU *presentation* rate,
  which can differ from `NetCPS` where the platform's presentation cadence is independent of
  the engine's.
* Rasterizing an SVG now honours the origin of the document's own bounds, not just their size,
  so vector art that an exporter left away from (0, 0) comes out whole instead of cropped — and
  art at negative coordinates comes out at all rather than as an empty bitmap. Art anchored at
  the origin rasterizes exactly as before. `SvgResource.Rasterize` carries the fix, so
  `SvgResourceManager` and `DirectSvg` pick it up too.

### Audio

* The rate/speed stage no longer drops the final frame of a converted sound, and no longer
  refuses to read after the source has been rewound. Looping and seeking therefore work for
  converted resources — a looping clip used to stop and restart in silence — and a
  `SoundChannel` replayed after its clip ran to the end sounds again instead of staying
  silent. At the default speed the stage is a pass-through: identical samples, including the
  final frame, and no read-ahead, so `AudioResource.CurrentTime` still reports the position
  that has actually been handed to the output.
* Audio load failures clean up after themselves: a failed `LoadFromFile` / `LoadFromStream` /
  `LoadFromPcm` / asset-pack load releases the reader, the stream under it, the temporary file
  it wrote and any partially built resource, registers no key, and still rethrows the original
  exception. A failed construction no longer strands a voice on the shared output.
* Loading assets from an engine assets file no longer leaks the per-entry stream.
* The assets-file load and save log messages no longer include the file path.

### Engine and scenes

* `EffectsManager` no longer times its first frame from host construction. The first
  engine-driven `Update(tick)` records the baseline and returns, so an effect started before
  the first frame can no longer be burst to completion by the host-creation-to-first-frame
  gap, and a manager that has never been updated is left alone by the global-pause rebaseline.
* Loading a `.gts` definition whose `Regions` array carries a null entry throws
  `InvalidDataException` ("GTS Regions cannot contain null entries.") instead of failing later
  with a `NullReferenceException`.
* A null sprite entry encountered while refreshing collision profiles on layer add is skipped
  instead of throwing.
* `EngineConfiguration.StartInitializationWaitTimeout` clamps to a value `TimeSpan` can
  actually express, so a very large timeout cannot overflow.
* Every sample's `.Core` project pinned an older `Microsoft.Extensions.Logging.Console` than
  the engine, so restoring any sample head failed with a package-downgrade error. All nine are
  back in step.
* The core test suite's Opus test no longer depends on the order its siblings run in. Audio
  codec registration is process-wide and permanent, so the one-per-process observation that
  `.opus` is unsupported before `CodeBrixAudioOpus.Register()` is now taken by a module
  initializer, which runs before any test in the assembly. Test-suite only; nothing a consumer
  can observe.

---

## SAMPLES AND REPOSITORY

* **`samples/Platformer.Brix`** gained angry-mushroom enemies: one spawns with the level and
  another every 10 seconds on solid ground near the player. Mushrooms walk towards the player
  and fall into pits. Side or underside contact returns the player to the start, like the
  spikes; landing on a mushroom's head while descending flattens it and bounces the player. A
  flattened mushroom holds its pose briefly, fades over its 16-frame fade strip and is
  disposed. Restarting clears the enemies and resets the spawn timer; winning clears them. Its
  generated tilesheet grew from 8 to 26 frames. The sample is also the pixel-art reference for
  presentation filtering: it sets `RenderScalingFilter = NearestNeighbor` in
  `OnEngineInitialized` (a value set in `OnSceneBound` is discarded when the engine loads its
  configuration), alongside the tile filter it already set on the backbuffer.
* **`samples/CoordinateTest`** is now a wrapping demonstrator: both scene layers are
  orthogonal, the first sets `WrapHorizontally = true`, and the two on-screen readouts are
  pinned to fixed positions. Panning left shows the wrapped layer continuing while the
  parallax layer beside it stops.
* **`samples/Spot.Brix`**, **`samples/SpaceDuel.Brix`** and **`samples/GpuRender`** anchor
  their overlays from the Backbuffer size instead of the surface size, which is the pattern
  every game should follow now that a resize is presentation-only.
* **`samples/ParticleTest`** lays its scene out from the pinned 1280x720 it already declared,
  and its campfire hit box calls `AdapterPxToScreenPx` instead of re-deriving the aspect fit.
* **`samples/Slider`** builds its puzzle from the Backbuffer size rather than the control's
  actual size, so a grid-size change after a window resize lays the board out correctly.
* **`samples/SoftRender`** (Mode B) and **`samples/MusicDemo`** are unaffected by the
  presentation work: Mode B owns its own fit and its own pointer mapping.
* **`samples/KenneyAssetsDemo`** is new: the worked example of the Kenney asset package, in the
  usual sample shape (a `.UI` shared project, a `.Core` library, a `.Game` library and the
  LinuxX11, Win32Skia and MacOS heads, with its own `.slnx`). It ships real CC0 Kenney bundles
  beside the executable and registers them with one `UseKenneyAssets` call, then shows a Tiled
  map imported into scene layers, a PRE-RENDERED 3D character driven by the keyboard that faces
  its direction of travel and animates through `Cycle` / `FrameSequence`, atlas sprites as
  collectibles, a pick-up sound, HUD text in a Kenney text font, one rasterized SVG icon, and an
  on-screen note listing the registered packs. It logs one line per loading step, which is the
  shape a game's own asset loading wants.
* The repository now holds **four** projects and **four** test suites: the new
  `src/CodeBrix.Platform.GameEngine.KenneyAssets` and
  `tests/CodeBrix.Platform.GameEngine.KenneyAssets.Tests` are both in
  `CodeBrix.Platform.GameEngine.slnx`, and the test suite carries five real CC0 Kenney bundles
  as fixtures (credited in the `FIXTURES-LICENSE.txt` beside them) plus an opt-in scan over a
  whole Kenney collection, gated by the
  `KENNEY_ALLIN1_DIR` environment variable. `MAINTAINER-README.txt` covers running it and the
  publish hand-over for the new package.
* New test coverage: layer wrapping (period vectors, seams, camera, rendering, save/load),
  viewport scaling and letterbox pointer mapping, engine initialization and the fixed-step
  accumulator, the host shutdown order, pointer coordinate mapping, the asset-provider
  registry (including both model routes and the fuller tile-map object data), the model data
  types, the tilesheet-definition validator, `AssetsFile.Validate`, `AudioResource`,
  `SoundChannel`, SVG rasterizing from a document's own origin, and the post-tile overlay pass.

---

## UPSTREAM

Upstream changes merged into this port, by commit:

| Commit | Title |
| --- | --- |
| `0de779f1c` | fix(core): harden engine initialization safety (#336) |
| `f8b4a2fa2` | fix(core): prevent engine dispose self-deadlock (#338) |
| `33fce6521` | refactor(core): use fixed-step simulation timing (#305) |
| `03a9c7c57` | fix(core): warm effects manager on first update (#337) |
| `582bc21dc` | refactor: move sprite rotation into Sprite.Draw (#345) |
| `6a71f5958` | feat(blazor): add WebGL GPU rendering path (#307) — core performance work and the GL presentation helpers only |
| `ed382d601` | feat(scene): implement SceneLayer wrapping (#359) |
| `119e6bd38` | feat(rendering): decouple backbuffer resolution from adapter presentation (#335) |
| `507a6d817` | feat(cli): add project health, package management, asset validation, and serving (#332) — the definition validator and `AssetsFile.Validate` only |
| `9aece48ce` | refactor(audio): separate portable contracts from platform backends (#352) — `StopAndWait`, playback speed and load-failure hygiene only |
| `9fdb115cc` | feat: finish core HUD widgets for #116 (#344) — `TextBlock.SetPadding` / `SetSize` only |
| `28384c9a5` | feat(widgets): add interactive widget controls (#346) — `Sprite.VisualBoundsChanged` only |
| `cb7bf1af9` | feat(widgets): add nested menus and keyboard accelerators (#365) — `CancelReveal` only |
| `1ea80d9d6` | feat(demos): add angry mushroom enemy logic (#343) |
| `bfcc1fea3` | v2.6.0 — the revision this port now tracks |

Deliberately **not** vendored: the upstream widget toolkit, its Blazor / WebGL rendering path,
its NAudio-backed audio backend packages, and its command-line and tilesheet-authoring
tooling. The port keeps its own System.Text.Json save/load path, its CodeBrix.Audio-based
audio stack, its global pause and its GPU tier.

---

## OPEN FOLLOW-UPS

1. **`DirectDrawingBase` bakes the viewport clip into a view-mode drawing at construction.**
   The constructor intersects the drawing's `ScreenBounds` with `view.Viewport.TargetRectPx`
   and never revisits it, so any later viewport change leaves the drawing clipped to the old
   one. This predates the presentation work — it bit every window enlargement before the
   decoupling — and it still bites surfaces that change their viewport afterwards
   (`TrackAdapterSize = true`, or a `RenderScale` change). The render pass already clips to
   the viewport, so the constructor clip is redundant; removing it is a small but
   behaviour-visible change across the direct-drawing tests, so it is held for its own item.
2. **Carried over from the 2026-09-03 notes, still Jeremy's to decide:**
   * The `ships.png` art was removed from `samples/SpaceDuel.Brix` because its rights were
     unconfirmed; the procedural sheet is the shipped art. Restore it only if the rights are
     cleared.
   * The upstream engine is named only in `THIRD-PARTY-NOTICES.txt`. Whether `AGENT-README.txt`
     prose should name it as well is still open.
   * `TilesheetDefinitionSerializer.Save(path, tilesheet)` keeps upstream parity and mutates a
     bitmap-only tilesheet — this one was **decided on 2026-09-17**: the behaviour stays, and
     it is documented in `AGENT-README.txt`.
