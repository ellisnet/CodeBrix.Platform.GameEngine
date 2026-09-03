using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Touch; //was previously: Gondwana.Input.Touch;
/// <summary>
/// Describes the current phase of a single touch contact point in its lifecycle.
/// </summary>
public enum TouchPhase
{
    /// <summary>
    /// A finger or pointer made initial contact with the surface.
    /// </summary>
    Began,

    /// <summary>
    /// A finger or pointer that is already in contact moved across the surface.
    /// </summary>
    Moved,

    /// <summary>
    /// A finger or pointer is in contact with the surface but has not moved since the last event.
    /// This conventional phase is retained for adapter and consumer compatibility, but
    /// <see cref="TouchEventPoller"/> does not raise a separate stationary event.
    /// </summary>
    Stationary,

    /// <summary>
    /// A finger or pointer lifted from the surface, ending contact normally.
    /// </summary>
    Ended,

    /// <summary>
    /// A finger or pointer contact was cancelled by the system (for example, an incoming call on a phone).
    /// </summary>
    Cancelled,
}
