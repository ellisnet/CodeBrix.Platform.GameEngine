using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// The layer-wrapping contract: <see cref="SceneLayer.WrapHorizontally"/> and
/// <see cref="SceneLayer.WrapVertically"/> make a layer's content periodic across lookup,
/// rendering selection and collision queries, while the bounds-checked indexer keeps its
/// original meaning and disabled axes never repeat.
/// </summary>
public class SceneLayerWrappingTests : IDisposable
{
    private readonly List<Scene> _scenes = new();

    /// <summary>Clears the global scene registry this fixture populated.</summary>
    public void Dispose()
    {
        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();
        GC.SuppressFinalize(this);
    }

    private SceneLayer CreateLayer(
        int columnCount,
        int rowCount,
        int width = 32,
        int height = 32,
        CoordinateSystemTypes coordinateSystem = CoordinateSystemTypes.Orthogonal)
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene.AddLayer(
            columnCount: columnCount,
            rowCount: rowCount,
            width: width,
            height: height,
            zOrder: 0,
            parallax: 1f,
            coordinateSystem: coordinateSystem);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WrapGrid_wraps_only_enabled_axes_and_the_indexer_never_wraps(bool horizontal, bool vertical)
    {
        //Arrange
        var layer = CreateLayer(4, 6);
        layer.WrapHorizontally = horizontal;
        layer.WrapVertically = vertical;

        //Act + Assert
        layer.WrapGrid(new PointF(-9, 19)).Should().Be(new PointF(horizontal ? 3 : -9, vertical ? 1 : 19));
        layer[-1, 0].Should().BeNull();
        layer[4, 6].Should().BeNull();
        layer.ResolveWrappedTile(-9, 0).Should().BeSameAs(horizontal ? layer[3, 0] : null);
        layer.ResolveWrappedTile(0, 19).Should().BeSameAs(vertical ? layer[0, 1] : null);
        layer.ResolveWrappedTile(-9, 19).Should().BeSameAs(horizontal && vertical ? layer[3, 1] : null);
        layer.GetAdjacentTile(layer[0, 0]!, CardinalDirections.W).Should().BeSameAs(horizontal ? layer[3, 0] : null);
    }

    [Theory]
    [InlineData(CoordinateSystemTypes.Orthogonal)]
    [InlineData(CoordinateSystemTypes.IsometricRhombic)]
    [InlineData(CoordinateSystemTypes.IsometricAxial)]
    [InlineData(CoordinateSystemTypes.HexAxialFlatTop)]
    [InlineData(CoordinateSystemTypes.HexAxialPointedTop)]
    [InlineData(CoordinateSystemTypes.ObliqueRight)]
    [InlineData(CoordinateSystemTypes.ObliqueLeft)]
    public void GetPeriod_matches_virtual_anchors_and_rendering_keeps_canonical_identity(CoordinateSystemTypes projection)
    {
        //Arrange
        var layer = CreateLayer(4, 6, coordinateSystem: projection);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        var period = layer.GetPeriod();

        //Act + Assert - every virtual cell projects to the canonical anchor plus a whole period.
        for (int x = -9; x < 10; x++)
        {
            for (int y = -13; y < 14; y++)
            {
                var canonical = layer.WrapGrid(new PointF(x, y));
                var anchor = layer.GridToWorldPx(new PointF(x, y));
                var original = layer.GridToWorldPx(canonical);
                var offset = period.Offset((x - canonical.X) / 4, (y - canonical.Y) / 6);

                anchor.Should().Be(new PointF(original.X + offset.X, original.Y + offset.Y));
                layer.ResolveWrappedTile(x, y).Should().BeSameAs(layer[(int)canonical.X, (int)canonical.Y]);
            }
        }

        //Assert - the repeated render instance points back at the canonical tile.
        var target = period.Offset(-3, 4);
        var rect = layer[0, 0]!.DrawLocationWorld;
        rect.Offset(Point.Round(target));
        var draws = layer.GetDrawablesInWorldRect(rect).Cast<WrappedDrawable>().ToList();

        draws.Any(d => ReferenceEquals(d.Owner, layer[0, 0]) && d.Offset == target).Should().BeTrue();
    }

