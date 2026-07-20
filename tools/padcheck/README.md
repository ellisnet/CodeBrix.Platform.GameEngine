# padcheck

Interactive hardware check for the SDL2 gamepad add-in
(`CodeBrix.Platform.GameEngine.Sdl2`).

**Nothing in this folder runs as part of the library build, the test run, or the
NuGet packaging.** It is a run-by-hand utility, and it is deliberately NOT
included in `CodeBrix.Platform.GameEngine.slnx` so that the solution contains
only product projects.

## Why this exists

The unit tests cover everything that can be asserted without hardware: the
loader shim, the availability and reason logic, the button-name table ordering,
and the axis conversions (which are tested with fabricated raw values, since a
real stick cannot be asked to report a specific number on demand).

What they cannot cover is the part most likely to be wrong in practice:

* whether each **physical** button reports under the **correct name**
* whether pushing a stick **up** actually reports `Up`
* whether the **left** trigger is the one reporting as left
* whether a controller that **sleeps and wakes** is picked back up

Those need a person with a controller. This tool is how that is checked.

It drives the real `SdlGamepadManager`, so what it prints is what a game would
actually see. It is not a reimplementation.

## The two drive modes — run both

The engine has two hosting modes, and a controller has to work in both:

| Mode | Flag | What drives the pads |
| --- | --- | --- |
| Direct | *(default)* | `manager.Update()`, called by this tool on a frame cadence |
| Pump | `--pump` | `InputPump.PollNow()`, the Mode-B path a software-rendered game (Doom.Brix, Wolfenstein.Brix) actually runs |

**Run both, every time.** The direct mode is the more forgiving of the two: it
drove every hardware check of the original gamepad implementation, and because
it supplied the refresh itself it could not detect that *nothing* refreshed the
manager on the `InputPump` path. Gamepads were completely dead in Mode B —
frozen state, no events, no hotplug — while every hardware test passed. A check
that only exercises the driver it supplies itself cannot see a missing driver.

## Usage

```
cd tools/padcheck
dotnet run -- 45                  # direct mode; seconds to poll, defaults to 30
dotnet run -- 45 --pump           # InputPump (Mode-B) path
```

### Testing engine changes that are not published yet

This tool sits downstream of `CodeBrix.Platform.GameEngine.Sdl2`, which compiles
against the **published** engine package — so by default it cannot see engine
changes still in the working tree, and a local engine fix will appear to do
nothing. To test local engine source:

```
dotnet build ../../tools/padcheck/padcheck.csproj -p:UseLocalEngineProject=true
./bin/Debug/net10.0/padcheck 10 --pump
```

That flag swaps the engine `PackageReference` for a `ProjectReference` and
disables package generation; it is for local verification only and `Pack`
refuses to run with it set.

It prints the detected controllers and their SDL2 mapping string up front, then
stays quiet and reports only **changes**: button presses, engaged sticks,
triggers, and hotplug transitions. A summary follows at the end.

## Suggested sequence

With the controller **on**, during the polling window:

1. Press **A, then B, then X, then Y**, slowly and in that order.
2. Push the left stick straight **up**, hold, then straight **down**.
3. Click **both sticks inward**.
4. Press both **shoulders**, all four **D-pad** directions, **Start**, **Back**,
   **Guide**.
5. Squeeze **both triggers** fully.

Then, in a second run started with the controller **off**, power it **on**
partway through to exercise reconnect.

## What to look for

| Check | Expected |
| --- | --- |
| Face buttons | print in the same order you pressed them |
| Stick up | `dir=Up` with a **positive** Y |
| Stick down | `dir=Down` with a **negative** Y |
| Triggers | both reach `1.00`, rest at `0.00`, and L is L |
| `Buttons NOT seen` | `(none - all 15 confirmed)` |
| Hotplug off then on | a `1 -> 0` line, then a `0 -> 1` line |
| After reconnect | buttons still work; the `GamepadId` has **changed** |
| `Resting-glitch seen` | `False` |

The face-button order check is the important one. Controller mappings are not
sequential — an Xbox pad over Bluetooth reports A, B, X, Y as buttons `b0`,
`b1`, `b3`, `b4`, skipping `b2` — so an implementation reading raw joystick
indices would put X and Y on the wrong buttons while still looking plausible.
Pressing them in a known order is what catches that.

A stick magnitude above `1.00` is **expected**, not a fault: X and Y are each
clamped to `[-1, 1]` independently, so a corner reaches up to 1.41. The tool
prints a note when it sees this. Clamp or normalize before using `Magnitude` as
a movement-speed scalar, or diagonals will be faster than the cardinals.

## Note on the reconnect case

A reconnected controller gets a **new** `GamepadId`, because the id derives from
SDL2's joystick instance id and that is not reused while a device stays
connected. This is by design, but it means anything registered against the old
id — button monitoring on the engine's gamepad event poller in particular — has
to be registered again. Bluetooth controllers power themselves down after a few
minutes of inactivity, so this is normal operation rather than an edge case.
