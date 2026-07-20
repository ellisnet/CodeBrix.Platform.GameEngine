// padcheck -- interactive hardware check for CodeBrix.Platform.GameEngine.Sdl2.
//
// Drives the REAL SdlGamepadManager, so that what it reports is what a game would actually see.
// It exists because the parts of gamepad support that matter most cannot be unit tested: whether
// each physical button reports under the right name, whether up is up, and whether a controller
// that sleeps and wakes is picked back up.
//
// TWO DRIVE MODES, because the engine has two hosting modes and a controller has to work in both:
//
//   (default)  DIRECT  -- calls manager.Update() itself on a frame cadence.
//   --pump     PUMP    -- assigns the manager to the engine and calls InputPump.PollNow(), the
//                         Mode-B path a software-rendered game (Doom.Brix, Wolfenstein.Brix)
//                         actually runs. Nothing here calls Update() directly.
//
// RUN BOTH. The direct mode is the more forgiving of the two: it drove every hardware check of
// the original gamepad implementation, and because it refreshed the manager itself it could not
// detect that NOTHING refreshed it on the InputPump path -- which left gamepads completely dead
// in Mode B while every hardware test passed. A check that only exercises the driver it supplies
// itself cannot see a missing driver.
//
// See tools/README.md for the suggested test sequence and what to look for.
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Input;
using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;

bool usePump = args.Any(a => string.Equals(a, "--pump", StringComparison.OrdinalIgnoreCase));
int seconds = 30;
foreach (string arg in args)
{
    if (int.TryParse(arg, out int parsed))
    {
        seconds = parsed;
        break;
    }
}

Console.WriteLine("=== CodeBrix.Platform.GameEngine.Sdl2 -- real controller check ===");
Console.WriteLine($"Drive mode          : {(usePump ? "PUMP (InputPump.PollNow -- the Mode-B game path)" : "DIRECT (manager.Update)")}");
Console.WriteLine();

bool started = SdlGamepadManager.TryStart(out SdlGamepadManager manager);

Console.WriteLine($"TryStart returned   : {started}");
Console.WriteLine($"IsAvailable         : {manager.IsAvailable}");
Console.WriteLine($"UnavailableCause    : {manager.UnavailableCause}");
Console.WriteLine($"UnavailableReason   : {manager.UnavailableReason ?? "(none)"}");
Console.WriteLine();

if (!manager.IsAvailable)
{
    Console.WriteLine("Gamepad support unavailable; nothing further to check.");
    manager.Dispose();
    return 1;
}

Console.WriteLine($"Controllers found   : {manager.ConnectedAdapters.Count}");
Console.WriteLine($"No-controller hint  : {manager.GetNoControllersHint() ?? "(none - a controller is connected)"}");
Console.WriteLine();

PrintControllers(manager);

if (usePump)
{
    // Exactly what a Mode-B game does: hand the manager to the engine and never touch it again.
    // From here on the loop only calls InputPump.PollNow().
    Engine.Instance.Input.GamepadManager = manager;
    Console.WriteLine("Manager assigned to Engine.Instance.Input.GamepadManager; driving with InputPump.PollNow().");
    Console.WriteLine();
}

Console.WriteLine($"Polling for {seconds}s at ~60fps. Press buttons and move the sticks.");
Console.WriteLine("(Only CHANGES are printed.)");
Console.WriteLine();

var seenButtons = new SortedSet<string>(StringComparer.Ordinal);
var pressedLastFrame = new HashSet<string>(StringComparer.Ordinal);

// Frames each controller has been seen for, keyed by GamepadId. Used to police the adapter's
// one-frame warm-up discard: the bogus full-deflection reading it exists to swallow happens
// immediately after a controller appears, so only the first few frames are suspect. Checking for
// it at any other time would flag a legitimate hard diagonal, which reads identically.
var framesPerController = new Dictionary<string, int>(StringComparer.Ordinal);
const int warmUpFramesToPolice = 5;

float maxLeftMagnitude = 0f, maxRightMagnitude = 0f;
float maxLeftTrigger = 0f, maxRightTrigger = 0f;
int frames = 0, hotplugEvents = 0;
int previousPadCount = manager.ConnectedAdapters.Count;
bool sawWarmUpGlitch = false;

