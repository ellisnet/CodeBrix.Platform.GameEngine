using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Timers;

namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// Named input actions ("Fire", "MenuUp", "Pause") read from any mix of keyboard keys, gamepad buttons, D-pad and
/// stick directions, through a swappable <see cref="InputBindingProfile"/>. Keyboard and every connected gamepad
/// are read together (hot-plug safe); the game asks per action whether it is held, was pressed or released, or
/// fired a hold-to-repeat step, and which kind of device was used last.
/// </summary>
/// <remarks>
/// <para>
/// Two clocks. <see cref="Poll(double)"/> samples the devices and LATCHES every press and release;
/// <see cref="Update"/> ends one game step: it hands the latched edges to the step's reads (<see cref="IsHeld"/>,
/// <see cref="WasPressed"/>, <see cref="WasReleased"/>, <see cref="IsTriggered"/>) and clears the latch. After
/// <see cref="Attach"/> the engine polls on every engine cycle, so a tap shorter than one fixed step is never
/// lost: the step after it sees it pressed (and held). Without an engine the game calls <see cref="Update"/>
/// alone, which polls first.
/// </para>
/// <para>
/// Whatever is held when the map starts (its first poll) does not count until it is released - no press, no hold,
/// no repeat - so the key that confirmed a menu does not also fire the first shot. <see cref="SuppressHeld"/> does
/// the same when a screen starts; a profile switch does it for inputs that the new profile newly binds.
/// </para>
/// <para>
/// The keys of the active profile are claimed on the keyboard adapter when it implements
/// <see cref="IKeyClaimingAdapter"/> (the Host package's adapter does), so they stay with the game while its
/// surface has focus; see <see cref="ClaimKeys"/>.
/// </para>
/// <para>
/// Thread-safe: the engine thread polls while the game reads from its own step (both usually the engine thread).
/// </para>
/// </remarks>
public sealed class InputActionMap
{
    private const int MaxPressedBindings = 64;

    private readonly object _gate = new();
    private readonly Func<IKeyboardAdapter?> _keyboard;
    private readonly Func<IEnumerable<IGamepadAdapter>?> _gamepads;
    private readonly Dictionary<string, ActionState> _actions = new(StringComparer.Ordinal);
    private readonly List<ActionState> _actionList = new();
    private readonly List<BindingState> _bindings = new();
    private readonly List<IGamepadAdapter> _pads = new();
    private readonly DigitalStickAxis[] _axes = { new(), new(), new(), new() };
    private readonly List<InputBinding> _latchedBindings = new();
    private readonly List<InputBinding> _stepBindings = new();
    private readonly HashSet<int> _ownClaims = new();
    private readonly Dictionary<int, byte> _wantedClaims = new();

    private InputBindingProfile _profile;
    private int _profileVersion = -1;
    private bool _started;
    private bool _suppressNewlyHeld;
    private bool _padsSampled;
    private double _padElapsed;
    private GamepadStickState _leftStick;
    private GamepadStickState _rightStick;
    private InputDeviceKind _lastDevice;
    private bool _stepAnyPressed;

    private Engine? _engine;
    private long _lastPollTick;
    private long _lastPadTick;

    private IKeyClaimingAdapter? _claimTarget;
    private int _claimedVersion = -1;
    private bool _claimKeys = true;

    /// <summary>
    /// Creates a map that reads the engine's devices: the keyboard adapter of
    /// <see cref="KeyboardEventPoller.Instance"/> and the connected adapters of
    /// <see cref="EngineInputSystems.GamepadManager"/>, looked up at every poll (so it may be created before either
    /// exists).
    /// </summary>
    /// <param name="profile">The binding profile to read.</param>
    public InputActionMap(InputBindingProfile profile)
        : this(profile, () => KeyboardEventPoller.Instance?.Adapter, () => Engine.Instance.Input.GamepadManager?.ConnectedAdapters)
    {
    }

