================================================================================
AGENT-README: CodeBrix.Platform.GameEngine.CardsAndDice
A Guide for AI Coding Agents — CONSUMING the CodeBrix.Platform.GameEngine.CardsAndDice.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Platform.GameEngine.CardsAndDice adds cards, decks, piles, dice and an
animated TABLETOP to the game engine. Target: .NET 10 or later.

Decks of any size, hands and piles, n-sided and symbolic dice, shuffling,
sequential dealing, flipping, rolling, selection and drag-and-drop all animate
on a table, while every outcome is decided once, up front, by the logic. The
logical objects (Deck, CardPile, Die) are ordinary classes that can be used and
unit-tested without a table, an engine or a display.

ONE PACKAGE, TWO ASSEMBLIES. The package carries
CodeBrix.Platform.GameEngine.CardsAndDice.dll (the API) and
CodeBrix.Platform.GameEngine.CardsAndDice.Assets.dll, which EMBEDS the artwork
— traditional and simple playing cards, historical tarot cards, card backs,
dice, symbols — plus optional sound effects and the asset catalog, as manifest
resources. There is no separate Assets package, no contentFiles, no extraction
step, no download and no loose artwork: nothing depends on the working
directory.

OTHER PACKAGES FROM THE SAME REPOSITORY
---------------------------------------
  CodeBrix.Platform.GameEngine.MitLicenseForever — the game engine itself
  (engine core + CodeBrix.Platform host layer). License: MIT. It is a hard
  dependency of this package; see AGENT-README.txt in the repository root for
  everything about scenes, views, draw lists, input, audio and the engine
  lifecycle. THIS file covers cards and dice only.

  CodeBrix.Platform.GameEngine.KenneyAssets.MitLicenseForever (Kenney asset
  bundles), CodeBrix.Platform.GameEngine.GeneratedMusic.MitLicenseForever
  (generated music) and CodeBrix.Platform.GameEngine.Sdl2.ZlibLicenseForever
  (gamepads) are optional add-ons unrelated to this one; each carries its own
  AGENT-README.txt.

INSTALLATION
============
NuGet package ID (note the license suffix):

    CodeBrix.Platform.GameEngine.CardsAndDice.MitLicenseForever

    dotnet add package CodeBrix.Platform.GameEngine.CardsAndDice.MitLicenseForever

Reference it from the game's shared Core (or game library) project. The
assemblies are CodeBrix.Platform.GameEngine.CardsAndDice and
CodeBrix.Platform.GameEngine.CardsAndDice.Assets, and the namespaces are
CodeBrix.Platform.GameEngine.CardsAndDice[.*] (WITHOUT the license suffix).

License: MIT — the code in this package. The embedded artwork and sounds are
CC0, public domain, or original MIT-covered artwork; see ASSET CATALOG AND
LICENSING below.

NuGet dependencies (pulled in automatically, listed by id):
    CodeBrix.Platform.GameEngine.MitLicenseForever   -- the engine + host

The engine dependency is an ORDINARY PackageReference on a published version:
this package is versioned and published INDEPENDENTLY of the engine package and
the two do NOT share a version number. Take the latest of each.

The add-on uses the published engine; it does not replace engine
initialization. When consuming the SOURCE through a ProjectReference instead of
the package, reference the Assets project explicitly as well if the game uses
AssetCatalog: the main project's reference to it is private (it is packed into
the main package). NuGet consumers need only the one package reference.

KEY NAMESPACES / USINGS
=======================
    using CodeBrix.Platform.GameEngine.CardsAndDice;          // UseCardsAndDice (engine extension)
    using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;    // cards, piles, decks, built-in decks, composer
    using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;     // Die, DieFace
    using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;   // CardLayout(s), CardPose, OccupiedDropRule
    using CodeBrix.Platform.GameEngine.CardsAndDice.Table;    // CardsAndDiceTable and its table-side types
    using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;   // AssetCatalog, AssetEntry
    using System.Drawing;                                      // Rectangle (table bounds)
    using System.Numerics;                                     // Vector2 (positions)
    using SkiaSharp;                                           // SKRect (area bounds)

