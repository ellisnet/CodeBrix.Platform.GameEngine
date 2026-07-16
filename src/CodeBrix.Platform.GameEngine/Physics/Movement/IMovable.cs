using System.Numerics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Movement; //was previously: Gondwana.Physics.Movement;
/// <summary>
/// Defines an object that can be moved within a specific coordinate space.
/// </summary>
/// <remarks>The <see cref="IMovable"/> interface provides methods to retrieve and update the position of an
/// object in a defined <see cref="MovementSpace"/>. Implementations of this interface are expected to handle
/// position-related operations consistently within the specified coordinate system.</remarks>
public interface IMovable
{
    /// <summary>Which unit system this mover uses for its position.</summary>
    MovementSpace PositionSpace { get; }

    /// <summary>Get the current position in the mover's <see cref="PositionSpace"/>.</summary>
    Vector2 GetPosition();

    /// <summary>Set the position in the mover's <see cref="PositionSpace"/>.</summary>
    void SetPosition(Vector2 pos);
}
