using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Physics.Collisions; //was previously: Gondwana.Physics.Collisions;
/// <summary>
/// Manages registration and querying of colliders, separating them into static and dynamic collections
/// for efficient collision detection.
/// </summary>
public sealed class ColliderRegistry
{
    private readonly HashSet<ICollider> _static = new();
    private readonly HashSet<ICollider> _dynamic = new();

    // Reused by QueryAabb so a broad-phase query does not allocate a fresh instance list and
    // de-duplication set on every call. QueryAabb is not re-entrant, matching QueryInstances.
    private readonly List<ColliderInstance> _instanceScratch = new();
    private readonly HashSet<ICollider> _uniqueScratch = new();

    /// <summary>
    /// Gets or sets the scene layer whose repetition lattice this registry queries through.
    /// </summary>
    internal CodeBrix.Platform.GameEngine.Scenes.SceneLayer? SceneLayer { get; set; }

    /// <summary>
    /// Queries canonical colliders with translated bounds for every overlapping wrapped instance.
    /// Collision masks and the ignored collider apply to canonical identities.
    /// </summary>
    /// <param name="area">The axis-aligned bounding box to query within.</param>
    /// <param name="layerMask">The layer mask to test against each collider's <see cref="ICollider.CollidesWith"/> mask.</param>
    /// <param name="collidesWithMask">The collision mask to test against each collider's <see cref="ICollider.CollisionGroup"/>.</param>
    /// <param name="results">The list to populate with matching instances. This list is cleared before adding results.</param>
    /// <param name="ignore">An optional collider to exclude from the results.</param>
    /// <exception cref="InvalidOperationException">
    /// The owning layer wraps but cannot produce a valid repetition lattice, or the query would
    /// produce more than one million candidate instances.
    /// </exception>
    public void QueryInstances(in Aabb area, int layerMask, int collidesWithMask,
        List<ColliderInstance> results, ICollider? ignore = null)
    {
        results.Clear();
        var layer = SceneLayer;
        var periodic = layer is not null && (layer.WrapHorizontally || layer.WrapVertically);
        var period = periodic ? layer!.GetPeriod() : default;
        var query = new System.Drawing.RectangleF(area.MinX, area.MinY, area.Width, area.Height);
        // The static and dynamic sets are walked in turn rather than through a LINQ concatenation,
        // so a per-frame broad-phase query allocates no enumerator. The visit order is unchanged.
        var box = area;
        void Collect(ICollider collider)
        {
            if (ReferenceEquals(collider, ignore) || (collider.CollisionGroup & collidesWithMask) == 0 || (layerMask & collider.CollidesWith) == 0)
                return;
            var bounds = collider.BoundsWorldPx;
            if (!periodic)
            {
                if (box.Intersects(bounds)) results.Add(new(collider, bounds));
                return;
            }
            foreach (var offset in period.Offsets(new(bounds.MinX, bounds.MinY, bounds.Width, bounds.Height), query))
                results.Add(new(collider, new(bounds.MinX + offset.X, bounds.MinY + offset.Y, bounds.MaxX + offset.X, bounds.MaxY + offset.Y)));
        }

        foreach (var collider in _static)
            Collect(collider);

        foreach (var collider in _dynamic)
            Collect(collider);
    }

    /// <summary>
    /// Gets the collection of static colliders registered in this registry.
    /// </summary>
    public IEnumerable<ICollider> StaticColliders => _static;
    
    /// <summary>
    /// Gets the collection of dynamic colliders registered in this registry.
    /// </summary>
    public IEnumerable<ICollider> DynamicColliders => _dynamic;

    /// <summary>
    /// Registers a collider with the registry. The collider is added to either the static or dynamic
    /// collection based on its <see cref="ICollider.IsStatic"/> property.
    /// </summary>
    /// <param name="collider">The collider to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collider"/> is <c>null</c>.</exception>
    public void Register(ICollider collider)
    {
        if (collider is null)
            throw new ArgumentNullException(nameof(collider));

        if (collider.IsStatic)
            _static.Add(collider);
        else
            _dynamic.Add(collider);
    }

    /// <summary>
    /// Unregisters a collider from the registry, removing it from either the static or dynamic collection
    /// based on its <see cref="ICollider.IsStatic"/> property.
    /// </summary>
    /// <param name="collider">The collider to unregister. If <c>null</c>, no action is taken.</param>
    public void Unregister(ICollider collider)
    {
        if (collider is null)
            return;

        if (collider.IsStatic)
            _static.Remove(collider);
        else
            _dynamic.Remove(collider);
    }

    /// <summary>
    /// Broad-phase query: returns colliders overlapping the given AABB that also
    /// match the provided layer mask (bitwise AND with their LayerMask / CollidesWithMask).
    /// On periodic layers, returns unique canonical identities for overlapping images.
    /// Use <see cref="QueryInstances"/> when translated bounds are needed.
    /// </summary>
    /// <param name="area">The axis-aligned bounding box to query within.</param>
    /// <param name="layerMask">The layer mask to test against each collider's <see cref="ICollider.CollidesWith"/> mask.</param>
    /// <param name="collidesWithMask">The collision mask to test against each collider's <see cref="ICollider.CollisionGroup"/>.</param>
    /// <param name="results">The list to populate with matching colliders. This list is cleared before adding results.</param>
    /// <param name="ignore">An optional collider to exclude from the results.</param>
    public void QueryAabb(
        in Aabb area,
        int layerMask,
        int collidesWithMask,
        List<ICollider> results,
        ICollider? ignore = null)
    {
        QueryInstances(area, layerMask, collidesWithMask, _instanceScratch, ignore);
        results.Clear();
        _uniqueScratch.Clear();

        foreach (var instance in _instanceScratch)
        {
            if (_uniqueScratch.Add(instance.Collider))
                results.Add(instance.Collider);
        }

        _instanceScratch.Clear();
        _uniqueScratch.Clear();
    }
}
