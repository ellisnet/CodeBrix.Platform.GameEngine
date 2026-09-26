using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>Covers <see cref="DrawListSnapshot"/>: it copies what it is given, and hit tests take the topmost region.</summary>
public class DrawListSnapshotTests
{
    [Fact]
    public void Empty_has_no_commands_no_regions_and_number_zero()
    {
        //Arrange + Act
        var empty = DrawListSnapshot.Empty;

        //Assert
        empty.Commands.Should().BeEmpty();
        empty.HitRegions.Should().BeEmpty();
        empty.Number.Should().Be(0L);
        empty.HitTest(0, 0).Should().BeNull();
    }

    [Fact]
    public void the_public_constructor_copies_the_commands_so_later_changes_do_not_reach_the_snapshot()
    {
        //Arrange
        var commands = new List<DrawCommand> { DrawCommand.ForCircle(1, 1, 1, SKColors.Red) };

        //Act
        var snapshot = new DrawListSnapshot(commands, number: 7);
        commands.Add(DrawCommand.ForCircle(2, 2, 2, SKColors.Blue));

        //Assert
        snapshot.Commands.Should().HaveCount(1);
        snapshot.Number.Should().Be(7L);
    }

    [Fact]
    public void the_public_constructor_rejects_null_commands()
    {
        //Arrange
        Action act = () => _ = new DrawListSnapshot(null!);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_command_collection_is_read_only()
    {
        //Arrange
        var snapshot = new DrawListSnapshot(new[] { DrawCommand.ForCircle(1, 1, 1, SKColors.Red) });

        //Act
        var asArray = snapshot.Commands as IList<DrawCommand>;

        //Assert
        asArray!.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void HitTest_returns_the_last_added_region_where_two_overlap()
    {
        //Arrange
        var regions = new[]
        {
            new DrawHitRegion(50, 50, 100, 100, "under"),
            new DrawHitRegion(50, 50, 20, 20, "over"),
        };
        var snapshot = new DrawListSnapshot(Array.Empty<DrawCommand>(), regions);

        //Act
        var center = snapshot.HitTest(50, 50);
        var corner = snapshot.HitTest(5, 5);
        var outside = snapshot.HitTest(500, 5);

        //Assert
        center!.Value.Id.Should().Be("over");
        corner!.Value.Id.Should().Be("under");
        outside.Should().BeNull();
    }

    [Theory]
    [InlineData(40.0, 45.0, true)]
    [InlineData(60.0, 55.0, true)]
    [InlineData(60.1, 50.0, false)]
    [InlineData(50.0, 44.9, false)]
    public void DrawHitRegion_Contains_uses_a_box_centred_on_the_point_edges_included(double x, double y, bool expected) =>
        new DrawHitRegion(50, 50, 20, 10, "r").Contains(x, y).Should().Be(expected);
}
