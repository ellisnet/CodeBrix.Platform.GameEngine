using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="Tile"/> lifetime behaviour that is not tied to one concrete tile kind: now
/// that layer tiles carry real colliders, disposing a tile whose scene layer was never assigned
/// (a deserialized tile that is discarded before rehydration, for instance) must not throw.
/// </summary>
public class TileTests : IDisposable
{
    /// <summary>Clears the global scene registry in case a test populated it.</summary>
    public void Dispose()
    {
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Dispose_does_not_throw_for_a_tile_with_no_scene_layer()
    {
        //Arrange - the deserialization constructor leaves parentSceneLayer null.
        var tile = new SceneLayerTile();

        //Act
        var act = () => tile.Dispose();

        //Assert
        act.Should().NotThrow();
        tile.Collider.Should().BeNull();
    }

    [Fact]
    public void Dispose_unregisters_the_collider_of_a_layer_tile()
    {
        //Arrange
        var scene = new Scene();
        var layer = scene.AddLayer(columnCount: 2, rowCount: 2, width: 16, height: 16, zOrder: 0, parallax: 1f);
        var tile = layer[0, 0]!;
        tile.CollisionsEnabled = true;
        tile.Collider.Should().NotBeNull();

        //Act
        tile.Dispose();

        //Assert
        tile.Collider.Should().BeNull();
        layer.ColliderRegistry.StaticColliders.Should().NotContain(c => ReferenceEquals(c.Owner, tile));
        layer.ColliderRegistry.DynamicColliders.Should().NotContain(c => ReferenceEquals(c.Owner, tile));

        scene.Dispose();
    }

    [Fact]
    public void CompareTo_reuses_the_captured_sort_key_for_the_whole_render_pass()
    {
        //Arrange - "lower" starts below "upper" in world space, so it sorts after it.
        using var scene = new Scene();
        var view = CreateView(scene);
        var upper = new SortableTestTile { DrawLocation = new Rectangle(0, 0, 10, 10) };
        var lower = new SortableTestTile { DrawLocation = new Rectangle(0, 100, 10, 10) };

        //Act - the first comparison captures both keys; moving a tile afterwards must not be
        //  seen by later comparisons in the SAME pass, so one sort cannot change its own ordering.
        RenderContext.Push(view, tick: 1);

        try
        {
            int before = lower.CompareTo(upper);
            lower.DrawLocation = new Rectangle(0, -500, 10, 10);
            int after = lower.CompareTo(upper);

            //Assert
            before.Should().BeGreaterThan(0);
            after.Should().BeGreaterThan(0);
        }
        finally
        {
            RenderContext.Pop();
        }
    }

    [Fact]
    public void CompareTo_recaptures_the_sort_key_in_the_next_render_pass()
    {
        //Arrange
        using var scene = new Scene();
        var view = CreateView(scene);
        var upper = new SortableTestTile { DrawLocation = new Rectangle(0, 0, 10, 10) };
        var moved = new SortableTestTile { DrawLocation = new Rectangle(0, 100, 10, 10) };

        //Act
        RenderContext.Push(view, tick: 1);
        int firstPass;

        try
        {
            firstPass = moved.CompareTo(upper);
            moved.DrawLocation = new Rectangle(0, -500, 10, 10);
        }
        finally
        {
            RenderContext.Pop();
        }

        RenderContext.Push(view, tick: 2);
        int secondPass;

        try
        {
            secondPass = moved.CompareTo(upper);
        }
        finally
        {
            RenderContext.Pop();
        }

        //Assert - a new pass has a new PassId, so the stale key is discarded.
        firstPass.Should().BeGreaterThan(0);
        secondPass.Should().BeLessThan(0);
    }

    [Fact]
    public void CompareTo_uses_live_values_outside_a_render_pass()
    {
        //Arrange
        var upper = new SortableTestTile { DrawLocation = new Rectangle(0, 0, 10, 10) };
        var moved = new SortableTestTile { DrawLocation = new Rectangle(0, 100, 10, 10) };

        //Act - no RenderContext is current, so nothing is cached between comparisons.
        RenderContext.Current.Should().BeNull();
        int before = moved.CompareTo(upper);
        moved.DrawLocation = new Rectangle(0, -500, 10, 10);
        int after = moved.CompareTo(upper);

        //Assert
        before.Should().BeGreaterThan(0);
        after.Should().BeLessThan(0);
    }

    private static View CreateView(Scene scene)
    {
        var viewport = new Viewport
        {
            TargetRectPx = new Rectangle(0, 0, 320, 200),
            Zoom = 1f
        };

        var camera = new Camera(scene);
        var view = new View(camera, viewport);
        camera.SnapTo(PointF.Empty);

        return view;
    }

    /// <summary>
    /// A tile whose sort inputs can be changed between comparisons, so the per-render-pass sort-key
    /// cache can be observed directly. Only the members the comparison path reads are meaningful.
    /// </summary>
    private sealed class SortableTestTile : Tile
    {
        /// <summary>The world rectangle the sort key is derived from.</summary>
        internal Rectangle DrawLocation { get; set; }

        /// <inheritdoc />
        public override bool IsPositionFixed => false;

        /// <inheritdoc />
        public override Rectangle DrawLocationWorld => DrawLocation;

        /// <inheritdoc />
        public override PointF SceneLayerCoordinates => PointF.Empty;

        /// <inheritdoc />
        /// <remarks>The comparison path never reads the layer, and this tile joins none.</remarks>
        public override SceneLayer SceneLayer => null!;
    }
}
