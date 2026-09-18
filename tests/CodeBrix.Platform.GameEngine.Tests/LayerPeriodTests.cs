using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// The world-space repetition lattice a wrapped layer is built from: the period vectors derived
/// from the projection, the change of basis between world pixels and period coefficients, and the
/// bounded translation search the rendering and collision paths rely on.
/// </summary>
public class LayerPeriodTests : IDisposable
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

    private SceneLayer CreateLayer(int columnCount, int rowCount, int width = 32, int height = 32)
    {
        var scene = new Scene();
        _scenes.Add(scene);

        return scene.AddLayer(columnCount, rowCount, width, height);
    }

    [Fact]
    public void Create_derives_one_period_vector_per_grid_axis()
    {
        //Arrange
        var layer = CreateLayer(4, 6);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        //Act
        var period = layer.GetPeriod();

        //Assert - a whole grid width and a whole grid height in world pixels.
        period.Columns.Should().Be(new PointF(128, 0));
        period.Rows.Should().Be(new PointF(0, 192));
        period.WrapColumns.Should().BeTrue();
        period.WrapRows.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_a_layer_without_positive_grid_dimensions()
    {
        //Arrange
        var layer = CreateLayer(0, 4);
        layer.WrapHorizontally = true;

        //Act
        Action create = () => layer.GetPeriod();

        //Assert
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Coefficients_expresses_a_world_delta_in_the_period_basis()
    {
        //Arrange
        var layer = CreateLayer(4, 6);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        var period = layer.GetPeriod();

        //Act
        var coefficients = period.Coefficients(new PointF(256, -384));

        //Assert
        coefficients.X.Should().BeApproximately(2f, 0.0001f);
        coefficients.Y.Should().BeApproximately(-2f, 0.0001f);

        //Act + Assert - Offset is the inverse of Coefficients.
        period.Offset(coefficients.X, coefficients.Y).Should().Be(new PointF(256, -384));
    }

    [Fact]
    public void Nearest_returns_the_closest_image_on_a_single_wrapped_axis()
    {
        //Arrange - columns repeat every 128 px; rows do not repeat at all.
        var layer = CreateLayer(4, 4);
        layer.WrapHorizontally = true;
        var period = layer.GetPeriod();

        //Act - a point just inside the right edge, seen from just inside the left edge.
        var nearest = period.Nearest(new PointF(127, 16), new PointF(1, 16));

        //Assert - the image one period to the left is closer than the canonical point.
        nearest.Should().Be(new PointF(-1, 16));

        //Act + Assert - the non-wrapped axis is never moved.
        period.Nearest(new PointF(0, 300), new PointF(0, 0)).Should().Be(new PointF(0, 300));
    }

    [Fact]
    public void Offsets_yields_only_the_zero_translation_when_no_axis_wraps()
    {
        //Arrange
        var layer = CreateLayer(4, 4);
        var period = layer.GetPeriod();

        //Act
        var offsets = period.Offsets(new RectangleF(0, 0, 32, 32), new RectangleF(0, 0, 512, 512)).ToArray();

        //Assert
        offsets.Should().ContainSingle();
        offsets[0].Should().Be(PointF.Empty);
    }

    [Fact]
    public void Offsets_covers_every_intersecting_image_on_both_axes()
    {
        //Arrange - a 32 px period seen through a 96 px window starting one period back.
        var layer = CreateLayer(2, 2, 16, 16);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;
        var period = layer.GetPeriod();

        //Act
        var offsets = period.Offsets(new RectangleF(0, 0, 32, 32), new RectangleF(-32, -32, 96, 96)).ToArray();

        //Assert - three columns by three rows of images touch the window.
        offsets.Length.Should().Be(9);
        offsets.All(o => Math.Abs(o.X) <= 32 && Math.Abs(o.Y) <= 32).Should().BeTrue();
    }
}
