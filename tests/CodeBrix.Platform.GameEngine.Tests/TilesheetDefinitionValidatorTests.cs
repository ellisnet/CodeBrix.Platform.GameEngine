using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the authoring diagnostics produced by <see cref="TilesheetDefinitionValidator"/>: region
/// naming, area and tile geometry, padding/margin/overhang signs, collision metadata, frame
/// coordinates, null entries, and the frame-grid arithmetic exposed by
/// <see cref="TilesheetDefinitionValidator.GridSize"/>.
/// </summary>
/// <remarks>
/// The validator never creates runtime tiles and never decodes an image, so these tests are pure
/// and touch no global engine state.
/// </remarks>
public class TilesheetDefinitionValidatorTests
{
    private static TilesheetRegionDefinition CreateRegion(string? name = null) =>
        new()
        {
            Name = name ?? TilesheetRegion.DefaultRegionName,
            Area = new Rectangle(0, 0, 32, 16),
            TileSize = new Size(16, 16)
        };

    private static TilesheetDefinition CreateDefinition(params TilesheetRegionDefinition[] regions) =>
        new()
        {
            Name = "Sheet",
            Image = new TilesheetImageDefinition { FilePath = "sheet.png" },
            Regions = new List<TilesheetRegionDefinition>(regions)
        };

    [Fact]
    public void Validate_returns_no_messages_for_a_well_formed_definition()
    {
        //Arrange
        var region = CreateRegion();
        region.Frames =
        [
            new TilesheetFrameDefinition { XTile = 0, YTile = 0, CollisionType = TileCollisionType.Blocking },
            new TilesheetFrameDefinition { XTile = 1, YTile = 0, CollisionAdjust = new CollisionAdjust(2, 2, 2, 2) }
        ];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_a_null_definition()
    {
        //Arrange
        var act = () => TilesheetDefinitionValidator.Validate(null!);

        //Act / Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_reports_a_region_whose_name_is_empty()
    {
        //Arrange
        var region = CreateRegion(name: "   ");

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region));

        //Assert
        errors.Should().ContainSingle(message => message.Contains("name is empty"));
    }

    [Fact]
    public void Validate_reports_two_regions_that_share_a_name_ignoring_case()
    {
        //Arrange
        var first = CreateRegion("Terrain");
        var second = CreateRegion("TERRAIN");

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(first, second));