    [Theory]
    [InlineData(CoordinateSystemTypes.HexAxialFlatTop, true)]
    [InlineData(CoordinateSystemTypes.HexAxialPointedTop, false)]
    public void WrapGrid_defers_stagger_validation_until_use_and_allows_the_other_axis(
        CoordinateSystemTypes projection,
        bool horizontal)
    {
        //Arrange - an odd stagger axis on a 3 x 3 hex grid cannot repeat cleanly.
        var layer = CreateLayer(3, 3, coordinateSystem: projection);
        layer.WrapHorizontally = horizontal;
        layer.WrapVertically = !horizontal;

        //Act + Assert - construction succeeded; the geometry is rejected on first operational use.
        Action wrapStaggeredAxis = () => layer.WrapGrid(new PointF(-1, -1));
        wrapStaggeredAxis.Should().Throw<InvalidOperationException>();

        //Act + Assert - the opposite axis has no parity constraint on this projection.
        layer.WrapHorizontally = !horizontal;
        layer.WrapVertically = horizontal;
        Action wrapOtherAxis = () => layer.WrapGrid(new PointF(-1, -1));
        wrapOtherAxis.Should().NotThrow();

        //Act + Assert - an orthogonal grid repeats on both axes at any size.
        layer.CoordinateSystemType = CoordinateSystemTypes.Orthogonal;
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        layer.WrapGrid(new PointF(-1, -1)).Should().Be(new PointF(2, 2));
    }

    [Fact]
    public void GetDrawablesInWorldRect_repeats_past_nine_copies_and_sorts_translated_bounds()
    {
        //Arrange - a single-tile layer seen through a view five periods wide and five tall.
        var layer = CreateLayer(1, 1, 16, 16);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        //Act
        var draws = layer.GetDrawablesInWorldRect(new Rectangle(-32, -32, 80, 80)).Cast<WrappedDrawable>().ToArray();

        //Assert
        draws.Length.Should().Be(25);
        draws.All(d => ReferenceEquals(d.Owner, layer[0, 0])).Should().BeTrue();
        draws.SequenceEqual(draws.OrderBy(d => d.Offset.Y).ThenBy(d => d.Offset.X)).Should().BeTrue();
    }

    [Fact]
    public void GetWrappedOffsets_rejects_a_query_beyond_the_supported_instance_range()
    {
        //Arrange
        var layer = CreateLayer(1, 1, 16, 16);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        //Act + Assert
        Action query = () => layer.GetWrappedOffsets(new RectangleF(0, 0, 16, 16), new RectangleF(0, 0, 1e9f, 1e9f)).ToArray();
        query.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(125, 10)]
    [InlineData(10, 125)]
    [InlineData(125, 125)]
    [InlineData(-3, -3)]
    [InlineData(509, -259)]
    public void QueryInstances_uses_translated_tile_bounds(int x, int y)
    {
        //Arrange
        var layer = CreateLayer(4, 4);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        var tile = layer[0, 0]!;
        tile.CollisionsEnabled = true;
        tile.Collider!.CollisionGroup = 1;
        tile.Collider.CollidesWith = 1;

        var results = new List<ColliderInstance>();
        var area = new Aabb(x, y, x + 10, y + 10);

        //Act
        layer.ColliderRegistry.QueryInstances(area, 1, 1, results);

        //Assert - the canonical collider is returned with the bounds of the overlapping image.
        results.Should().ContainSingle();
        results[0].Collider.Should().BeSameAs(tile.Collider);
        area.Intersects(results[0].BoundsWorldPx).Should().BeTrue();
        results[0].Collider.Owner.Should().BeSameAs(tile);

        //Act + Assert - the ignored collider is excluded by canonical identity, not per image.
        layer.ColliderRegistry.QueryInstances(area, 1, 1, results, tile.Collider);
        results.Should().BeEmpty();
    }
}
