using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Coordinates; //was previously: Gondwana.Drawing.Coordinates;
/// <summary>
/// Represents the eight cardinal and intercardinal directions of the compass.
/// </summary>
public enum CardinalDirections
{
    /// <summary>
    /// North direction.
    /// </summary>
    N,
    
    /// <summary>
    /// Northeast direction.
    /// </summary>
    NE,
    
    /// <summary>
    /// East direction.
    /// </summary>
    E,
    
    /// <summary>
    /// Southeast direction.
    /// </summary>
    SE,
    
    /// <summary>
    /// South direction.
    /// </summary>
    S,
    
    /// <summary>
    /// Southwest direction.
    /// </summary>
    SW,
    
    /// <summary>
    /// West direction.
    /// </summary>
    W,
    
    /// <summary>
    /// Northwest direction.
    /// </summary>
    NW
}