var stopwatch = System.Diagnostics.Stopwatch.StartNew();
while (stopwatch.Elapsed.TotalSeconds < seconds)
{
    if (usePump)
    {
        InputPump.PollNow();
    }
    else
    {
        manager.Update();
    }

    frames++;

    if (manager.ConnectedAdapters.Count != previousPadCount)
    {
        hotplugEvents++;
        Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] HOTPLUG: controller count "
            + $"{previousPadCount} -> {manager.ConnectedAdapters.Count}");
        previousPadCount = manager.ConnectedAdapters.Count;
        PrintControllers(manager);
    }

    foreach (SdlGamepadAdapter pad in manager.ConnectedAdapters)
    {
        framesPerController.TryGetValue(pad.GamepadId, out int padFrames);
        framesPerController[pad.GamepadId] = ++padFrames;

        foreach (string button in pad.PressedButtons)
        {
            if (pressedLastFrame.Add(button))
            {
                seenButtons.Add(button);
                Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] BUTTON DOWN: {button}");
            }
        }

        pressedLastFrame.RemoveWhere(b => !pad.PressedButtons.Contains(b));

        var left = pad.LeftStick ?? default;
        var right = pad.RightStick ?? default;

        if (padFrames <= warmUpFramesToPolice && IsFullDeflection(left) && IsFullDeflection(right))
        {
            // BOTH sticks pegged in both axes at once, within a few frames of the controller
            // appearing. No hand does that; it is the pre-first-HID-report reading leaking through.
            sawWarmUpGlitch = true;
            Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] *** WARM-UP GLITCH LEAKED: "
                + $"L={left} R={right} on frame {padFrames} of {pad.GamepadId} ***");
        }

        maxLeftMagnitude = Math.Max(maxLeftMagnitude, left.Magnitude);
        maxRightMagnitude = Math.Max(maxRightMagnitude, right.Magnitude);
        maxLeftTrigger = Math.Max(maxLeftTrigger, pad.LeftTrigger);
        maxRightTrigger = Math.Max(maxRightTrigger, pad.RightTrigger);

        if (left.IsEngaged())
        {
            Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] LEFT STICK  {left} "
                + $"dir={left.Direction()} mag={left.Magnitude:0.00}");
        }

        if (right.IsEngaged())
        {
            Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] RIGHT STICK {right} "
                + $"dir={right.Direction()} mag={right.Magnitude:0.00}");
        }

        if (pad.LeftTrigger > 0.05f || pad.RightTrigger > 0.05f)
        {
            Console.WriteLine($"  [{stopwatch.Elapsed.TotalSeconds,5:0.0}s] TRIGGERS "
                + $"L={pad.LeftTrigger:0.00} R={pad.RightTrigger:0.00}");
        }
    }

    Thread.Sleep(16);
}

Console.WriteLine();
Console.WriteLine("=== SUMMARY ===");
Console.WriteLine($"Frames polled        : {frames}");
Console.WriteLine($"Controllers at end   : {manager.ConnectedAdapters.Count}");
Console.WriteLine($"Hotplug transitions  : {hotplugEvents}");
Console.WriteLine($"Buttons seen         : {(seenButtons.Count > 0 ? string.Join(", ", seenButtons) : "(none)")}");
Console.WriteLine($"Buttons NOT seen     : {DescribeMissingButtons(seenButtons)}");
Console.WriteLine($"Max left-stick mag   : {maxLeftMagnitude:0.00}");
Console.WriteLine($"Max right-stick mag  : {maxRightMagnitude:0.00}");
Console.WriteLine($"Max left trigger     : {maxLeftTrigger:0.00}");
Console.WriteLine($"Max right trigger    : {maxRightTrigger:0.00}");
Console.WriteLine($"Resting-glitch seen  : {sawWarmUpGlitch}   <- must be False");

// Magnitude legitimately exceeds 1.0 on a diagonal, because X and Y are clamped independently.
// Call it out rather than leaving it to look like a defect.
if (maxLeftMagnitude > 1f || maxRightMagnitude > 1f)
{
    Console.WriteLine();
    Console.WriteLine("NOTE: a stick magnitude above 1.00 is EXPECTED, not a fault. X and Y are each");
    Console.WriteLine("      clamped to [-1, 1] independently, so a corner reaches up to 1.41. Clamp or");
    Console.WriteLine("      normalize before using Magnitude as a movement-speed scalar.");
}

if (usePump)
{
    Engine.Instance.Input.GamepadManager = null;
}

manager.Dispose();
manager.Dispose(); // idempotent by contract
Console.WriteLine();
Console.WriteLine("Disposed twice, cleanly.");
return 0;

static bool IsFullDeflection(CodeBrix.Platform.GameEngine.Input.Gamepad.GamepadStickState stick)
    => Math.Abs(stick.X) >= 1f && Math.Abs(stick.Y) >= 1f;

static string DescribeMissingButtons(SortedSet<string> seen)
{
    var missing = SdlGamepadButtons.All.Where(b => !seen.Contains(b)).ToArray();
    return missing.Length == 0 ? "(none - all 15 confirmed)" : string.Join(", ", missing);
}

static void PrintControllers(SdlGamepadManager manager)
{
    foreach (SdlGamepadAdapter pad in manager.ConnectedAdapters)
    {
        Console.WriteLine($"  GamepadId  : {pad.GamepadId}");
        Console.WriteLine($"  Name       : {pad.Name}");
        Console.WriteLine($"  InstanceId : {pad.InstanceId}");
        Console.WriteLine($"  IsConnected: {pad.IsConnected}");
        Console.WriteLine($"  Mapping    : {pad.GetMappingString()}");
        Console.WriteLine();
    }
}
