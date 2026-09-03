using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// The scene-layer coordinate contract: the serialized values behind
/// <see cref="CoordinateSystemTypes"/>, the tightly packed affine basis used by
/// <see cref="CoordinateSystemTypes.IsometricAxial"/>, and the mirrored shear of the two
/// oblique projections.
/// </summary>
public class SceneLayerCoordinateSystemTests : IDisposable
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

    [Fact]
    public void CoordinateSystemTypes_preserve_the_serialized_oblique_right_value()
    {
        //Arrange + Act + Assert - layers saved before the rename still load as ObliqueRight.
        ((int)CoordinateSystemTypes.ObliqueRight).Should().Be(5);
        ((int)CoordinateSystemTypes.ObliqueLeft).Should().Be(6);
    }

    [Fact]
    public void IsometricAxial_uses_a_tightly_packed_affine_basis()
    {
        //Arrange
        var layer = CreateLayer(4, 4, width: 64, height: 32, CoordinateSystemTypes.IsometricAxial);

        //Act + Assert - columns advance by (W, 0), rows by (W/2, H/2).
        layer.GridToWorldPx(new PointF(0f, 0f)).Should().Be(new PointF(0f, 0f));
        layer.GridToWorldPx(new PointF(1f, 0f)).Should().Be(new PointF(64f, 0f));
        layer.GridToWorldPx(new PointF(0f, 1f)).Should().Be(new PointF(32f, 16f));
        layer.GridToWorldPx(new PointF(1f, 1f)).Should().Be(new PointF(96f, 16f));
        layer.GridToWorldPx(new PointF(0f, 2f)).Should().Be(new PointF(64f, 32f));
    }

    [Fact]
    public void IsometricAxial_world_to_grid_inverts_the_affine_basis()
    {
        //Arrange
        var layer = CreateLayer(8, 8, width: 64, height: 32, CoordinateSystemTypes.IsometricAxial);

        //Act + Assert - including a fractional row, which must survive the inverse shear.
        layer.WorldPxToGrid(new PointF(256f, 32f)).Should().Be(new PointF(3f, 2f));
        layer.WorldPxToGrid(new PointF(112f, 24f)).Should().Be(new PointF(1f, 1.5f));
    }

    [Fact]
    public void IsometricAxial_adjacent_rows_share_a_diamond_edge()
    {
        //Arrange
        var layer = CreateLayer(2, 2, width: 64, height: 32, CoordinateSystemTypes.IsometricAxial);

        //Act
        var first = layer[0, 0]!.OutlinePointsWorld;
        var nextRow = layer[0, 1]!.OutlinePointsWorld;

        //Assert - the next row's top and left vertices are the previous row's right and bottom.
        nextRow[0].Should().Be(first[1]);
        nextRow[3].Should().Be(first[2]);
    }

    [Theory]
    [InlineData(CoordinateSystemTypes.ObliqueRight, 40f)]
    [InlineData(CoordinateSystemTypes.ObliqueLeft, -40f)]
    public void Oblique_rows_recede_in_the_selected_direction(
        CoordinateSystemTypes coordinateSystem,
        float expectedRowX)
    {
        //Arrange
        var layer = CreateLayer(4, 4, width: 80, height: 48, coordinateSystem);

        //Act + Assert - columns stay horizontal; the row shear picks the direction.
        layer.GridToWorldPx(new PointF(1f, 0f)).Should().Be(new PointF(40f, 0f));
        layer.GridToWorldPx(new PointF(0f, 1f)).Should().Be(new PointF(expectedRowX, 48f));
    }

    [Theory]
    [InlineData(CoordinateSystemTypes.ObliqueRight, 200f)]
    [InlineData(CoordinateSystemTypes.ObliqueLeft, 40f)]
    public void Oblique_world_to_grid_inverts_the_selected_shear(
        CoordinateSystemTypes coordinateSystem,
        float anchorX)
    {
        //Arrange
        var layer = CreateLayer(5, 5, width: 80, height: 48, coordinateSystem);
        var world = new PointF(anchorX, 96f);

        //Act + Assert
        layer.GridToWorldPx(new PointF(3f, 2f)).Should().Be(world);
        layer.WorldPxToGrid(world).Should().Be(new PointF(3f, 2f));
    }

    [Fact]
    public void ObliqueLeft_polygon_mirrors_ObliqueRight()
    {
        //Arrange
        var right = CreateLayer(1, 1, width: 80, height: 48, CoordinateSystemTypes.ObliqueRight);
        var left = CreateLayer(1, 1, width: 80, height: 48, CoordinateSystemTypes.ObliqueLeft);

        //Act
        var rightOutline = right[0, 0]!.OutlinePointsWorld;
        var leftOutline = left[0, 0]!.OutlinePointsWorld;

        //Assert - the parallelogram leans the other way, with the vertex order mirrored.
        rightOutline.Should().Equal(
            new Point(0, 0),
            new Point(40, 0),
            new Point(80, 48),
            new Point(40, 48));

        leftOutline.Should().Equal(
            new Point(40, 0),
            new Point(80, 0),
            new Point(40, 48),
            new Point(0, 48));
    }

    [Fact]
    public void CoordinateSystemType_round_trips_both_oblique_implementations()
    {
        //Arrange
        var layer = CreateLayer(2, 2);

        //Act + Assert
        layer.CoordinateSystemType = CoordinateSystemTypes.ObliqueRight;
        layer.CoordinateSystemType.Should().Be(CoordinateSystemTypes.ObliqueRight);

        layer.CoordinateSystemType = CoordinateSystemTypes.ObliqueLeft;
        layer.CoordinateSystemType.Should().Be(CoordinateSystemTypes.ObliqueLeft);
    }
}
