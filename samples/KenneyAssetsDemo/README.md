# KenneyAssetsDemo

A small, self-contained sample where everything on screen comes out of two Kenney asset bundles,
loaded through `CodeBrix.Platform.GameEngine.KenneyAssets` and the engine's own asset-provider
registry. It is documentation by example for that package: the game logic is deliberately tiny, and
the interesting part is the loading code and the report it prints.

## Play

* Arrow keys or `W` / `A` / `S` / `D`: walk the character
* Walk into a gem to pick it up

There is nothing to win. The character walks anywhere on the map — the demo has no collision, so that
the asset loading stays the only thing worth reading.

## What it demonstrates

One call registers the bundles:

```csharp
KenneyGameAssetProvider provider = Engine.UseKenneyAssets(
    Path.Combine(AppContext.BaseDirectory, "assets", "kenney", "simulated_bundle.zip"),
    Path.Combine(AppContext.BaseDirectory, "assets", "kenney", "kenney_puzzle-pack-1.zip"));
```

After that every asset is addressed by key through `Engine.Managers.AssetProviders`, and the demo
takes one of each kind:

* **Tiled map into scene layers** — `ImportTiledMap` adds one `SceneLayer` per tile layer of the map
  to the scene the demo creates, and registers one tilesheet per tile set the map references under
  `<map key>#<tile set name>`. The map's two tile sets use different tile sizes and it uses flipped
  tiles, so the import shows off the tile-offset and pre-baked flip-variant rules. The demo puts a
  layer of its own on top of the imported ones for the character and the gems, which is all there is
  to mixing imported content with content a game builds itself.
* **A 3D model pre-rendered into sprite frames** — `LoadTilesheet` on a `.glb` key with
  `TilesheetMaterializeOptions.ModelRender` renders the animated glTF character from eight camera
  directions, for its `idle` and `walk` animations. The output layout is fixed: one uniform-grid
  region per animation plus `rest` for the rest pose, COLUMNS = frames and ROWS = directions, so a
  frame is `sheet["walk", frame, direction]`. The demo turns each row into an engine `Cycle` over a
  `FrameSequence` and picks the row that faces the way the character is walking.
* **Sprite atlas frames** — `LoadTilesheet` on an atlas key gives one tilesheet with a 1x1 region per
  frame, named after the frame with its image extension stripped, so the gems are
  `sheet["element_blue_diamond_glossy", 0, 0]` and friends.
* **A sound** — `LoadAudio` on an `.ogg` key, played when a gem is picked up.
* **Fonts** — `LoadFont` gives an `SKTypeface` the heads-up display draws with. Two Kenney text fonts
  are used, plus one of Kenney's input-prompt fonts, which is an ICON font: its glyphs live in the
  private use area and it carries no letters at all, so it draws a prompt and never a word.
* **A rasterized SVG** — `LoadTilesheet` on an `.svg` key rasterizes the document into a bitmap
  tilesheet, shown as the badge in the bottom corner.
* **The catalog** — `provider.Describe()` and `provider.Packs` are what the on-screen note listing the
  registered packs is built from, and what the start-up report counts.

Beyond the asset provider it also exercises the ordinary engine things a sample needs: a pinned render
resolution with the world shown at a 2x viewport zoom (so 18-pixel tile art doubles cleanly while the
screen-space heads-up display keeps the full resolution to draw text into), nearest-neighbour tile and
presentation filtering for pixel art, integrated sprite velocity driven from the keyboard poller, and
view-bound `DirectRectangle` / `TextBlock` / `DirectImage` overlays.

## The start-up report

Every loading step writes one console line with a fixed prefix, so a run can be checked without
looking at it:

```
[KenneyAssetsDemo] packs registered as 'kenney': ...
[KenneyAssetsDemo] assets described: ...
[KenneyAssetsDemo] sound loaded: ...
[KenneyAssetsDemo] font loaded: ...
[KenneyAssetsDemo] character sheet: ...
[KenneyAssetsDemo] sprite atlas: ...
[KenneyAssetsDemo] vector rasterized: ...
[KenneyAssetsDemo] map imported: ...
[KenneyAssetsDemo] map tilesheet: ...
[KenneyAssetsDemo] animation cycles: ...
[KenneyAssetsDemo] collectibles placed: ...
[KenneyAssetsDemo] controls: ...
[KenneyAssetsDemo] READY
```

A problem the demo worked around says `WARNING`; a problem it could not work around says `FAILED` and
the exception goes on where it was going. `READY` is the last line of a good run.

## Running it

```
dotnet run --project samples/KenneyAssetsDemo/src/KenneyAssetsDemo.LinuxX11/KenneyAssetsDemo.LinuxX11.csproj
```

Swap the head project for `KenneyAssetsDemo.Win32Skia` or `KenneyAssetsDemo.MacOS` on the other
platforms. The sample carries its own `KenneyAssetsDemo.slnx`; like every other sample it is not part
of the repository's product solution.

## Where the assets came from

The two `.zip` files under `src/KenneyAssetsDemo.Core/assets/kenney/` were created and distributed by
**Kenney (www.kenney.nl)** and are released under the Creative Commons Zero (CC0 1.0 Universal) public
domain dedication. CC0 needs no attribution; Kenney is credited because crediting the source of an
asset is the house style of this repository. Each bundle carries Kenney's own `License.txt` inside it,
and the demo reads that file — the licence title is what names the pack and what the pack slug in every
asset key is made from. See `src/KenneyAssetsDemo.Core/assets/kenney/KENNEY-ASSETS.txt` for what each
bundle holds.

The bundles ship unopened, exactly as they are downloaded, and are copied next to the executable at
build time. Reading a downloaded bundle where it lies is the whole point of the package this sample
demonstrates.

## Layout

```
KenneyAssetsDemo.slnx
src/libs/KenneyAssetsDemo.Game/    the demo: the game host plus its asset keys, log and helpers
src/KenneyAssetsDemo.Core/         view model, host-builder helper and the Kenney bundles
src/KenneyAssetsDemo.UI/           shared App.xaml and Views/MainPage.xaml
src/KenneyAssetsDemo.LinuxX11/     executable heads - one platform package each
src/KenneyAssetsDemo.MacOS/
src/KenneyAssetsDemo.Win32Skia/
```
