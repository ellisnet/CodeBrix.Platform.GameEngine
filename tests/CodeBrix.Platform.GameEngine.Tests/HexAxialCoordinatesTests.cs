using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the two hexagonal coordinate systems (<see cref="CoordinateSystemTypes.HexAxialFlatTop"/> and
/// <see cref="CoordinateSystemTypes.HexAxialPointedTop"/>): integer grid positions keep their historical
/// anchors, and fractional grid positions interpolate the staggered offset instead of snapping to a cell.
/// </summary>
public class HexAxialCoordinatesTests
{
    private static SceneLayer CreateLayer(CoordinateSystemTypes coordinateSystem) =>
        new(columnCount: 4, rowCount: 4, width: 64, height: 32, coordinateSystem: coordinateSystem);

    [Fact]
    public void GridToWorldPx_flat_top_keeps_the_original_anchors_for_integer_coordinates()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialFlatTop);

        //Act + Assert (column 1 is odd, so it is staggered down by half the tile height)
        layer.GridToWorldPx(new PointF(0f, 0f)).Should().Be(new PointF(0f, 0f));
        layer.GridToWorldPx(new PointF(1f, 0f)).Should().Be(new PointF(48f, 16f));
        layer.GridToWorldPx(new PointF(2f, 1f)).Should().Be(new PointF(96f, 32f));
        layer.GridToWorldPx(new PointF(3f, 2f)).Should().Be(new PointF(144f, 80f));
    }

    [Fact]
    public void GridToWorldPx_flat_top_places_a_half_column_midway_including_half_the_stagger()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialFlatTop);

        //Act
        var halfway = layer.GridToWorldPx(new PointF(0.5f, 0f));

        //Assert (x is halfway between 0 and 48; y carries half of the 16 px column stagger)
        halfway.Should().Be(new PointF(24f, 8f));
    }

    [Fact]
    public void GridToWorldPx_flat_top_interpolates_a_fractional_row()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialFlatTop);

        //Act
        var quarterRow = layer.GridToWorldPx(new PointF(0f, 0.25f));

        //Assert
        quarterRow.Should().Be(new PointF(0f, 8f));
    }

    [Fact]
    public void GridToWorldPx_pointed_top_keeps_the_original_anchors_for_integer_coordinates()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialPointedTop);

        //Act + Assert (row 1 is odd, so it is staggered right by half the tile width)
        layer.GridToWorldPx(new PointF(0f, 0f)).Should().Be(new PointF(0f, 0f));
        layer.GridToWorldPx(new PointF(0f, 1f)).Should().Be(new PointF(32f, 24f));
        layer.GridToWorldPx(new PointF(1f, 2f)).Should().Be(new PointF(64f, 48f));
        layer.GridToWorldPx(new PointF(2f, 3f)).Should().Be(new PointF(160f, 72f));
    }

    [Fact]
    public void GridToWorldPx_pointed_top_places_a_half_row_midway_including_half_the_stagger()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialPointedTop);

        //Act
        var halfway = layer.GridToWorldPx(new PointF(0f, 0.5f));

        //Assert (y is halfway between 0 and 24; x carries half of the 32 px row stagger)
        halfway.Should().Be(new PointF(16f, 12f));
    }

    [Fact]
    public void GridToWorldPx_pointed_top_interpolates_a_fractional_column()
    {
        //Arrange
        using var layer = CreateLayer(CoordinateSystemTypes.HexAxialPointedTop);

        //Act
        var quarterColumn = layer.GridToWorldPx(new PointF(0.25f, 0f));

        //Assert
        quarterColumn.Should().Be(new PointF(16f, 0f));
    }
}
