using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// planned for future use
/// </summary>
public readonly struct CollisionResult
{
    public ICollider Primary { get; }
    public ICollider Other { get; }
    public CollisionDirectionFrom Direction { get; }

    public CollisionResult(ICollider primary, ICollider other, CollisionDirectionFrom direction)
    {
        Primary = primary;
        Other = other;
        Direction = direction;
    }
}
