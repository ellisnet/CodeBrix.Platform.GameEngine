using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Movement; //was previously: Gondwana.Physics.Movement;
/// <summary>
/// Specifies the coordinate system used for movement calculations and updates.
/// </summary>
public enum MovementSpace
{
    /// <summary>
    /// Movement is in a scene's grid/tile coordinate system.
    /// </summary>
    Grid,

    /// <summary>
    /// Movement is in pixel coordinates relative to the rendering layer.
    /// </summary>
    Pixel
}
