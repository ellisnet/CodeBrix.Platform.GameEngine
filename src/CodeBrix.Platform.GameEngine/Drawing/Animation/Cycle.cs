using System.Runtime.Serialization;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Timers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Json.Extensions.References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Animation; //was previously: Gondwana.Drawing.Animation;
/// <summary>
/// References a FrameSequence object, along with a particular
/// Throttle value for animating through Frame objects
/// </summary>
[JsonReferenceable]
public class Cycle : ICloneable, IDisposable
{
    #region fields

    /// <summary>
    /// The frame sequence used by this animation cycle
    /// </summary>
    [JsonInclude]
    public FrameSequence Sequence;

    /// <summary>
    /// The unique identifier key for this cycle
    /// </summary>
    [JsonInclude]
    public readonly string CycleKey;

    /// <summary>
    /// Indicates whether the tile should be hidden when the cycle completes
    /// </summary>
    [JsonInclude]
    public readonly bool HideTileOnCycleEnd;

    internal long _throttle = 0;

    #endregion fields

    #region constructors / destructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Cycle"/> class
    /// </summary>
    /// <param name="sequence">The frame sequence to animate through</param>
    /// <param name="throttleTime">The time in seconds between frame transitions</param>
    /// <param name="cycleKey">The unique identifier for this cycle</param>
    /// <param name="hideTileOnCycleEnd">If true, hides the tile when the cycle completes. Default is false.</param>
    public Cycle(FrameSequence sequence, double throttleTime, string cycleKey, bool hideTileOnCycleEnd = false)
    {
        Sequence = sequence;
        ThrottleTime = throttleTime;
        NextCycle = this;
        CycleKey = cycleKey;
        HideTileOnCycleEnd = hideTileOnCycleEnd;

        if (Cycle._cycles.ContainsKey(cycleKey))
            Cycle._cycles[cycleKey] = this;
        else
            Cycle._cycles.Add(cycleKey, this);
    }

    private Cycle(Cycle fromCycle)
    {
        Sequence = fromCycle.Sequence;
        _throttle = fromCycle._throttle;
        _throttleTime = fromCycle._throttleTime;
        NextCycle = this;
        CycleKey = fromCycle.CycleKey;
        HideTileOnCycleEnd = fromCycle.HideTileOnCycleEnd;
    }

    // Deserialization construction: no registry side effect (EngineState's merge step
    // decides registry membership); the serializer populates the members afterwards —
    // including the readonly CycleKey/HideTileOnCycleEnd fields, via the save-contract
    // resolver's field access.
    private Cycle()
    {
        Sequence = new FrameSequence(new List<Frame>());
        CycleKey = string.Empty;
        NextCycle = this;
    }

    /// <summary>
    /// Creates a bare <see cref="Cycle"/> for the save-system deserializer: unlike the public
    /// constructor it does NOT self-register in the global cycle registry.
    /// </summary>
    internal static Cycle CreateForDeserialization() => new();

    #endregion constructors / destructor

    #region public properties

    private double _throttleTime;

    /// <summary>
    /// Gets or sets the time in seconds between frame transitions in the animation cycle
    /// </summary>
    [JsonInclude]
    public double ThrottleTime
    {
        get { return _throttleTime; }
        set
        {
            _throttle = (long)(value * (double)HighResTimer.TicksPerSecond);
            _throttleTime = value;
        }
    }

    /// <summary>
    /// Gets how long the sequence's current frame shows, in seconds: the frame's own duration when
    /// it carries one (see <see cref="FrameSequence.SetDurationSeconds"/>), otherwise <see cref="ThrottleTime"/>
    /// </summary>
    [JsonIgnore]
    public double CurrentFrameDurationSeconds
    {
        get { return Sequence.GetDurationSeconds(Sequence.CurrentFrameIdx) ?? ThrottleTime; }
    }

    // A frame's own duration held longer than this many ticks is capped here, so the animator's
    // "last tick + throttle" arithmetic can never overflow; the frame still holds for ages.
    private const long MaxFrameThrottleTicks = long.MaxValue / 4;

    /// <summary>
    /// The current frame's display time in ticks, as the animator waits on it. Without a per-frame
    /// duration this is exactly the cycle's throttle (so untimed cycles behave as before, including
    /// stopping when the throttle is not positive). A frame's own duration shorter than one tick
    /// maps to 0 (the animator then stops, as for a zero throttle).
    /// </summary>
    internal long CurrentFrameThrottle
    {
        get
        {
            if (Sequence.GetDurationSeconds(Sequence.CurrentFrameIdx) is not { } seconds)
                return _throttle;

            var ticks = seconds * HighResTimer.TicksPerSecond;
            if (!double.IsFinite(ticks) || ticks >= MaxFrameThrottleTicks)
                return MaxFrameThrottleTicks;

            return ticks < 1 ? 0 : (long)ticks;
        }
    }