        //Assert
        errors.Should().ContainSingle(message => message.Contains("duplicate name"));
    }

    [Theory]
    [InlineData(0, 16, 16, 16)]
    [InlineData(32, 0, 16, 16)]
    [InlineData(32, 16, 0, 16)]
    [InlineData(32, 16, 16, -1)]
    public void Validate_reports_non_positive_area_and_tile_dimensions(int areaWidth, int areaHeight, int tileWidth, int tileHeight)
    {
        //Arrange
        var region = CreateRegion();
        region.Area = new Rectangle(0, 0, areaWidth, areaHeight);
        region.TileSize = new Size(tileWidth, tileHeight);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region));

        //Assert - the region is abandoned after this message, so nothing else is reported for it.
        errors.Should().ContainSingle(message => message.Contains("area and tile dimensions must be positive"));
    }

    [Theory]
    [InlineData(0, 0, 64, 16)]
    [InlineData(0, 0, 32, 64)]
    [InlineData(-1, 0, 32, 16)]
    [InlineData(0, -1, 32, 16)]
    public void Validate_reports_an_area_that_falls_outside_the_source_image(int x, int y, int width, int height)
    {
        //Arrange
        var region = CreateRegion();
        region.Area = new Rectangle(x, y, width, height);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().Contain(message => message.Contains("area is outside the source image"));
    }

    [Fact]
    public void Validate_ignores_the_image_bounds_when_the_dimensions_are_unknown()
    {
        //Arrange
        var region = CreateRegion();
        region.Area = new Rectangle(0, 0, 4096, 4096);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region));

        //Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_negative_tile_padding_and_region_margins()
    {
        //Arrange
        var region = CreateRegion();
        region.TilePadding = new Spacing(-1, 0, 0, 0);
        region.RegionMargin = new Spacing(0, 0, 0, -2);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("tile padding and region margins cannot be negative"));
    }

    [Fact]
    public void Validate_reports_negative_overhang()
    {
        //Arrange
        var region = CreateRegion();
        region.Overhang = new Spacing(0, 0, 0, -3);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("overhang cannot be negative"));
    }

    [Fact]
    public void Validate_reports_a_layout_that_holds_no_complete_frame()
    {
        //Arrange
        var region = CreateRegion();
        region.TileSize = new Size(64, 64);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("layout contains no complete frames"));
    }

    [Fact]
    public void Validate_accepts_negative_collision_adjustments_that_expand_the_collision_area()
    {
        //Arrange - a negative adjustment grows the collision rectangle past the frame on purpose.
        var region = CreateRegion();
        region.CollisionAdjust = new CollisionAdjust(-4, -4, -4, -4);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_a_region_collision_adjustment_that_inverts_the_collision_rectangle()
    {
        //Arrange - left + right exceed the 16 px tile width.
        var region = CreateRegion();
        region.CollisionAdjust = new CollisionAdjust(0, 0, 10, 10);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("invert the frame's collision rectangle"));
    }

    [Fact]
    public void Validate_reports_a_frame_collision_adjustment_that_inverts_the_collision_rectangle()
    {
        //Arrange - top + bottom exceed the 16 px tile height for this one frame only.
        var region = CreateRegion();
        region.Frames = [new TilesheetFrameDefinition { XTile = 1, YTile = 0, CollisionAdjust = new CollisionAdjust(9, 9, 0, 0) }];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("frame (1, 0)") &&
                                                message.Contains("invert the frame's collision rectangle"));
    }

    [Fact]
    public void Validate_reports_an_unknown_region_collision_type()
    {
        //Arrange
        var region = CreateRegion();
        region.CollisionType = (TileCollisionType)99;

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("unknown collision type"));
    }

    [Fact]
    public void Validate_reports_an_unknown_frame_collision_type()
    {
        //Arrange
        var region = CreateRegion();
        region.Frames = [new TilesheetFrameDefinition { XTile = 0, YTile = 0, CollisionType = (TileCollisionType)42 }];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("frame (0, 0)") &&
                                                message.Contains("unknown collision type"));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Validate_reports_a_frame_outside_the_frame_grid(int xTile, int yTile)
    {
        //Arrange - the 32x16 area with 16x16 tiles is a 2x1 grid.
        var region = CreateRegion();
        region.Frames = [new TilesheetFrameDefinition { XTile = xTile, YTile = yTile }];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("coordinates are outside the frame grid"));
    }

    [Fact]
    public void Validate_reports_duplicate_frame_coordinates()
    {
        //Arrange
        var region = CreateRegion();
        region.Frames =
        [
            new TilesheetFrameDefinition { XTile = 0, YTile = 0 },
            new TilesheetFrameDefinition { XTile = 0, YTile = 0 }
        ];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("duplicate frame metadata"));
    }

    [Fact]
    public void Validate_reports_a_null_region_and_keeps_checking_the_remaining_regions()
    {
        //Arrange
        var broken = CreateRegion("Second");
        broken.TileSize = new Size(0, 16);

        var definition = CreateDefinition();
        definition.Regions.Add(null!);
        definition.Regions.Add(broken);

        //Act
        var errors = TilesheetDefinitionValidator.Validate(definition, imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("Regions contains a null entry"));
        errors.Should().ContainSingle(message => message.Contains("area and tile dimensions must be positive"));
    }

    [Fact]
    public void Validate_reports_a_null_frame_and_keeps_checking_the_remaining_frames()
    {
        //Arrange
        var region = CreateRegion();
        region.Frames = [null!, new TilesheetFrameDefinition { XTile = 7, YTile = 0 }];

        //Act
        var errors = TilesheetDefinitionValidator.Validate(CreateDefinition(region), imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().ContainSingle(message => message.Contains("null frame metadata"));
        errors.Should().ContainSingle(message => message.Contains("coordinates are outside the frame grid"));
    }

    [Fact]
    public void Validate_tolerates_a_definition_with_no_regions()
    {
        //Arrange
        var definition = CreateDefinition();

        //Act
        var errors = TilesheetDefinitionValidator.Validate(definition, imageWidth: 32, imageHeight: 16);

        //Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void GridSize_removes_the_region_margin_and_adds_the_tile_padding_to_every_frame()
    {
        //Arrange - frame pitch is 16 + 2 + 2 = 20 across and 16 + 1 + 1 = 18 down; the margins
        //remove 10 px of width and 6 px of height before the division.
        var region = new TilesheetRegionDefinition
        {
            Area = new Rectangle(0, 0, 100, 60),
            TileSize = new Size(16, 16),
            TilePadding = new Spacing(2, 1, 2, 1),
            RegionMargin = new Spacing(5, 3, 5, 3)
        };

        //Act
        var (columns, rows) = TilesheetDefinitionValidator.GridSize(region);

        //Assert
        columns.Should().Be(4);
        rows.Should().Be(3);
    }

    [Fact]
    public void GridSize_returns_zero_when_the_frame_pitch_is_not_positive()
    {
        //Arrange
        var region = new TilesheetRegionDefinition
        {
            Area = new Rectangle(0, 0, 64, 64),
            TileSize = new Size(0, 0)
        };

        //Act
        var (columns, rows) = TilesheetDefinitionValidator.GridSize(region);

        //Assert
        columns.Should().Be(0);
        rows.Should().Be(0);
    }

    [Fact]
    public void GridSize_returns_zero_when_the_margins_consume_the_whole_area()
    {
        //Arrange
        var region = new TilesheetRegionDefinition
        {
            Area = new Rectangle(0, 0, 20, 20),
            TileSize = new Size(16, 16),
            RegionMargin = new Spacing(30, 30, 30, 30)
        };

        //Act
        var (columns, rows) = TilesheetDefinitionValidator.GridSize(region);

        //Assert
        columns.Should().Be(0);
        rows.Should().Be(0);
    }
}
