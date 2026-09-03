using System;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Minimal render-surface host used by direct-drawing unit tests: it owns a real
/// <see cref="Scenes.Scene"/> and <see cref="Rendering.Views.ViewManager"/> so drawings can be
/// constructed and positioned, but it never renders. Tests that need pixels draw into their own
/// <see cref="BitmapBackbuffer"/> and call <c>Draw</c> directly.
/// </summary>
internal sealed class TestRenderSurfaceHost : RenderSurfaceHostBase
{
    /// <summary>Initializes a host with an empty scene and an empty view manager.</summary>
    internal TestRenderSurfaceHost()
    {
        Scene = new Scene();
        ViewManager = new ViewManager(this);
    }

    /// <inheritdoc />
    public override BackbufferBase Backbuffer =>
        throw new NotSupportedException("The test host does not render.");

    /// <inheritdoc />
    public override Scene Scene { get; }

    /// <inheritdoc />
    public override RenderSurfaceAdapterBase? RenderSurfaceAdapter => null;

    /// <inheritdoc />
    public override ViewManager ViewManager { get; }

    internal override void RenderToBackbuffer(long tick)
    {
    }

    internal override void PresentBackbufferToAdapter()
    {
    }
}
