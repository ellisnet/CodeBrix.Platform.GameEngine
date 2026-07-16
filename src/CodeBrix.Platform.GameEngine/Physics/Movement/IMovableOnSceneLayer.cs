using CodeBrix.Platform.GameEngine.Scenes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Movement; //was previously: Gondwana.Physics.Movement;
/// <summary>Implemented by grid-space IMovable objects that belong to a specific <see cref="Scenes.SceneLayer"/>.</summary>
public interface IMovableOnSceneLayer : IMovable
{
    /// <summary>
    /// Gets the <see cref="Scenes.SceneLayer"/> that this movable object belongs to.
    /// </summary>
    /// <value>The scene layer containing this movable object.</value>
    SceneLayer SceneLayer { get; }
}