Which type lives where:
    .CardsAndDice          EngineCardsAndDiceExtensions (UseCardsAndDice)
    .CardsAndDice.Cards    CardDefinition, Card, CardPile, Deck, BuiltInDecks,
                           CardComposer
    .CardsAndDice.Dice     Die, DieFace
    .CardsAndDice.Layout   CardLayout, CardLayouts, CardPose, OccupiedDropRule
    .CardsAndDice.Table    CardsAndDiceTable, TableArea, TableDie,
                           CardSelectionMode, DealStyle, DealAnimation,
                           DealSequence, ArtworkPreparation
    .CardsAndDice.Assets   AssetCatalog, AssetEntry (in the Assets assembly)

A game that never touches the engine (rules code, tests) needs only .Cards,
.Dice and .Layout.

QUICK START
===========
After the engine, scene, host and mouse adapter are initialized:

    var table = engine.UseCardsAndDice(host, view, new Rectangle(0, 0, 1280, 800));
    var deck = BuiltInDecks.PlayingCards();
    var hand = new CardPile("Hand");
    table.AddArea(deck.DrawPile, new SKRect(30, 200, 230, 530), CardLayout.Stack);
    table.AddArea(hand, new SKRect(270, 200, 1100, 530), CardLayout.Fan);
    table.AddDie(new Die(20), new Vector2(650, 650));
    deck.Shuffle();
    table.Deal(deck, hand, 5);
    table.Roll();
    table.CardClicked += card => card.IsSelected = !card.IsSelected;

UseCardsAndDice automatically hooks pause-safe engine time, drawing and mouse
input. There is also a scene-layer overload which transforms picking through
the selected view. Mutate the table on the engine thread, or before starting.
Dispose after rendering stops; engine.Disposing automatically does this.
The table owns cached SKImages returned by Image(key): do not dispose them.

CUSTOM CARDS AND MULTIPLE DECKS
===============================
CardDefinition holds the definition key, display name, face/back artwork keys,
value and optional Data. Deck creates physical Card instances, each with a
unique Id, Deck origin, Pile, face state, rotation and selection. Definition
keys can repeat; physical identity is never inferred from artwork or rank.
There is no fixed deck size. An empty deck is valid; copies creates repeated
packs. Multiple decks can share a table and mix into a pile. Deck.Reset gathers
only that deck's instances, even from mixed hands/discards, and resets state.

    var svg = CardComposer.Compose("Fire", "symbols/original/fire.svg", 6,
        "An elemental token", accent: "#e69a70");
    table.RegisterSvg("my/fire", svg);
    var custom = new Deck(new[] {
        new CardDefinition("fire", "Fire", "my/fire", value: 6)
    }, copies: 12);

Custom backs use the same registered-artwork keys as faces:

    table.RegisterSvg("my/back", yourBackSvg);
    var playing = BuiltInDecks.PlayingCards(back: "my/back", jokers: true);
    var tarot = BuiltInDecks.Tarot(back: "my/back");

For custom definitions, pass back: "my/back" to CardDefinition. Omit back to use
royal (custom/playing cards) or celestial (tarot). The bundled backs share a
public-domain Star of Ishtar medallion, recorded in THIRD-PARTY-NOTICES.txt.

RegisterSvg accepts text or a caller-owned stream and prepares its image
immediately, reusing the parsed SVG. Supply trusted local artwork. CardComposer
uses bundled symbols and escaped title/description text; dynamic text uses the
rendering host's sans-serif font. Bundled faces and symbols are self-contained
vector art, and the simple cards' lettering is vector paths.