    /// <summary>
    /// Returns the total time in seconds for the Cycle
    /// </summary>
    /// <remarks>
    /// When frames carry their own durations each frame counts for its own time (the others for
    /// <see cref="ThrottleTime"/>), in the same pattern as the uniform totals: a simple cycle counts
    /// every frame but the last, a repeating cycle every frame once, and a ping-pong cycle the end
    /// frames once and the middle frames twice.
    /// </remarks>
    [JsonIgnore]
    public double TotalCycleTime
    {
        get
        {
            if (Sequence.HasFrameDurations)
                return TotalCycleTimeWithFrameDurations();

            switch (Sequence.SequenceCycleType)
            {
                case CycleType.Simple:
                    // -1 since first frame is played right away
                    return ThrottleTime * (double)(Sequence.FrameCount - 1);

                case CycleType.Repeating:
                    return ThrottleTime * (double)Sequence.FrameCount;

                case CycleType.PingPong:
                    // -2, since:
                    // "C" is only shown once, and...
                    // second "A" is actually part of next cycle repetition
                    return ThrottleTime * (double)((Sequence.FrameCount * 2) - 2);

                default:
                    return 0;
            }
        }
    }

    private double TotalCycleTimeWithFrameDurations()
    {
        var frameCount = Sequence.FrameCount;
        double total = 0;

        for (var i = 0; i < frameCount; i++)
        {
            var duration = Sequence.GetDurationSeconds(i) ?? ThrottleTime;

            switch (Sequence.SequenceCycleType)
            {
                case CycleType.Simple:
                    // the last frame is where the cycle ends, as in the uniform "- 1"
                    if (i < frameCount - 1)
                        total += duration;
                    break;

                case CycleType.Repeating:
                    total += duration;
                    break;

                case CycleType.PingPong:
                    // end frames once, middle frames on the way out and on the way back
                    // (a single frame never moves, as in the uniform "* 2 - 2")
                    if (frameCount > 1)
                        total += (i > 0 && i < frameCount - 1) ? duration * 2 : duration;
                    break;

                default:
                    return 0;
            }
        }

        return total;
    }

    /// <summary>
    /// Gets or sets the next cycle to transition to when this cycle completes
    /// </summary>
    [JsonInclude]
    public Cycle NextCycle { get; set; }

    #endregion public properties

    #region ICloneable Members

    /// <summary>
    /// Creates a shallow copy of the current <see cref="Cycle"/> instance
    /// </summary>
    /// <returns>A new <see cref="Cycle"/> object that is a copy of this instance</returns>
    public object Clone()
    {
        return new Cycle(this);
    }

    #endregion ICloneable Members

    #region IDisposable Members

    /// <summary>
    /// Releases all resources used by the <see cref="Cycle"/> and removes it from the static cycle collection
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (Sprite sprite in SpriteManager.Instance._spriteList)
        {
            if (sprite.TileAnimator.CurrentCycle == this)
                sprite.TileAnimator.CurrentCycle = null;
        }

        Cycle._cycles.Remove(CycleKey);
    }

    #endregion IDisposable Members

    #region static members

    internal static readonly Dictionary<string, Cycle> _cycles = new();

    /// <summary>
    /// Gets the total number of animation cycles currently registered
    /// </summary>
    public static int Count => _cycles.Count;

    /// <summary>
    /// Retrieves a list of all registered animation cycle keys
    /// </summary>
    /// <returns>A list containing all cycle keys</returns>
    public static List<string> GetAnimationCycleKeys() => new List<string>(_cycles.Keys);

    /// <summary>
    /// Retrieves a list of all registered animation cycles
    /// </summary>
    /// <returns>A list containing all registered <see cref="Cycle"/> instances</returns>
    public static List<Cycle> GetAnimationCycles() => new List<Cycle>(_cycles.Values);

    /// <summary>
    /// Retrieves a clone of the animation cycle with the specified key
    /// </summary>
    /// <param name="cycleKey">The unique identifier of the cycle to retrieve</param>
    /// <returns>A cloned <see cref="Cycle"/> instance if found; otherwise, null</returns>
    public static Cycle GetAnimationCycle(string cycleKey)
    {
        if (_cycles.ContainsKey(cycleKey))
            return (Cycle)_cycles[cycleKey].Clone();
        else
            return null;
    }

    /// <summary>
    /// Removes and disposes the animation cycle with the specified key
    /// </summary>
    /// <param name="cycleKey">The unique identifier of the cycle to clear</param>
    public static void ClearAnimationCycle(string cycleKey)
    {
        if (_cycles.ContainsKey(cycleKey))
            _cycles[cycleKey].Dispose();
    }

    /// <summary>
    /// Removes and disposes all registered animation cycles
    /// </summary>
    public static void ClearAllAnimationCycles()
    {
        var tempCycles = new List<Cycle>(_cycles.Values);
        foreach (Cycle cyc in tempCycles)
            cyc.Dispose();
    }

    #endregion static members
}