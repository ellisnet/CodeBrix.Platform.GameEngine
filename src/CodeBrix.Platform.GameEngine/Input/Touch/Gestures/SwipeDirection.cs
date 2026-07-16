using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Touch.Gestures; //was previously: Gondwana.Input.Touch.Gestures;
/// <summary>
/// Specifies the direction of a swipe gesture.
/// </summary>
public enum SwipeDirection
{
    /// <summary>
    /// The swipe moved primarily to the right (positive X axis).
    /// </summary>
    Right,

    /// <summary>
    /// The swipe moved primarily to the left (negative X axis).
    /// </summary>
    Left,

    /// <summary>
    /// The swipe moved primarily upward (negative Y axis in screen coordinates).
    /// </summary>
    Up,

    /// <summary>
    /// The swipe moved primarily downward (positive Y axis in screen coordinates).
    /// </summary>
    Down,
}
