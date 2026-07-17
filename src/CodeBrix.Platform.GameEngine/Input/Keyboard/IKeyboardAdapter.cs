using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Input.Keyboard; //was previously: Gondwana.Input.Keyboard;
/// <summary>
/// Represents an adapter for keyboard input, providing access to the currently pressed keys and active modifier states.
/// </summary>
/// <remarks>This interface is designed to abstract keyboard input handling, allowing retrieval of pressed keys
/// and modifier states. The implementation of the interface is responsible for keyboard polling, etc.</remarks>
public interface IKeyboardAdapter
{
    /// <summary>
    /// Returns true if the specified platform-agnostic key code is currently down.
    /// Key codes should be stable integers agreed upon by the adapter and the engine.
    /// For WinForms, this should be the Windows Virtual-Key code (0..255).
    /// </summary>
    /// <remarks>
    /// CONTRACT: implementations must make this lock-free and safe to call from ANY thread at
    /// high frequency — it is the per-tic polled-state path for game loops (called directly
    /// from game threads, with or without the engine cycle running).
    /// </remarks>
    bool IsDown(int keyCode);

    /// <summary>
    /// Gets the current state of modifier keys, such as Shift, Ctrl, and Alt.
    /// Must be safe to read from the Engine thread at high frequency.
    /// </summary>
    KeyboardModifierState CurrentKeyboardModifiers { get; }
}