PILE AND DECK RULES
===================
CardPile.Cards is top-first and read-only. Add transfers ownership, Draw removes
the top or returns null when empty, Collect transfers all, Cut rotates the top
N under the remainder, Shuffle uses unbiased Fisher-Yates. Deck.Draw/Deal can
place cards into any target pile (Deal forbids the deck's own draw pile).
Deal returns however many remain; it does not silently recycle discards.
Deck.Discard checks deck ownership. Deck.Random and optional seeds support
repeatable game tests; they are ordinary System.Random, not gambling security.

PRESENTATION AND INTERACTION
============================
Layouts: Stack, Row, Fan, Grid, Circle, Manual. Bounds/card sizes are adjustable.
ManualPoses maps physical card IDs to CardPose centers/angles. Layout bounds are
placement bounds, not clipping rectangles: choose sizes that fit your scene.

Shuffle splits the deck into two packets, riffles individual cards together,
squares the stack, and puts it down. It lasts ShuffleDurationSeconds, the
measured length of the embedded shuffle sound. Unlike other motions it uses
real seconds regardless of AnimationSpeed, so the sound and action finish
together. Wait for AnimationsCompleted before dealing if you want a
shuffle-then-deal sequence; the CardsAndDiceDemo sample demonstrates this
without blocking the engine thread. ReducedMotion skips both the shuffle wait
and its long sound.

Shuffle, Deal, Flip, Rotate and Roll animate; ReducedMotion completes motion
without changing results, AnimationSpeed changes presentation only. SoundEnabled
is opt-in and loads embedded streams through the engine's existing audio system.
A sound failure disables sound and logs the failure; gameplay continues.

Pointer supports press/move/release and inspect in table coordinates. Engine
wiring handles mouse; a custom touch/controller adapter can call Pointer.
Click raises CardClicked; right-click raises InspectRequested. Games supply their
own inspection UI. Dice clicks toggle IsHeld and raise DieClicked. Drag preserves
the grab offset; CancelDrag returns the visual without transferring ownership.
InputEnabled disables interactions for modal UI; DragEnabled leaves clicks active.
TableArea.AcceptsDrops and CanDrop control acceptance. For SingleCard areas,
OccupiedRule chooses Reject, Stack, Swap, or Displace into DisplacementPile.
CardDropped fires after successful transfer. AnimationsCompleted fires when the
whole table settles; it is not a per-card completion callback.

For headless rules/custom rendering, create CardsAndDiceTable directly, call
Update(seconds), Draw(yourDrawList), then publish that list yourself. DrawList
positions are centers. CompleteAnimations is useful in tests and reduced-motion
workflows. Logical outcomes are chosen once at Roll; animation consumes no RNG.

SELECTION AND DRAGGING
----------------------
Selection and dragging need only these settings:

    table.SelectionMode = CardSelectionMode.Multiple; // or Single
    table.CanSelect = card => card.Pile == p1.Pile;    // optional game rule
    table.CanDrag = card => card.Pile != deck.DrawPile;

Dragging is enabled by default. A successful drop into a Manual area saves the
released center and angle, including a move within that same area. Other layouts
arrange the card automatically. Selection highlights are built in. Multiple
selection toggles cards independently; a drag moves the grabbed card. None (the
default selection mode) leaves CardClicked behavior entirely to the game.

SEQUENTIAL DEALS AND FREE PLACEMENT
===================================
Register the draw pile and each receiving pile as areas, then:

    var p1 = table.AddArea(new CardPile("Player 1"), new SKRect(50, 200, 400, 500));
    var p2 = table.AddArea(new CardPile("Player 2"), new SKRect(850, 200, 1200, 500));
    table.Shuffle(deck.DrawPile);
    var sequence = table.DealRoundRobin(deck, new[] { p1, p2 }, cardsPerPlayer: 5);

The queued sequence waits for shuffling, then deals to p1, p2, p1, p2, etc.
Each departure draws the actual top card at that time, moves it into the target
pile, and flies it from the source pose to its layout position. Faces are hidden
in transit and physically flipped after landing unless faceUp:false. The reveal
takes .3 seconds; the next departure and CardDealt wait for it to finish. No
game timers, background tasks or async UI callbacks are needed. Update drives
the sequence with pause-safe time. ReducedMotion/CompleteAnimations finish it
immediately. AnimationSpeed scales travel and the pause between cards, but not
shuffle audio duration.

To land at an arbitrary board position and angle:

    var board = table.AddArea(new CardPile("Board"),
        new SKRect(20, 170, 1260, 650), CardLayout.Manual);
    table.DealTo(deck, board, new CardPose(new Vector2(700, 350), 25),
        animation: new DealAnimation { Style = DealStyle.Toss, Duration = .65 });

DealAnimation offers Slide, Arc (default), and Toss, plus Duration, Interval and
ArcHeight. DealTo queues one card; DealRoundRobin snapshots player order and
queues the requested rounds. DealSequence.Cards contains actual departures in
order, IsComplete/IsCanceled report progress, and CardDealt fires after each
landing/reveal. IsDealing includes queued and airborne cards. Source and
destination must already be displayed, and explicit poses require Manual
layout. These are scripted game actions and bypass drag/drop acceptance rules.
Cards are not reserved; exhaustion finishes remaining requests without
recycling discards. CancelDeals, Clear and Dispose cancel pending work,
retaining launched cards in their destination and leaving unlaunched cards in
the deck. Deal remains an immediate logical batch with simultaneous motion; use
the queued APIs for dealer-style cadence.

To leave each player's middle card hidden until clicked, queue three one-round
DealRoundRobin calls with faceUp true, false, true, and add a CardClicked
handler that Flips hidden cards.

DICE
====
Die.Traditional() supplies a six-sided die with ivory SVG faces and dark pips.
It tumbles through dotted faces while rolling and settles on its logical result.
new Die(n) keeps numbered faces and supports n >= 2. Custom DieFace values,
labels and optional SVG keys support symbolic or weighted-by-repeated-face dice.
Held dice are skipped. PercentileTens()/PercentileUnits() and PercentileResult
implement 01..100 with 00+0 interpreted as 100. Standard d4/d6/d8/d10/d12/d20
artwork is supplied; other side counts use generic numbered artwork. These are
animated 2D visuals, not 3D rigid-body physics. Result is null until rolled.

ASSET CATALOG AND LICENSING
===========================
AssetCatalog.All/Search/Open/ReadSvg expose keys, categories, friendly names,
source URLs/bundle paths, license identifiers, original hashes and embedded
hashes. Open returns a caller-owned stream. Keys are case-sensitive. Asset
categories are playing, tarot, backs, dice, symbols and sounds; list
AssetCatalog.All for the current set rather than relying on a fixed count.

BuiltInDecks.PlayingCards(theme: "traditional" or "simple", jokers: true)
supplies standard packs; Tarot supplies the 78 traced historical
Rider-Waite-Smith faces. Tarot values are illustrative 1..13 cycling through
catalog order, not traditional meanings; define your own CardDefinitions for
your game rules.

Kenney board-game/fantasy symbols and original elemental/alchemical geometry
provide hundreds of reusable images. THIRD-PARTY-NOTICES.txt (shipped inside the
package) records the exact provenance, permission and hashes of every embedded
file. Every embedded asset is CC0, public domain, or original artwork covered by
the package's MIT license, so there is no required artist-credit screen: retain
the normal MIT notices; the third-party CC0/public-domain art imposes no
in-game attribution step.

Historical tarot scans are traced vector paths, not JPEGs wrapped in SVG. They
retain the original paper/print texture and cost more memory than simple icons.

ARTWORK PREPARATION AND CACHING
-------------------------------
Rasterization is cached lazily at up to 480 x 840 per card, 144 wide per symbol;
cache lifetime is the table lifetime. Clear removes placements, not cached art.

For responsive loading, call table.BeginPrepareCards(deck.Cards) on the engine
thread after switching the visible mode. It returns ArtworkPreparation with
Progress, Completed, Total, IsComplete, IsCanceled and Error. Update installs
worker-rendered images in the cache; keep updating/drawing while it loads.
Wait for IsComplete with no Error/cancellation before starting the shuffle/deal.
Show progress and queue the requested action if the user clicks early; do not
automatically deal if they never clicked. Cancel or Clear on a mode change.
Dispose cancels without waiting for the worker; abandoned images are disposed.
Only isolated SVG rendering runs on the worker; table mutations stay on the
engine thread. Already cached images are skipped.

Alternatively call table.PrepareCards(deck.Cards) during a blocking loading
phase to cache both sides before gameplay. This is synchronous and can take time
for a large deck, but repeated calls reuse the same images. Shuffle and queued
deals also prepare the source pile before starting; batch Deal prepares
departing cards, and Flip prepares the destination side before starting its
timer. This keeps first-use SVG parsing/rasterization out of the middle of a
card reveal. Prepare every deck in advance when several decks animate together.

WORKING EXAMPLE
===============
samples/CardsAndDiceDemo in the repository is a complete game and asset gallery
with Linux X11, Windows Win32Skia and macOS heads; its README.md describes the
tables and controls. The test project
tests/CodeBrix.Platform.GameEngine.CardsAndDice.Tests covers ownership, seeded
rules, layout, input and animation, dealing, every embedded asset's hash, and
rendering every embedded SVG.

================================================================================
END OF AGENT-README
================================================================================
