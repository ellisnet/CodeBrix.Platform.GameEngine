using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Defines how a collider responds to collisions with other colliders.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CollisionResponseType
{
    /// <summary>
    /// Solid collision response that pushes out overlapping colliders and blocks movement.
    /// </summary>
    Solid,
    
    /// <summary>
    /// Trigger collision response that reports overlaps without applying push-out or blocking movement.
    /// </summary>
    Trigger
}

