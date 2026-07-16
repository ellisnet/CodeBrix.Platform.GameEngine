using System.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Represents an entity that participates in the collision system and has a defined collision area.
/// </summary>
public interface ICollisionEntity
{
    /// <summary>
    /// Gets the collision area of this entity in world pixel coordinates.
    /// </summary>
    Rectangle CollisionArea { get; }
}
