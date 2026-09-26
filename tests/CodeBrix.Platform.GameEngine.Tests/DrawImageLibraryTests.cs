using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="DrawImageLibrary"/>: finding pictures by tilesheet name or asset key and frame name, remembering
/// them, and reporting what is missing once instead of throwing mid-frame.
/// </summary>
public class DrawImageLibraryTests : IDisposable
{
    private const string SheetName = "draw-image-library-sheet";

    private readonly Tilesheet _sheet;

    /// <summary>Registers a 16 x 8 sheet with two 8 x 8 frames, "left" and "right".</summary>
    public DrawImageLibraryTests()
    {
        _sheet = TilesheetRegistry.Instance.LoadFromBitmap(SheetName, new SKBitmap(16, 8));
        _sheet.DefaultRegion.TileSize = new Size(16, 8);
        _sheet.AddRegion("left", new Rectangle(0, 0, 8, 8), new Size(8, 8));
        _sheet.AddRegion("right", new Rectangle(8, 0, 8, 8), new Size(8, 8));
    }

    /// <summary>Removes and disposes the fixture's sheet.</summary>
    public void Dispose()
    {
        TilesheetRegistry.Instance.Remove(SheetName, dispose: true);
        _sheet.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Get_finds_a_frame_of_a_registered_tilesheet()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var image = library.Get(SheetName, "right");

        //Assert
        image.Should().BeSameAs(_sheet.GetImage("right", 0, 0));
        image!.Width.Should().Be(8);
        library.Count.Should().Be(1);
        library.Missing.Should().BeEmpty();
    }

    [Fact]
    public void Get_without_a_frame_name_takes_the_default_region_the_whole_picture()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var image = library.Get(SheetName);

        //Assert
        image!.Width.Should().Be(16);
        image.Height.Should().Be(8);
    }

    [Fact]
    public void Get_remembers_a_picture_so_it_is_not_looked_up_again()
    {
        //Arrange
        var library = new DrawImageLibrary();
        var first = library.Get(SheetName, "left");

        //Act - the registry no longer has the sheet, but the library already holds the picture
        TilesheetRegistry.Instance.Remove(SheetName, dispose: false);
        var second = library.Get(SheetName, "left");

        //Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void an_unknown_frame_draws_nothing_and_is_reported_once_with_the_closest_names()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var first = library.Get(SheetName, "rihgt");
        var second = library.Get(SheetName, "rihgt");

        //Assert
        first.Should().BeNull();
        second.Should().BeNull();
        library.Missing.Should().ContainSingle();
        library.Missing[0].Should().Be($"{SheetName} / rihgt");
        library.Warnings.Should().ContainSingle();
        library.Warnings[0].Should().Contain("Did you mean: 'right'");
    }

    [Fact]
    public void a_frame_without_a_picture_is_reported_as_such()
    {
        //Arrange - a region with no tile size cuts no pictures
        using var sheet = TilesheetFactory.FromBitmap("draw-image-library-no-tiles", new SKBitmap(4, 4));
        var library = new DrawImageLibrary();
        library.AddTilesheet("bare", sheet);

        //Act
        var image = library.Get("bare");

        //Assert
        image.Should().BeNull();
        library.Warnings.Should().ContainSingle();
        library.Warnings[0].Should().Contain("has no picture");
    }

    [Fact]
    public void an_unknown_asset_key_draws_nothing_and_is_reported_once()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var first = library.Get("draw-image-library-no-such-key", "a");
        var second = library.Get("draw-image-library-no-such-key", "b");

        //Assert
        first.Should().BeNull();
        second.Should().BeNull();
        library.Missing.Should().ContainSingle();
        library.Missing[0].Should().Be("draw-image-library-no-such-key");
    }

    [Fact]
    public void Get_rejects_a_blank_asset_key()
    {
        //Arrange
        var library = new DrawImageLibrary();
        Action act = () => library.Get(" ");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Preload_counts_the_frames_it_found_and_lists_the_rest_as_missing()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var found = library.Preload(SheetName, new[] { "left", "right", "middle" });

        //Assert
        found.Should().Be(2);
        library.Missing.Should().ContainSingle();
    }

    [Fact]
    public void Preload_with_no_frame_names_resolves_the_whole_picture()
    {
        //Arrange
        var library = new DrawImageLibrary();

        //Act
        var found = library.Preload(SheetName, Array.Empty<string>());

        //Assert
        found.Should().Be(1);
    }

    [Fact]
    public void AddTilesheet_registers_a_sheet_under_a_key_of_the_game_and_forgets_older_pictures_of_that_key()
    {
        //Arrange
        var library = new DrawImageLibrary();
        using var first = TilesheetFactory.FromBitmap("draw-image-library-own-1", new SKBitmap(4, 4));
        using var second = TilesheetFactory.FromBitmap("draw-image-library-own-2", new SKBitmap(6, 6));
        first.DefaultRegion.TileSize = new Size(4, 4);
        second.DefaultRegion.TileSize = new Size(6, 6);
        library.AddTilesheet("promo", first);
        var before = library.Get("promo");

        //Act
        library.AddTilesheet("promo", second);
        var after = library.Get("promo");

        //Assert
        before!.Width.Should().Be(4);
        after!.Width.Should().Be(6);
    }

    [Fact]
    public void AddImage_registers_one_picture_under_a_key_and_frame()
    {
        //Arrange
        var library = new DrawImageLibrary();
        using var bitmap = new SKBitmap(3, 3);
        using var image = SKImage.FromBitmap(bitmap);

        //Act
        library.AddImage("icons", "star", image);

        //Assert
        library.Get("icons", "star").Should().BeSameAs(image);
        library.Get("icons").Should().BeNull();
    }
}
