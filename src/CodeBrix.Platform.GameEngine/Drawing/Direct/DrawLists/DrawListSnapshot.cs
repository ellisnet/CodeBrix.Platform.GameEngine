using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// A finished, published copy of a <see cref="DrawList"/>: its commands (back to front) and hit regions. It never
/// changes after it is made, so the render side can draw it on any thread while the game builds the next one.
/// </summary>
public sealed class DrawListSnapshot
{
    private readonly DrawCommand[] _commands;
    private readonly DrawHitRegion[] _hitRegions;

    /// <summary>An empty snapshot (number 0): what a list publishes before its first <see cref="DrawList.Publish"/>.</summary>
    public static readonly DrawListSnapshot Empty = new(Array.Empty<DrawCommand>(), Array.Empty<DrawHitRegion>(), 0);

    /// <summary>
    /// Creates a snapshot from commands and hit regions, copying both, so the snapshot stays unchanged whatever the
    /// caller does with its collections afterwards.
    /// </summary>
    /// <param name="commands">The commands, back to front.</param>
    /// <param name="hitRegions">The hit regions, back to front (the last one wins a <see cref="HitTest"/>); null for none.</param>
    /// <param name="number">A frame number for the caller's own bookkeeping.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="commands"/> is null.</exception>
    public DrawListSnapshot(IEnumerable<DrawCommand> commands, IEnumerable<DrawHitRegion>? hitRegions = null, long number = 0)
        : this(
            (commands ?? throw new ArgumentNullException(nameof(commands))).ToArray(),
            hitRegions?.ToArray() ?? Array.Empty<DrawHitRegion>(),
            number)
    {
    }

    //The arrays are owned by the new snapshot (never shared with a builder)
    internal DrawListSnapshot(DrawCommand[] commands, DrawHitRegion[] hitRegions, long number)
    {
        _commands = commands;
        _hitRegions = hitRegions;
        Commands = Array.AsReadOnly(commands);
        HitRegions = Array.AsReadOnly(hitRegions);
        Number = number;
    }

    /// <summary>Gets the commands, back to front.</summary>
    public ReadOnlyCollection<DrawCommand> Commands { get; }

    /// <summary>Gets the hit regions, in the order they were added.</summary>
    public ReadOnlyCollection<DrawHitRegion> HitRegions { get; }

    /// <summary>
    /// Gets the frame number: <see cref="DrawList.Publish"/> numbers its snapshots 1, 2, 3, ... per list;
    /// <see cref="Empty"/> is 0.
    /// </summary>
    public long Number { get; }

    //The render side iterates the array directly (no enumerator, no copies of the wrapper)
    internal DrawCommand[] CommandArray => _commands;

    /// <summary>
    /// Finds the hit region under a point: the LAST one added that contains it, so a region added later (drawn on
    /// top) wins where two overlap.
    /// </summary>
    /// <param name="x">The point's X, in the list's coordinates.</param>
    /// <param name="y">The point's Y, in the list's coordinates.</param>
    /// <returns>The region, or <see langword="null"/> when the point hits none.</returns>
    public DrawHitRegion? HitTest(double x, double y)
    {
        for (var i = _hitRegions.Length - 1; i >= 0; i--)
        {
            if (_hitRegions[i].Contains(x, y))
            {
                return _hitRegions[i];
            }
        }

        return null;
    }
}
