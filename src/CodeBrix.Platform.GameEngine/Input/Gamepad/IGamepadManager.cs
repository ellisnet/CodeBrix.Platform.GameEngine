using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Gamepad; //was previously: Gondwana.Input.Gamepad;
/// <summary>
/// Interface to track a collection of <see cref="IGamepadAdapter" /> gamepad adapters."/>
/// </summary>
public interface IGamepadManager<out T> where T : IGamepadAdapter
{
    /// <summary>
    /// Gets the list of currently connected gamepad adapters.
    /// </summary>
    IReadOnlyCollection<T> ConnectedAdapters { get; }

    /// <summary>
    /// Updates the <see cref="ConnectedAdapters" /> of all connected gamepads.
    /// This will be called every frame to ensure the gamepad state is up-to-date.
    /// </summary>
    /// <remarks>*** DO NOT CALL THIS WITHOUT THROTTLING; LIMIT TO ENGINE FRAMERATE ***</remarks>
    void Update();
}