using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Provides predefined collision mask constants for common collision filtering scenarios.
/// </summary>
public static class CollisionMasks
{
    /// <summary>
    /// Represents a collision mask with no groups enabled (all bits clear).
    /// Used to indicate that no collisions should be detected.
    /// </summary>
    public const int None = 0;
    
    /// <summary>
    /// Represents a collision mask with all groups enabled (all bits set).
    /// Used to indicate that collisions with all groups should be detected.
    /// </summary>
    public const int All = ~0;
}