    /// <summary>
    /// Creates a map that reads the given devices - for a game that owns its loop, or for tests with fake adapters.
    /// </summary>
    /// <param name="profile">The binding profile to read.</param>
    /// <param name="keyboard">Returns the keyboard adapter to read, or <see langword="null"/> for none; called at every poll.</param>
    /// <param name="gamepads">Returns the connected gamepads, or <see langword="null"/> for none; called at every gamepad sample.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public InputActionMap(InputBindingProfile profile, Func<IKeyboardAdapter?> keyboard, Func<IEnumerable<IGamepadAdapter>?> gamepads)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(keyboard);
        ArgumentNullException.ThrowIfNull(gamepads);
        _profile = profile;
        _keyboard = keyboard;
        _gamepads = gamepads;
    }

    /// <summary>
    /// The binding profile the map reads. Assigning another profile swaps the controls at the next poll; inputs the
    /// new profile newly binds that are held at that moment do not count until released.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public InputBindingProfile Profile
    {
        get
        {
            lock (_gate)
            {
                return _profile;
            }
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                if (!ReferenceEquals(value, _profile))
                {
                    _profile = value;
                    _profileVersion = -1;
                }
            }
        }
    }

    /// <summary>How far (0..1) a stick must be pushed for a stick-direction binding to turn on. Default 0.5.</summary>
    public double StickPressThreshold { get; set; } = 0.5;

    /// <summary>How far (0..1) a stick must come back for a stick-direction binding to turn off (hysteresis). Default 0.3.</summary>
    public double StickReleaseThreshold { get; set; } = 0.3;

    /// <summary>
    /// Seconds after a stick direction turns off during which that axis cannot turn on again, either way. A released
    /// stick springs back PAST centre for a few milliseconds; without this window the overshoot reads as a push the
    /// other way. Default 0.08.
    /// </summary>
    public double StickSettleSeconds { get; set; } = 0.08;

    /// <summary>The dead zone <see cref="GetAxis"/> applies to each stick axis (0 inside it). Default 0.15.</summary>
    public double StickDeadZone { get; set; } = 0.15;

    /// <summary>
    /// Whether the map claims the keys of its active profile on a keyboard adapter that implements
    /// <see cref="IKeyClaimingAdapter"/>. Default <see langword="true"/>. A key the game already uses (claimed or
    /// monitored before the map claimed it) is left alone and never unclaimed by the map; the map withdraws only its
    /// own claims (on a profile switch that drops a key, when set to <see langword="false"/>, and on
    /// <see cref="Detach"/>).
    /// </summary>
    public bool ClaimKeys
    {
        get
        {
            lock (_gate)
            {
                return _claimKeys;
            }
        }

        set
        {
            lock (_gate)
            {
                _claimKeys = value;
                _claimedVersion = -1;
                if (!value)
                {
                    WithdrawClaims();
                }
            }
        }
    }

    /// <summary>
    /// The kind of device that produced the most recent press (a key, a button, a stick push), for on-screen
    /// prompts. Settable, so a game can restore a saved value or note mouse input (<see cref="NoteDeviceUsed"/>).
    /// </summary>
    public InputDeviceKind LastDevice
    {
        get
        {
            lock (_gate)
            {
                return _lastDevice;
            }
        }

        set
        {
            lock (_gate)
            {
                _lastDevice = value;
            }
        }
    }

    /// <summary>Whether any action was pressed in the current step (a "press any key" check).</summary>
    public bool AnyPressed
    {
        get
        {
            lock (_gate)
            {
                return _stepAnyPressed;
            }
        }
    }

    /// <summary>
    /// The bindings pressed since the previous step, oldest first (for an input log): a copy, at most 64 per step.
    /// Repeats and suppressed holds are not listed.
    /// </summary>
    public IReadOnlyList<InputBinding> PressedBindings
    {
        get
        {
            lock (_gate)
            {
                return _stepBindings.ToArray();
            }
        }
    }

    /// <summary>Whether the map is attached to an engine (<see cref="Attach"/>).</summary>
    public bool IsAttached
    {
        get
        {
            lock (_gate)
            {
                return _engine is not null;
            }
        }
    }

    /// <summary>
    /// Lets the engine poll the map on every engine cycle (after the engine's own input polling, before the
    /// fixed steps), so presses shorter than a game step are latched. Gamepads are sampled at most once per
    /// <see cref="Configuration.EngineConfiguration.TimeBetweenGamepadStateUpdates"/>, the rate their state is
    /// refreshed at. The map starts over: whatever is held at the first poll does not count until released.
    /// Call <see cref="Update"/> once per game step (at the top of a <see cref="Engine.FixedUpdate"/> handler).
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The map is attached already.</exception>
    public void Attach(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        lock (_gate)
        {
            if (_engine is not null)
            {
                throw new InvalidOperationException("The input action map is attached to the engine already; call Detach first.");
            }

            _engine = engine;
            _lastPollTick = 0;
            _lastPadTick = 0;
            _started = false;
        }

        engine.AfterBackgroundTasksExecute += OnEngineCycle;
    }

    /// <summary>
    /// Stops the engine polling the map and withdraws the key claims the map made. Does nothing when not attached.
    /// </summary>
    public void Detach()
    {
        Engine? engine;
        lock (_gate)
        {
            engine = _engine;
            _engine = null;
            WithdrawClaims();
            _claimTarget = null;
            _claimedVersion = -1;
        }

        if (engine is not null)
        {
            engine.AfterBackgroundTasksExecute -= OnEngineCycle;
        }
    }

    /// <summary>
    /// Samples the keyboard and every connected gamepad and latches presses and releases until the next
    /// <see cref="Update"/>. The engine calls this on every cycle after <see cref="Attach"/>; a game that owns its
    /// loop calls it as often as it can (at least once per step).
    /// </summary>
    /// <param name="elapsedSeconds">Seconds since the previous poll (drives the stick settle window).</param>
    public void Poll(double elapsedSeconds)
    {
        lock (_gate)
        {
            PollCore(elapsedSeconds, samplePads: true);
        }
    }

    /// <summary>
    /// Ends one game step: the reads (<see cref="IsHeld"/>, <see cref="WasPressed"/>, <see cref="WasReleased"/>,
    /// <see cref="IsTriggered"/>, <see cref="AnyPressed"/>, <see cref="PressedBindings"/>) now describe everything
    /// since the previous step, and the latch is cleared. Advances the hold-to-repeat clocks by
    /// <paramref name="elapsedSeconds"/>. When the map is not attached to an engine, polls the devices first.
    /// </summary>
    /// <param name="elapsedSeconds">The step length in seconds (the fixed step).</param>
    public void Update(double elapsedSeconds)
    {
        lock (_gate)
        {
            if (_engine is null)
            {
                PollCore(elapsedSeconds, samplePads: true);
            }

            var elapsed = elapsedSeconds > 0 && !double.IsInfinity(elapsedSeconds) ? elapsedSeconds : 0;
            _stepAnyPressed = false;
            foreach (var action in _actionList)
            {
                action.EndStep(elapsed);
                _stepAnyPressed |= action.StepPressed;
            }

            _stepBindings.Clear();
            _stepBindings.AddRange(_latchedBindings);
            _latchedBindings.Clear();
        }
    }

    /// <summary>Whether the action is held in the current step (a press latched since the previous step counts, however short).</summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> if held; <see langword="false"/> if not, or if no such action is bound.</returns>
    public bool IsHeld(string action) => Read(action, state => state.StepHeld);

    /// <summary>Whether the action was pressed since the previous step (once per press, however many bindings drive it).</summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> if pressed.</returns>
    public bool WasPressed(string action) => Read(action, state => state.StepPressed);

    /// <summary>Whether the action was released since the previous step.</summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> if released.</returns>
    public bool WasReleased(string action) => Read(action, state => state.StepReleased);

    /// <summary>
    /// Whether the action acts in the current step: pressed, or - with <see cref="SetRepeat"/> timing - held long
    /// enough for a repeat. Without repeat timing it equals <see cref="WasPressed"/>. The menu-navigation read.
    /// </summary>
    /// <param name="action">The action name.</param>
    /// <returns><see langword="true"/> if the action acts in this step.</returns>
    public bool IsTriggered(string action) => Read(action, state => state.StepTriggered);

    /// <summary>
    /// Returns -1, 0 or +1 from two held actions, plus - when <paramref name="stick"/> is given - that stick's
    /// axis (strongest over all gamepads, <see cref="StickDeadZone"/> applied), clamped to -1..+1. The movement read
    /// of an action game. Do not bind the same stick's directions to the two actions as well, or a light push
    /// reads as a full one.
    /// </summary>
    /// <param name="negativeAction">The action for -1 (left or down).</param>
    /// <param name="positiveAction">The action for +1 (right or up).</param>
    /// <param name="stick">The stick to add, or <see langword="null"/> for the actions alone.</param>
    /// <param name="vertical">Whether to add the stick's Y axis (up positive) rather than its X axis.</param>
    /// <returns>The axis value.</returns>
    public double GetAxis(string negativeAction, string positiveAction, GamepadStick? stick = null, bool vertical = false)
    {
        var value = (IsHeld(positiveAction) ? 1.0 : 0.0) - (IsHeld(negativeAction) ? 1.0 : 0.0);
        if (stick is { } which)
        {
            var state = GetStick(which);
            double axis = vertical ? state.Y : state.X;
            value += Math.Abs(axis) < StickDeadZone ? 0 : axis;
        }

        return Math.Clamp(value, -1.0, 1.0);
    }

    /// <summary>The stick's latest position: of all connected gamepads, the one pushed furthest (no dead zone applied).</summary>
    /// <param name="stick">The stick.</param>
    /// <returns>The position; centred when no gamepad is connected.</returns>
    public GamepadStickState GetStick(GamepadStick stick)
    {
        lock (_gate)
        {
            return stick == GamepadStick.Left ? _leftStick : _rightStick;
        }
    }

    /// <summary>Gives an action hold-to-repeat timing (see <see cref="IsTriggered"/>); may be changed at any time.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="repeat">The timing.</param>
    /// <exception cref="ArgumentException"><paramref name="action"/> is null or empty.</exception>
    public void SetRepeat(string action, InputRepeat repeat)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        lock (_gate)
        {
            GetOrAddAction(action).Repeat = repeat;
        }
    }

    /// <summary>Removes an action's hold-to-repeat timing.</summary>
    /// <param name="action">The action name.</param>
    public void ClearRepeat(string action)
    {
        lock (_gate)
        {
            if (action is not null && _actions.TryGetValue(action, out var state))
            {
                state.Repeat = null;
            }
        }
    }

    /// <summary>
    /// Latches a press of the action as if a bound input had been pressed and released - for input that does not
    /// come from a binding (the window being hidden requesting a pause, a scripted demo). Does not change
    /// <see cref="LastDevice"/>.
    /// </summary>
    /// <param name="action">The action name.</param>
    /// <exception cref="ArgumentException"><paramref name="action"/> is null or empty.</exception>
    public void SimulatePress(string action)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        lock (_gate)
        {
            GetOrAddAction(action).PressedLatch = true;
        }
    }

    /// <summary>Records input from a device the map does not read (a mouse click, a touch) for <see cref="LastDevice"/>.</summary>
    /// <param name="device">The device kind.</param>
    public void NoteDeviceUsed(InputDeviceKind device) => LastDevice = device;

    /// <summary>
    /// Makes every action held now count as not held until it is released - no press, no hold, no repeat. Call it
    /// when a screen starts, so the input that left the previous screen does not act on the new one. Presses of
    /// those actions still latched are dropped.
    /// </summary>
    public void SuppressHeld()
    {
        lock (_gate)
        {
            foreach (var action in _actionList)
            {
                if (action.Held)
                {
                    action.Suppressed = true;
                    action.PressedLatch = false;
                    action.RepeatRunning = false;
                }
            }
        }
    }

    /// <summary>Drops every latched press and release and the pressed-binding list, so the next step sees none.</summary>
    public void ClearLatched()
    {
        lock (_gate)
        {
            foreach (var action in _actionList)
            {
                action.PressedLatch = false;
                action.ReleasedLatch = false;
            }

            _latchedBindings.Clear();
        }
    }

    private bool Read(string action, Func<ActionState, bool> read)
    {
        lock (_gate)
        {
            return action is not null && _actions.TryGetValue(action, out var state) && read(state);
        }
    }

    private void OnEngineCycle()
    {
        lock (_gate)
        {
            var engine = _engine;
            if (engine is null)
            {
                return;
            }

            var tick = HighResTimer.GetCurrentTick();
            var elapsed = _lastPollTick == 0 ? 0 : HighResTimer.GetDuration(_lastPollTick, tick);
            _lastPollTick = tick;

            var interval = engine.Configuration.TimeBetweenGamepadStateUpdates;
            var padsDue = interval <= 0 || _lastPadTick == 0 || HighResTimer.GetDuration(_lastPadTick, tick) >= interval;
            if (padsDue)
            {
                _lastPadTick = tick;
            }

            PollCore(elapsed, padsDue);
        }
    }

    private ActionState GetOrAddAction(string action)
    {
        if (!_actions.TryGetValue(action, out var state))
        {
            state = new ActionState();
            _actions.Add(action, state);
            _actionList.Add(state);
        }

        return state;
    }

    private void RefreshBindings()
    {
        var version = _profile.Version;
        if (version == _profileVersion)
        {
            return;
        }

        var pairs = _profile.Snapshot(out version);
        _bindings.Clear();
        foreach (var (action, binding) in pairs)
        {
            _bindings.Add(new BindingState(binding, GetOrAddAction(action)));
        }

        _profileVersion = version;
        _claimedVersion = -1;
        _padsSampled = false;
        if (_started)
        {
            _suppressNewlyHeld = true;
        }
    }

    private void PollCore(double elapsedSeconds, bool samplePads)
    {
        RefreshBindings();

        var elapsed = elapsedSeconds > 0 && !double.IsInfinity(elapsedSeconds) ? elapsedSeconds : 0;
        var keyboard = _keyboard();
        SyncClaims(keyboard);

        // Gamepads change only when the gamepad manager refreshes them; until a sample runs, keep their last state
        _padElapsed += elapsed;
        var padsFresh = (samplePads || !_padsSampled) && SamplePads();

        var countEdges = _started && !_suppressNewlyHeld;
        foreach (var binding in _bindings)
        {
            var active = binding.Binding.Kind switch
            {
                InputBindingKind.Key => keyboard is not null && keyboard.IsDown(binding.Binding.KeyCode),
                _ when !padsFresh => binding.Active,
                InputBindingKind.GamepadButton => IsButtonDown(binding.Binding.Button!),
                _ => IsStickPushed(binding.Binding.Stick, binding.Binding.Direction),
            };

            if (active && !binding.Active && countEdges)
            {
                _lastDevice = binding.Binding.Device;
                if (_latchedBindings.Count < MaxPressedBindings)
                {
                    _latchedBindings.Add(binding.Binding);
                }
            }

            binding.Active = active;
            binding.Action.NextHeld |= active;
        }

        foreach (var action in _actionList)
        {
            var held = action.NextHeld;
            action.NextHeld = false;
            if (!_started)
            {
                action.Suppressed = held;
            }
            else if (held && !action.Held)
            {
                if (_suppressNewlyHeld)
                {
                    action.Suppressed = true;
                }
                else if (!action.Suppressed)
                {
                    action.PressedLatch = true;
                }
            }
            else if (!held && action.Held)
            {
                if (action.Suppressed)
                {
                    action.Suppressed = false;
                }
                else
                {
                    action.ReleasedLatch = true;
                }
            }

            action.Held = held;
        }

        // Gamepad adapters are live views owned by the gamepad manager: never kept past this poll
        _pads.Clear();
        _started = true;
        _suppressNewlyHeld = false;
    }

    private bool SamplePads()
    {
        _pads.Clear();
        try
        {
            var pads = _gamepads();
            if (pads is not null)
            {
                _pads.AddRange(pads);
            }
        }
        catch (InvalidOperationException)
        {
            // The connected list changed while it was read (a pad plugged in or out): keep the last state and
            // sample again at the next poll
            _pads.Clear();
            return false;
        }

        var left = default(GamepadStickState);
        var right = default(GamepadStickState);
        foreach (var pad in _pads)
        {
            left = Stronger(left, pad.LeftStick);
            right = Stronger(right, pad.RightStick);
        }

        _leftStick = left;
        _rightStick = right;

        var press = StickPressThreshold;
        var release = StickReleaseThreshold;
        if (!_started)
        {
            _axes[0].Start(left.X, release);
            _axes[1].Start(left.Y, release);
            _axes[2].Start(right.X, release);
            _axes[3].Start(right.Y, release);
        }
        else
        {
            var settle = StickSettleSeconds;
            _axes[0].Update(left.X, _padElapsed, press, release, settle);
            _axes[1].Update(left.Y, _padElapsed, press, release, settle);
            _axes[2].Update(right.X, _padElapsed, press, release, settle);
            _axes[3].Update(right.Y, _padElapsed, press, release, settle);
        }

        _padElapsed = 0;
        _padsSampled = true;
        return true;
    }

    private static GamepadStickState Stronger(GamepadStickState current, GamepadStickState? candidate)
    {
        if (candidate is not { } stick || float.IsNaN(stick.X) || float.IsNaN(stick.Y))
        {
            return current;
        }

        return stick.Magnitude > current.Magnitude ? stick : current;
    }

    private bool IsButtonDown(string button)
    {
        foreach (var pad in _pads)
        {
            var pressed = pad.PressedButtons;
            if (pressed is not null && pressed.Contains(button))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsStickPushed(GamepadStick stick, StickDirection direction)
    {
        var offset = stick == GamepadStick.Left ? 0 : 2;
        return direction switch
        {
            StickDirection.Up => _axes[offset + 1].Direction > 0,
            StickDirection.Down => _axes[offset + 1].Direction < 0,
            StickDirection.Left => _axes[offset].Direction < 0,
            _ => _axes[offset].Direction > 0,
        };
    }

    private void SyncClaims(IKeyboardAdapter? keyboard)
    {
        var target = _claimKeys ? keyboard as IKeyClaimingAdapter : null;
        if (!ReferenceEquals(target, _claimTarget))
        {
            WithdrawClaims();
            _claimTarget = target;
            _claimedVersion = -1;
        }

        if (target is null || _claimedVersion == _profileVersion)
        {
            return;
        }

        _wantedClaims.Clear();
        foreach (var binding in _bindings)
        {
            if (binding.Binding.Kind == InputBindingKind.Key)
            {
                _wantedClaims[binding.Binding.KeyCode] = 0;
            }
        }

        _ownClaims.RemoveWhere(code =>
        {
            if (_wantedClaims.ContainsKey(code))
            {
                return false;
            }

            target.UnclaimKey(code);
            return true;
        });

        var toClaim = new List<int>();
        foreach (var code in _wantedClaims.Keys)
        {
            if (!_ownClaims.Contains(code) && !target.IsKeyUsed(code))
            {
                toClaim.Add(code);
            }
        }

        if (toClaim.Count > 0)
        {
            target.ClaimKeys(toClaim);
            _ownClaims.UnionWith(toClaim);
        }

        _claimedVersion = _profileVersion;
    }

    private void WithdrawClaims()
    {
        if (_claimTarget is not null)
        {
            foreach (var code in _ownClaims)
            {
                _claimTarget.UnclaimKey(code);
            }
        }

        _ownClaims.Clear();
    }

    private sealed class BindingState
    {
        public BindingState(InputBinding binding, ActionState action)
        {
            Binding = binding;
            Action = action;
        }

        public InputBinding Binding { get; }

        public ActionState Action { get; }

        public bool Active { get; set; }
    }

    private sealed class ActionState
    {
        public bool Held { get; set; }

        public bool NextHeld { get; set; }

        public bool Suppressed { get; set; }

        public bool PressedLatch { get; set; }

        public bool ReleasedLatch { get; set; }

        public InputRepeat? Repeat { get; set; }

        public bool RepeatRunning { get; set; }

        public bool StepHeld { get; private set; }

        public bool StepPressed { get; private set; }

        public bool StepReleased { get; private set; }

        public bool StepTriggered { get; private set; }

        private double _heldTime;
        private double _nextRepeat;

        public void EndStep(double elapsed)
        {
            var heldNow = Held && !Suppressed;
            StepPressed = PressedLatch;
            StepReleased = ReleasedLatch;
            StepHeld = heldNow || PressedLatch;
            StepTriggered = false;

            if (PressedLatch)
            {
                StepTriggered = true;
                RepeatRunning = Repeat is not null && heldNow;
                _heldTime = 0;
                _nextRepeat = Repeat?.DelaySeconds ?? 0;
            }
            else if (RepeatRunning && heldNow && Repeat is { } repeat)
            {
                _heldTime += elapsed;
                if (_heldTime >= _nextRepeat)
                {
                    StepTriggered = true;

                    // One repeat per due time; after a long stall the next one is a full interval away (no burst)
                    _nextRepeat += repeat.IntervalSeconds;
                    if (_nextRepeat <= _heldTime)
                    {
                        _nextRepeat = _heldTime + repeat.IntervalSeconds;
                    }
                }
            }
            else
            {
                RepeatRunning = false;
            }

            PressedLatch = false;
            ReleasedLatch = false;
        }
    }
}
