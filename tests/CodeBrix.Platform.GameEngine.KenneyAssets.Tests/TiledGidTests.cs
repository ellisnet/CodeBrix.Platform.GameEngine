using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates taking a Tiled cell value apart into a tile id and its flip flags, using the values that
/// appear in the Kenney map in the simulated bundle.
/// </summary>
public class TiledGidTests
{
    [Fact]
    public void GetTileId_of_a_plain_cell_is_the_value_itself()
    {
        //Act, Assert
        TiledGid.GetTileId(132u).Should().Be(132u);
        TiledGid.GetFlipFlags(132u).Should().Be(0u);
        TiledGid.IsEmpty(132u).Should().BeFalse();
    }

    [Fact]
    public void An_empty_cell_is_recognized()
    {
        //Act, Assert
        TiledGid.IsEmpty(0u).Should().BeTrue();
        TiledGid.GetTileId(0u).Should().Be(0u);
    }

    [Fact]
    public void A_horizontally_flipped_cell_keeps_its_tile_id()
    {
        //Arrange
        // 2147483775 is the first flipped value in the simulated bundle's map.
        const uint gid = 2147483775u;

        //Act, Assert
        TiledGid.GetTileId(gid).Should().Be(127u);
        TiledGid.IsFlippedHorizontally(gid).Should().BeTrue();
        TiledGid.IsFlippedVertically(gid).Should().BeFalse();
        TiledGid.IsFlippedDiagonally(gid).Should().BeFalse();
    }

    [Fact]
    public void A_cell_flipped_both_ways_reports_both_flags()
    {
        //Arrange
        const uint gid = 3221225625u;

        //Act, Assert
        TiledGid.GetTileId(gid).Should().Be(153u);
        TiledGid.IsFlippedHorizontally(gid).Should().BeTrue();
        TiledGid.IsFlippedVertically(gid).Should().BeTrue();
        TiledGid.IsFlippedDiagonally(gid).Should().BeFalse();
    }

    [Fact]
    public void A_diagonally_flipped_cell_reports_its_flag()
    {
        //Arrange
        const uint gid = TiledGid.FlipDiagonallyFlag | 45u;

        //Act, Assert
        TiledGid.GetTileId(gid).Should().Be(45u);
        TiledGid.IsFlippedDiagonally(gid).Should().BeTrue();
        TiledGid.GetFlipFlags(gid).Should().Be(TiledGid.FlipDiagonallyFlag);
    }

    [Fact]
    public void Every_flag_together_still_leaves_the_tile_id_readable()
    {
        //Arrange
        const uint gid = TiledGid.FlipHorizontallyFlag
            | TiledGid.FlipVerticallyFlag
            | TiledGid.FlipDiagonallyFlag
            | 1024u;

        //Act, Assert
        TiledGid.GetTileId(gid).Should().Be(1024u);
        TiledGid.IsFlippedHorizontally(gid).Should().BeTrue();
        TiledGid.IsFlippedVertically(gid).Should().BeTrue();
        TiledGid.IsFlippedDiagonally(gid).Should().BeTrue();
    }
}
