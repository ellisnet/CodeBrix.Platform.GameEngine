using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Touch; //was previously: Gondwana.Input.Touch;
/// <summary>
/// Provides data for touch contact events raised by <see cref="ITouchInput"/>,
/// including the details of the touch point that triggered the event.
/// </summary>
public sealed class TouchEventArgs : EventArgs
{
    /// <summary>
    /// Gets the touch contact point associated with this event, including its identifier,
    /// screen position, and current lifecycle phase.
    /// </summary>
    public TouchPoint Touch { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchEventArgs"/> class with the specified
    /// touch contact point.
    /// </summary>
    /// <param name="touch">
    /// The touch contact point that caused this event to be raised.
    /// </param>
    /// <param name="tick">
    /// The high-resolution tick at which the poll that raised this event ran. Defaults to 0 for
    /// callers that do not track it.
    /// </param>
    public TouchEventArgs(TouchPoint touch, long tick = 0)
    {
        Touch = touch;
        Tick = tick;
    }

    /// <summary>
    /// Gets the high-resolution tick at which the poll that raised this event ran.
    /// </summary>
    /// <remarks>
    /// Comparable with <see cref="CodeBrix.Platform.GameEngine.Timers.HighResTimer.GetCurrentTick"/>
    /// values, so a handler can measure gesture timing directly. 0 when the raiser did not supply one.
    /// </remarks>
    public long Tick { get; }
}
