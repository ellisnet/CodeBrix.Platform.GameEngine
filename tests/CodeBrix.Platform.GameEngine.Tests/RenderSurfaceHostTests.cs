using System;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the one-render-surface-host-per-scene binding rule: a scene is claimed by the host it is
/// bound to, a failed <see cref="RenderSurfaceHost{TBackbuffer}.Bind"/> leaves both hosts on the
/// scenes they already had, and both host disposal and scene disposal release the claim.
/// </summary>
public class RenderSurfaceHostTests
{
    [Fact]
    public void Bind_throws_when_the_scene_belongs_to_another_host_and_changes_neither_host()
    {
        //Arrange
        using var scene = new Scene();
        using var firstAdapter = new FakeRenderSurfaceAdapter();
        using var secondAdapter = new FakeRenderSurfaceAdapter();
        using var firstHost = new RenderSurfaceHost<BitmapBackbuffer>(firstAdapter);
        using var secondHost = new RenderSurfaceHost<BitmapBackbuffer>(secondAdapter);
        firstHost.Bind(scene);

        //Act
        Action bindAgain = () => secondHost.Bind(scene);

        //Assert
        bindAgain.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{scene.ID}*");
        scene.BoundRenderSurfaceHost.Should().BeSameAs(firstHost);
        firstHost.Scene.Should().BeSameAs(scene);
        secondHost.Scene.Should().BeSameAs(Scene.Empty);
    }

    [Fact]
    public void Bind_releases_the_previous_scene_so_another_host_can_claim_it()
    {
        //Arrange
        using var firstScene = new Scene();
        using var secondScene = new Scene();
        using var firstAdapter = new FakeRenderSurfaceAdapter();
        using var secondAdapter = new FakeRenderSurfaceAdapter();
        using var firstHost = new RenderSurfaceHost<BitmapBackbuffer>(firstAdapter);
        using var secondHost = new RenderSurfaceHost<BitmapBackbuffer>(secondAdapter);

        //Act
        firstHost.Bind(firstScene);
        firstHost.Bind(secondScene);
        secondHost.Bind(firstScene);

        //Assert
        firstScene.BoundRenderSurfaceHost.Should().BeSameAs(secondHost);
        secondScene.BoundRenderSurfaceHost.Should().BeSameAs(firstHost);
    }

    [Fact]
    public void Dispose_releases_the_scene_binding()
    {
        //Arrange
        using var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter();
        var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene);

        //Act
        host.Dispose();

        //Assert - the scene is free for another host, and the disposed one holds it no longer.
        scene.BoundRenderSurfaceHost.Should().BeNull();
        host.Scene.Should().BeSameAs(Scene.Empty);
    }

    [Fact]
    public void Bind_binding_is_released_when_the_bound_scene_is_disposed()
    {
        //Arrange
        var scene = new Scene();
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.Bind(scene);

        //Act
        scene.Dispose();

        //Assert - this port clears the host's scene to null (rather than Scene.Empty) so the
        //  render path can skip the frame outright; see RenderToBackbuffer's null-scene guard.
        scene.BoundRenderSurfaceHost.Should().BeNull();
        host.Scene.Should().BeNull();
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(320, 200) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }
}
