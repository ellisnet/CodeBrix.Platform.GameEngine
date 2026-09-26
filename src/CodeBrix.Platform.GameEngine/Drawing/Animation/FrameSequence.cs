using System.Collections;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Animation; //was previously: Gondwana.Drawing.Animation;
/// <summary>
/// Represents a sequence of animation frames that can be cycled through using different animation patterns
/// </summary>
public struct FrameSequence : IEnumerable<Frame>
{
    #region fields

    /// <summary>
    /// The type of cycle pattern used when animating through the frame sequence
    /// </summary>
    [JsonInclude]
    public CycleType SequenceCycleType;

    [JsonInclude]
    private List<Frame> frameList;

    // Optional per-frame display times in seconds, index-aligned with frameList (shorter is fine:
    // a missing or null entry means "use the cycle's ThrottleTime"). Null when no frame is timed,
    // which keeps saves of untimed sequences in their earlier shape. Replaced, never edited in
    // place, because copies of this struct (cloned cycles) share the list reference.
    private List<double?>? frameDurations;

    private int currentFrameIdx;
    private int curFrameIncrement;
    private bool cycleFinished;

    #endregion fields

    #region constructors / finalizer

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSequence"/> struct with a single frame
    /// </summary>
    /// <param name="frame">The single frame to include in the sequence</param>
    public FrameSequence(Frame frame)
    {
        frameList = new List<Frame>();
        frameList.Add(frame);
        SequenceCycleType = CycleType.Simple;
        currentFrameIdx = 0;
        curFrameIncrement = 1;
        cycleFinished = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSequence"/> struct with a collection of frames
    /// </summary>
    /// <param name="frames">The list of frames to include in the sequence</param>
    public FrameSequence(List<Frame> frames)
    {
        frameList = frames;
        SequenceCycleType = CycleType.Simple;
        currentFrameIdx = 0;
        curFrameIncrement = 1;
        cycleFinished = true;
    }

    #endregion constructors / finalizer

    #region properties

    /// <summary>
    /// Gets a value indicating whether the animation cycle has finished playing through the sequence
    /// </summary>
    [JsonIgnore]
    public bool CycleFinished
    {
        get { return cycleFinished; }
    }

    /// <summary>
    /// Gets the total number of frames in the sequence
    /// </summary>
    [JsonIgnore]
    public int FrameCount
    {
        get
        {
            if (frameList == null)
                SetDefaults();

            return frameList.Count;
        }
    }

    /// <summary>
    /// Gets the frame currently active in the animation sequence
    /// </summary>
    [JsonIgnore]
    public Frame CurrentFrame
    {
        get
        {
            if (frameList == null)
                SetDefaults();

            return frameList[currentFrameIdx];
        }
    }

    /// <summary>
    /// Gets the zero-based index of the current frame in the sequence
    /// </summary>
    [JsonIgnore]
    public int CurrentFrameIdx
    {
        get { return currentFrameIdx; }
    }

    /// <summary>
    /// Gets a read-only view of the list of frames in the sequence
    /// </summary>
    [JsonIgnore]
    public IList<Frame> FrameList
    {
        get { return frameList.AsReadOnly(); }
    }

    /// <summary>
    /// Gets a value indicating whether at least one frame carries its own display duration
    /// (see <see cref="SetDurationSeconds"/>). When <see langword="false"/> every frame shows for
    /// the owning <see cref="Cycle.ThrottleTime"/>.
    /// </summary>
    [JsonIgnore]
    public bool HasFrameDurations
    {
        get { return frameDurations is not null; }
    }

    #endregion properties

    #region public methods

    /// <summary>
    /// Creates and adds a new frame to the sequence using tilesheet coordinates
    /// </summary>
    /// <param name="bmp">The tilesheet containing the frame image</param>
    /// <param name="regionName">The name of the region within the tilesheet</param>
    /// <param name="xTile">The x-coordinate of the tile in the tilesheet</param>
    /// <param name="yTile">The y-coordinate of the tile in the tilesheet</param>
    /// <returns>The newly created and added <see cref="Frame"/></returns>
    public Frame AddFrame(Tilesheet bmp, string regionName, int xTile, int yTile)
    {
        return AddFrame(new Frame(bmp, regionName, xTile, yTile));
    }

    /// <summary>
    /// Creates and adds a new frame to the sequence using tilesheet coordinates
    /// </summary>
    /// <param name="bmp">The tilesheet containing the frame image</param>
    /// <param name="xTile">The x-coordinate of the tile in the tilesheet</param>
    /// <param name="yTile">The y-coordinate of the tile in the tilesheet</param>
    /// <returns>The newly created and added <see cref="Frame"/></returns>
    public Frame AddFrame(Tilesheet bmp, int xTile, int yTile)
    {
        return AddFrame(new Frame(bmp, TilesheetRegion.DefaultRegionName, xTile, yTile));
    }

    /// <summary>
    /// Adds an existing frame to the sequence
    /// </summary>
    /// <param name="frame">The frame to add to the sequence</param>
    /// <returns>The added <see cref="Frame"/></returns>
    public Frame AddFrame(Frame frame)
    {
        if (frameList == null)
            SetDefaults();

        frameList.Add(frame);
        return frame;
    }

    /// <summary>
    /// Adds an existing frame to the sequence with its own display duration
    /// </summary>
    /// <param name="frame">The frame to add to the sequence</param>
    /// <param name="durationSeconds">How long the frame shows, in seconds; must be positive and finite</param>
    /// <returns>The added <see cref="Frame"/></returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="durationSeconds"/> is not positive and finite; the frame is not added.
    /// </exception>
    public Frame AddFrame(Frame frame, double durationSeconds)
    {
        ValidateDuration(durationSeconds, nameof(durationSeconds));

        AddFrame(frame);
        SetDurationSeconds(FrameCount - 1, durationSeconds);
        return frame;
    }

    /// <summary>
    /// Removes the frame at the specified index from the sequence; the other frames keep their durations
    /// </summary>
    /// <param name="idx">The zero-based index of the frame to remove</param>
    public void RemoveFrame(int idx)
    {
        if (idx < frameList.Count)
        {
            frameList.RemoveAt(idx);

            if (frameDurations is not null && idx >= 0 && idx < frameDurations.Count)
            {
                var durations = new List<double?>(frameDurations);
                durations.RemoveAt(idx);
                frameDurations = NullWhenUntimed(durations);
            }
        }
    }

    /// <summary>
    /// Gets the display duration of the frame at the specified index, when it carries one
    /// </summary>
    /// <param name="index">The zero-based index of the frame</param>
    /// <returns>
    /// The frame's duration in seconds, or <see langword="null"/> when the frame shows for the
    /// owning <see cref="Cycle.ThrottleTime"/> (also for an index outside the sequence)
    /// </returns>
    public double? GetDurationSeconds(int index)
    {
        if (frameDurations is null || index < 0 || index >= frameDurations.Count)
            return null;

        return frameDurations[index];
    }

    /// <summary>
    /// Sets how long the frame at the specified index shows, or clears it back to the cycle default
    /// </summary>
    /// <param name="index">The zero-based index of the frame</param>
    /// <param name="seconds">
    /// The frame's duration in seconds (positive and finite), or <see langword="null"/> to show the
    /// frame for the owning <see cref="Cycle.ThrottleTime"/>
    /// </param>
    /// <remarks>
    /// A copy of this sequence (for example the one inside a cloned <see cref="Cycle"/>) keeps its
    /// own timing: changing a duration here never changes the copy's, and the reverse.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside the sequence, or when
    /// <paramref name="seconds"/> is not positive and finite.
    /// </exception>
    public void SetDurationSeconds(int index, double? seconds)
    {
        if (index < 0 || index >= FrameCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the sequence.");

        if (seconds is { } duration)
            ValidateDuration(duration, nameof(seconds));

        var durations = frameDurations is null ? new List<double?>() : new List<double?>(frameDurations);
        while (durations.Count < FrameCount)
            durations.Add(null);

        durations[index] = seconds;
        frameDurations = NullWhenUntimed(durations);
    }

    /// <summary>
    /// Removes every per-frame duration, so all frames show for the owning <see cref="Cycle.ThrottleTime"/>
    /// </summary>
    public void ClearFrameDurations()
    {
        frameDurations = null;
    }

    /// <summary>
    /// Resets the sequence to its initial state, starting from the first frame
    /// </summary>
    public void Reset()
    {
        currentFrameIdx = 0;
        curFrameIncrement = 1;
    }

    #endregion public methods

    #region internal methods

    /// <summary>
    /// The durations as saved: one entry per frame (null = the cycle default), or null when no frame is timed.
    /// </summary>
    internal IReadOnlyList<double?>? GetDurationsForSave()
    {
        if (frameDurations is null)
            return null;

        var durations = new List<double?>(FrameCount);
        for (var i = 0; i < FrameCount; i++)
            durations.Add(GetDurationSeconds(i));

        return durations;
    }

    /// <summary>
    /// Restores saved durations; entries beyond the frame count are ignored.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a duration is not positive and finite.</exception>
    internal void SetDurationsFromSave(IReadOnlyList<double?> durations)
    {
        var restored = new List<double?>(FrameCount);
        for (var i = 0; i < FrameCount && i < durations.Count; i++)
        {
            if (durations[i] is { } duration)
                ValidateDuration(duration, nameof(durations));

            restored.Add(durations[i]);
        }

        frameDurations = NullWhenUntimed(restored);
    }

    internal void StopCycle()
    {
        cycleFinished = true;
    }

    internal Frame AdvanceFrame()
    {
        switch (SequenceCycleType)
        {
            case CycleType.PingPong:
                currentFrameIdx += curFrameIncrement;
                if ((currentFrameIdx <= 0) || (currentFrameIdx >= frameList.Count - 1))
                    curFrameIncrement *= -1;

                if (currentFrameIdx < 0)
                    currentFrameIdx = 0;

                if (currentFrameIdx > frameList.Count - 1)
                    currentFrameIdx = frameList.Count - 1;

                cycleFinished = false;
                break;

            case CycleType.Repeating:
                if (++currentFrameIdx >= frameList.Count)
                    currentFrameIdx = 0;

                cycleFinished = false;
                break;

            case CycleType.Simple:
                if (++currentFrameIdx > frameList.Count - 1)
                {
                    currentFrameIdx = frameList.Count - 1;
                    cycleFinished = true;
                }
                else
                    cycleFinished = false;
                break;

            default:
                throw new InvalidOperationException("Invalid CycleType: " + SequenceCycleType.ToString());
        }

        return frameList[currentFrameIdx];
    }

    #endregion internal methods

    #region private methods

    private static void ValidateDuration(double seconds, string paramName)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentOutOfRangeException(paramName, seconds, "A frame duration must be positive and finite.");
    }

    private static List<double?>? NullWhenUntimed(List<double?> durations)
    {
        foreach (var duration in durations)
        {
            if (duration.HasValue)
                return durations;
        }

        return null;
    }

    private void SetDefaults()
    {
        frameList = new List<Frame>();
        frameDurations = null;
        SequenceCycleType = CycleType.Simple;
        currentFrameIdx = 0;
        curFrameIncrement = 1;
        cycleFinished = true;
    }

    #endregion private methods

    #region indexers

    /// <summary>
    /// Gets the frame at the specified index in the sequence
    /// </summary>
    /// <param name="frameIdx">The zero-based index of the frame to retrieve</param>
    /// <returns>The <see cref="Frame"/> at the specified index</returns>
    public Frame this[int frameIdx]
    {
        get { return frameList[frameIdx]; }
    }

    #endregion indexers

    #region IEnumerable Members

    /// <summary>
    /// Returns an enumerator that iterates through the frame sequence
    /// </summary>
    /// <returns>An <see cref="IEnumerator"/> for the frame sequence</returns>
    public IEnumerator GetEnumerator()
    {
        for (int i = 0; i < frameList.Count; i++)
            yield return frameList[i];
    }

    /// <summary>
    /// Returns a strongly-typed enumerator that iterates through the frame sequence
    /// </summary>
    /// <returns>An <see cref="IEnumerator{Frame}"/> for the frame sequence</returns>
    IEnumerator<Frame> IEnumerable<Frame>.GetEnumerator()
    {
        return frameList.GetEnumerator();
    }

    #endregion IEnumerable Members
}