# The Fortune Table

A playable CardsAndDice sample with seven tables: traditional playing cards,
minimal playing cards, historical tarot, two custom elemental decks, dice,
a paged gallery of all embedded SVG assets, and a three-player dealer table.

Deal, click cards to keep, then replace the others once or bank the score.
The opening deal waits for a 3.063-second split/riffle/square shuffle, matched to
the embedded Vorbis clip. Shuffle timing stays synchronized with sound even
when the speed control changes; reduced motion skips the shuffle wait.
Meet the displayed target to win. Dice work similarly: roll, click to hold,
then reroll once or bank. Start another round to play again. Dice choices
include five d6s, a polyhedral mix, five d7s and percentile dice.

The DEALER tab shuffles and deals three rounds around three players. Its other
button tosses one top card to a specified position/angle. Click cards to select;
drag them between hands or anywhere on the free board. The library handles the
queue, arrival events, selection and manual placement.

Drag cards between areas, right-click to inspect, and use Flip Selected to see
both faces. The footer controls sound, reduced motion, animation speed and reset.
Gallery images open in a large inspector. All artwork comes from the add-on's
embedded Assets assembly; the game has no loose asset directory.

From the repository root:

```sh
dotnet run --project samples/CardsAndDiceDemo/src/CardsAndDiceDemo.LinuxX11
```

Windows uses `CardsAndDiceDemo.Win32Skia`; macOS uses `CardsAndDiceDemo.MacOS`.
The shared game is in `src/libs/CardsAndDiceDemo.Game/FortuneTableGame.cs`.
The game uses an 800-unit-high table. Wider windows expand the table and reflow
the hand, dice, gallery and controls while keeping card proportions intact.
Narrow windows scale the minimum 1280 × 800 layout to fit. Resizing preserves the
current round, cards and dice.
The first build requires the .NET 10 SDK and published CodeBrix dependencies.

For a desktop integration walkthrough, set `CARDSDICE_WALKTHROUGH=1` when
launching the Linux head. It plays two actions in each game mode and each dice set, browses the gallery,
checks the finished rounds, writes `cardsdice-mode-N.png` to the system temp folder (`Path.GetTempPath()`, which honours `TMPDIR`), prints
`CARDSDICE WALKTHROUGH PASS`, and stops the engine. Close the window afterward.
This mode requires a graphical desktop; it does not replace unit tests.

Card values are deliberately simple demo rules: standard ranks score 1–13,
elemental cards their printed value, tarot cycles 1–13, and dice sum their faces.
Tarot scores carry no claim about traditional divination meanings.

Licenses and individual asset provenance are in the repository's
[THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). Kenney art/sounds and
Adrian Kennard's traditional cards are CC0; the historical tarot sources are
public domain; original geometric artwork is covered by the repository MIT
license. No artist-credit screen is required.

The 5 × D6 set uses traditional dotted dice; the other sets retain numbers.
DEAL 3 EACH deals face-up, face-down, face-up to each player. Face-up cards
flip after landing. Click the hidden middle card to reveal it with the same
flip animation; only revealed cards contribute to the displayed player score.

Card modes switch immediately and prepare missing artwork in the background.
Clicking Shuffle & Deal before preparation finishes shows a progress bar and
queues the action; it starts automatically when ready. Without a click, the
table waits. Switching modes or resetting cancels any pending deal. Subsequent
rounds reuse cached artwork, so card reveals do not block a flip.
