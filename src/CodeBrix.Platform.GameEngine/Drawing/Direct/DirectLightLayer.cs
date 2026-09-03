using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing.Direct; //was previously: Gondwana.Drawing.Direct;

/// <summary>
/// Convenience owner for a group of scene-layer radial lights.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally not a new <see cref="SceneLayer"/> type. Each light is still a bounded
/// <see cref="DirectRadialLight"/> registered with <see cref="DirectDrawingManager"/>, so the existing
/// scene-layer drawable query can include only lights whose world bounds intersect the dirty world rect.
/// </para>
/// <para>
/// Use this class when a game wants a clear logical owner for torch/lamp lights without changing the
/// renderer's composition model.
/// </para>
/// </remarks>
public sealed class DirectLightLayer : IDisposable
{
    private readonly List<DirectRadialLight> _lights = [];

    /// <summary>
    /// Occurs after a light has been added to this owner.
    /// </summary>
    public event Action<DirectRadialLight>? LightAdded;

    /// <summary>
    /// Occurs before a light is disposed and removed from this owner.
    /// </summary>
    public event Action<DirectRadialLight>? LightRemoving;

    /// <summary>
    /// Initializes a new light owner for the given render surface and scene layer.
    /// </summary>
    /// <param name="renderSurfaceHost">The render surface host used by lights created from this owner.</param>
    /// <param name="sceneLayer">The scene layer where the lights are drawn.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="renderSurfaceHost"/> or <paramref name="sceneLayer"/> is <see langword="null"/>.
    /// </exception>
    public DirectLightLayer(RenderSurfaceHostBase renderSurfaceHost, SceneLayer sceneLayer)
    {
        RenderSurfaceHost = renderSurfaceHost ?? throw new ArgumentNullException(nameof(renderSurfaceHost));
        SceneLayer = sceneLayer ?? throw new ArgumentNullException(nameof(sceneLayer));
    }

    /// <summary>
    /// Gets the render surface host used by lights created from this owner.
    /// </summary>
    /// <value>The owning <see cref="RenderSurfaceHostBase"/>.</value>
    public RenderSurfaceHostBase RenderSurfaceHost { get; }

    /// <summary>
    /// Gets the scene layer where lights are drawn.
    /// </summary>
    /// <value>The <see cref="SceneLayer"/> that every light in this owner belongs to.</value>
    public SceneLayer SceneLayer { get; }

    /// <summary>
    /// Gets the lights owned by this layer.
    /// </summary>
    /// <value>A live, ordered view of the owned <see cref="DirectRadialLight"/> instances.</value>
    public IReadOnlyList<DirectRadialLight> Lights => _lights;

    /// <summary>
    /// Gets or sets the Z-order assigned to newly-created lights.
    /// </summary>
    /// <value>The Z-order given to lights created by <see cref="AddTorchLight"/>; the default is 10,000.</value>
    public int DefaultZOrder { get; set; } = 10_000;

    /// <summary>
    /// Creates a warm, screen-blended torch-style light.
    /// </summary>
    /// <param name="centerWorldPx">The center of the light in world pixels.</param>
    /// <param name="radiusWorldPx">The light radius in world pixels.</param>
    /// <param name="color">The light color, or <see langword="null"/> for the default warm torch color.</param>
    /// <param name="nickname">Optional name used by <see cref="DirectDrawingManager"/>.</param>
    /// <returns>The newly created light, already owned by this layer.</returns>
    public DirectRadialLight AddTorchLight(
        PointF centerWorldPx,
        float radiusWorldPx,
        Color? color = null,
        string? nickname = null)
    {
        var light = new DirectRadialLight(
            color ?? Color.FromArgb(180, 255, 190, 80),
            RenderSurfaceHost,
            SceneLayer,
            centerWorldPx,
            radiusWorldPx,
            nickname)
        {
            ZOrder = DefaultZOrder,
            BlendMode = SKBlendMode.Screen,
            HotspotRadiusRatio = 0.06f,
            MidpointRadiusRatio = 0.55f,
            MidpointIntensityRatio = 0.35f
        };

        _lights.Add(light);
        LightAdded?.Invoke(light);
        return light;
    }

    /// <summary>
    /// Removes and disposes a light owned by this layer.
    /// </summary>
    /// <param name="light">The light to remove.</param>
    /// <returns>
    /// <see langword="true"/> if the light was owned by this layer and has been removed and disposed;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public bool Remove(DirectRadialLight light)
    {
        if (!_lights.Remove(light))
            return false;

        LightRemoving?.Invoke(light);
        light.Dispose();
        return true;
    }

    /// <summary>
    /// Disposes every light owned by this layer.
    /// </summary>
    public void Clear()
    {
        foreach (var light in _lights.ToArray())
            Remove(light);

        _lights.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Clear();
        LightAdded = null;
        LightRemoving = null;
    }
}
