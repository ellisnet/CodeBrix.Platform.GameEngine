using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Touch.Gestures; //was previously: Gondwana.Input.Touch.Gestures;
/// <summary>
/// Provides data for the <see cref="PinchGestureRecognizer.PinchUpdated"/> event,
/// describing the change in scale produced by a two-finger pinch or spread gesture.
/// </summary>
public sealed class PinchedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the lifecycle phase of this pinch event.
    /// </summary>
    public PinchPhase Phase { get; }

    /// <summary>
    /// Gets the two contact identifiers participating in the gesture, in ascending order.
    /// The list is empty when the event was created from the two-argument constructor.
    /// </summary>
    public IReadOnlyList<int> TouchIds { get; }

    /// <summary>
    /// Gets the midpoint between the two contacts, in client coordinates.
    /// </summary>
    public PointF Center { get; }

    /// <summary>
    /// Gets the distance in pixels at which the current pinch gesture began.
    /// </summary>
    public double StartingDistance { get; }

    /// <summary>
    /// Gets the distance in pixels reported by the preceding pinch event.
    /// </summary>
    public double PreviousDistance { get; }

    /// <summary>
    /// Gets the scale factor relative to the previous pinch update.
    /// A value greater than <c>1.0</c> indicates the fingers moved apart (zoom in / expand),
    /// while a value less than <c>1.0</c> indicates the fingers moved closer together (zoom out / contract).
    /// A value of exactly <c>1.0</c> means no change in finger separation occurred.
    /// </summary>
    public double ScaleDelta { get; }

    /// <summary>
    /// Gets the current distance in pixels between the two active touch contact points.
    /// </summary>
    public double CurrentDistance { get; }

    /// <summary>
    /// Gets the scale relative to the beginning of the current pinch gesture.
    /// A value greater than <c>1.0</c> means the contacts have spread apart since the gesture
    /// began, while a value less than <c>1.0</c> means they have moved closer together.
    /// </summary>
    public double TotalScale { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PinchedEventArgs"/> class describing a
    /// pinch update with only the relative scale and current distance known.
    /// </summary>
    /// <param name="scaleDelta">
    /// The scale factor relative to the previous pinch update.
    /// </param>
    /// <param name="currentDistance">
    /// The current distance in pixels between the two active touch contact points.
    /// </param>
    public PinchedEventArgs(double scaleDelta, double currentDistance)
    {
        Phase = PinchPhase.Updated;
        TouchIds = Array.Empty<int>();
        Center = PointF.Empty;
        StartingDistance = currentDistance;
        PreviousDistance = scaleDelta != 0 ? currentDistance / scaleDelta : currentDistance;
        CurrentDistance = currentDistance;
        ScaleDelta = scaleDelta;
        TotalScale = scaleDelta;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PinchedEventArgs"/> class with the complete
    /// pinch lifecycle data produced by <see cref="PinchGestureRecognizer"/>.
    /// </summary>
    /// <param name="phase">The lifecycle phase of the pinch gesture.</param>
    /// <param name="touchIds">The two contact identifiers participating in the gesture.</param>
    /// <param name="center">The midpoint between the two contacts, in client coordinates.</param>
    /// <param name="startingDistance">The distance in pixels at which the gesture began.</param>
    /// <param name="previousDistance">The distance in pixels reported by the preceding event.</param>
    /// <param name="currentDistance">The current distance in pixels between the two contacts.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="touchIds"/> is <see langword="null"/>.
    /// </exception>
    public PinchedEventArgs(
        PinchPhase phase,
        IReadOnlyList<int> touchIds,
        PointF center,
        double startingDistance,
        double previousDistance,
        double currentDistance)
    {
        Phase = phase;
        TouchIds = touchIds ?? throw new ArgumentNullException(nameof(touchIds));
        Center = center;
        StartingDistance = startingDistance;
        PreviousDistance = previousDistance;
        CurrentDistance = currentDistance;
        ScaleDelta = previousDistance > 0 ? currentDistance / previousDistance : 1.0;
        TotalScale = startingDistance > 0 ? currentDistance / startingDistance : 1.0;
    }
}
