using System;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the tilesheet's region lookup, which is backed by a case-insensitive name index so that
/// a sheet carrying hundreds of single-tile regions (the shape an atlas produces) still resolves a
/// frame without scanning.
/// </summary>
public class TilesheetTests
{
    private const int TileSizePx = 16;
    private const int SheetTilesPerRow = 20;
    private const int AtlasRegionCount = 300;

    [Fact]
    public void GetRegion_matches_a_name_case_insensitively()
    {
        //Arrange
        using var tilesheet = CreateSheet();
        var added = tilesheet.AddRegion("BallBlue", new Rectangle(0, 0, TileSizePx, TileSizePx), new Size(TileSizePx, TileSizePx));

        //Act
        var exact = tilesheet.GetRegion("BallBlue");
        var lowered = tilesheet.GetRegion("ballblue");
        var uppered = tilesheet.GetRegion("BALLBLUE");

        //Assert
        exact.Should().BeSameAs(added);
        lowered.Should().BeSameAs(added);
        uppered.Should().BeSameAs(added);
    }

    [Fact]
    public void GetRegion_returns_null_for_an_unknown_or_empty_name()
    {
        //Arrange
        using var tilesheet = CreateSheet();
        tilesheet.AddRegion("ballBlue", new Rectangle(0, 0, TileSizePx, TileSizePx), new Size(TileSizePx, TileSizePx));

        //Act
        var unknown = tilesheet.GetRegion("ballRed");
        var empty = tilesheet.GetRegion("   ");

        //Assert
        unknown.Should().BeNull();
        empty.Should().BeNull();
    }

    [Fact]
    public void GetRegion_resolves_every_region_of_a_sheet_with_many_regions()
    {
        //Arrange
        using var tilesheet = CreateSheet();
        AddAtlasRegions(tilesheet, AtlasRegionCount);

        //Act & Assert - every single-tile region resolves to its own area.
        for (int index = 0; index < AtlasRegionCount; index++)
        {
            var region = tilesheet.GetRegion(RegionName(index));

            region.Should().NotBeNull();
            region!.Area.Should().Be(RegionArea(index));
            region.Columns.Should().Be(1);
            region.Rows.Should().Be(1);
        }

        tilesheet.Regions.Count.Should().Be(AtlasRegionCount + 1);
        tilesheet[RegionName(7), 0, 0].RegionName.Should().Be(RegionName(7));
    }

    [Fact]
    public void RemoveRegion_drops_the_name_from_the_lookup()
    {
        //Arrange
        using var tilesheet = CreateSheet();
        AddAtlasRegions(tilesheet, AtlasRegionCount);

        //Act
        bool removed = tilesheet.RemoveRegion(RegionName(42));

        //Assert
        removed.Should().BeTrue();
        tilesheet.GetRegion(RegionName(42)).Should().BeNull();
        tilesheet.GetRegion(RegionName(41)).Should().NotBeNull();
        tilesheet.GetRegion(RegionName(43)).Should().NotBeNull();

        //Act - the freed name can be taken again.
        var reAdded = tilesheet.AddRegion(RegionName(42), RegionArea(42), new Size(TileSizePx, TileSizePx));

        //Assert
        tilesheet.GetRegion(RegionName(42)).Should().BeSameAs(reAdded);
    }

    [Fact]
    public void AddRegion_rejects_a_duplicate_name_regardless_of_case()
    {
        //Arrange
        using var tilesheet = CreateSheet();
        tilesheet.AddRegion("ballBlue", new Rectangle(0, 0, TileSizePx, TileSizePx), new Size(TileSizePx, TileSizePx));

        //Act
        Action act = () => tilesheet.AddRegion(
            "BALLBLUE",
            new Rectangle(TileSizePx, 0, TileSizePx, TileSizePx),
            new Size(TileSizePx, TileSizePx));

        //Assert
        act.Should().Throw<ArgumentException>();
        tilesheet.Regions.Count.Should().Be(2);
    }

    [Fact]
    public void GetRegion_follows_the_public_Regions_list_when_it_is_changed_directly()
    {
        //Arrange - the Regions list is public, so the name index has to treat itself as a cache.
        using var tilesheet = CreateSheet();
        AddAtlasRegions(tilesheet, 3);
        tilesheet.GetRegion(RegionName(0)).Should().NotBeNull();

        //Act - remove one entry directly, then replace another with a region the index never saw.
        var directlyRemoved = tilesheet.GetRegion(RegionName(0))!;
        tilesheet.Regions.Remove(directlyRemoved);

        int replacedIndex = tilesheet.Regions.IndexOf(tilesheet.GetRegion(RegionName(1))!);
        var external = new TilesheetRegion(
            tilesheet,
            "externalRegion",
            RegionArea(1),
            new Size(TileSizePx, TileSizePx),
            Spacing.None,
            Spacing.None,
            Spacing.None,
            CollisionAdjust.None);
        tilesheet.Regions[replacedIndex] = external;

        //Assert
        tilesheet.GetRegion(RegionName(0)).Should().BeNull();
        tilesheet.GetRegion(RegionName(1)).Should().BeNull();
        tilesheet.GetRegion("externalRegion").Should().BeSameAs(external);
        tilesheet.GetRegion(RegionName(2)).Should().NotBeNull();
    }

    private static Tilesheet CreateSheet()
    {
        var bitmap = new SKBitmap(SheetTilesPerRow * TileSizePx, SheetTilesPerRow * TileSizePx);

        return TilesheetFactory.FromBitmap("RegionIndex", bitmap);
    }

    private static void AddAtlasRegions(Tilesheet tilesheet, int count)
    {
        for (int index = 0; index < count; index++)
            tilesheet.AddRegion(RegionName(index), RegionArea(index), new Size(TileSizePx, TileSizePx));
    }

    private static string RegionName(int index) => $"frame_{index:D3}";

    private static Rectangle RegionArea(int index)
    {
        int column = index % SheetTilesPerRow;
        int row = index / SheetTilesPerRow;

        return new Rectangle(column * TileSizePx, row * TileSizePx, TileSizePx, TileSizePx);
    }
}
