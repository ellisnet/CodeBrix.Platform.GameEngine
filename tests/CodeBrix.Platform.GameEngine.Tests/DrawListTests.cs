using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="DrawList"/>: building, publishing a finished copy (latest wins), hit regions, picture and
/// typeface resolution, and that a reader on another thread only ever sees whole frames.
/// </summary>
public class DrawListTests : IDisposable
{
    private readonly List<string> _fontKeys = new();

    /// <summary>Removes the font keys this fixture registered; the font manager is shared.</summary>
    public void Dispose()
    {
        foreach (var key in _fontKeys)
            FontManager.Instance.Remove(key);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Published_is_the_empty_snapshot_before_the_first_publish()
    {
        //Arrange + Act
        var list = new DrawList();

        //Assert
        list.Published.Should().BeSameAs(DrawListSnapshot.Empty);
        list.Count.Should().Be(0);
    }

    [Fact]
    public void Publish_copies_the_commands_so_building_the_next_frame_leaves_the_published_one_alone()
    {
        //Arrange
        var list = new DrawList();
        list.Rectangle(10, 10, 4, 4, SKColors.Red);
        list.Circle(20, 20, 2, SKColors.Blue);

        //Act
        var first = list.Publish();
        list.Clear();
        list.Text("next", 0, 0, SKTypeface.Default, 12, SKColors.White);

        //Assert
        list.Published.Should().BeSameAs(first);
        first.Commands.Should().HaveCount(2);
        first.Commands[0].Kind.Should().Be(DrawCommandKind.Rectangle);
        first.Commands[1].Kind.Should().Be(DrawCommandKind.Circle);
        list.Count.Should().Be(1);
    }

    [Fact]
    public void Publish_numbers_snapshots_and_the_latest_wins()
    {
        //Arrange
        var list = new DrawList();

        //Act
        var first = list.Publish();
        var second = list.Publish();

        //Assert
        first.Number.Should().Be(1L);
        second.Number.Should().Be(2L);
        list.Published.Should().BeSameAs(second);
    }

    [Fact]
    public void Publish_keeps_the_builder_contents_until_Clear()
    {
        //Arrange
        var list = new DrawList();
        list.Rectangle(0, 0, 1, 1, SKColors.Red);

        //Act
        list.Publish();
        var again = list.Publish();

        //Assert
        again.Commands.Should().HaveCount(1);
    }

    [Fact]
    public void Clear_removes_commands_and_hit_regions_but_not_the_published_snapshot()
    {
        //Arrange
        var list = new DrawList();
        list.Rectangle(0, 0, 1, 1, SKColors.Red);
        list.HitRegion(0, 0, 10, 10, "button");
        var published = list.Publish();

        //Act
        list.Clear();

        //Assert
        list.Commands.Should().BeEmpty();
        list.HitRegions.Should().BeEmpty();
        list.Published.Should().BeSameAs(published);
        published.HitTest(0, 0)!.Value.Id.Should().Be("button");
    }

    [Fact]
    public void HitRegion_rejects_a_null_id()
    {
        //Arrange
        var list = new DrawList();
        Action act = () => list.HitRegion(0, 0, 1, 1, null!);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Image_with_a_null_picture_adds_nothing()
    {
        //Arrange
        var list = new DrawList();

        //Act
        var added = list.Image((SKImage?)null, 0, 0, 10, 10);

        //Assert
        added.Should().BeFalse();
        list.Count.Should().Be(0);
    }

    [Fact]
    public void Image_with_a_frame_resolves_the_frame_picture_when_the_command_is_added()
    {
        //Arrange
        using var bitmap = new SKBitmap(8, 8);
        using var sheet = TilesheetFactory.FromBitmap("draw-list-frame", bitmap);
        sheet.DefaultRegion.TileSize = new System.Drawing.Size(8, 8);
        var list = new DrawList();

        //Act
        var added = list.Image(new Frame(sheet, 0, 0), 4, 4, 8, 8);

        //Assert
        added.Should().BeTrue();
        list.Commands[0].Image.Should().BeSameAs(sheet.GetImage(TilesheetRegion.DefaultRegionName, 0, 0));
    }

    [Fact]
    public void Image_with_an_asset_key_uses_the_list_library()
    {
        //Arrange
        using var bitmap = new SKBitmap(8, 8);
        using var image = SKImage.FromBitmap(bitmap);
        var library = new DrawImageLibrary();
        library.AddImage("ui", "cursor", image);
        var list = new DrawList(library);

        //Act
        var found = list.Image("ui", "cursor", 1, 1, 8, 8);
        var missing = list.Image("ui", "nope", 1, 1, 8, 8);

        //Assert
        list.Images.Should().BeSameAs(library);
        found.Should().BeTrue();
        missing.Should().BeFalse();
        list.Commands.Should().HaveCount(1);
        list.Commands[0].Image.Should().BeSameAs(image);
    }

    [Fact]
    public void Text_with_a_typeface_key_resolves_it_through_the_font_manager()
    {
        //Arrange
        var typeface = RegisterFont("draw-list-text");
        var list = new DrawList();

        //Act
        list.Text("SCORE", 10, 10, "draw-list-text", 20, SKColors.White, SKTextAlign.Left);

        //Assert
        list.Commands[0].Typeface.Should().BeSameAs(typeface);
        list.Commands[0].TextAlign.Should().Be(SKTextAlign.Left);
    }

    [Fact]
    public void Text_with_an_unknown_typeface_key_throws_the_font_manager_error()
    {
        //Arrange
        var list = new DrawList();
        Action act = () => list.Text("x", 0, 0, "draw-list-no-such-font", 12, SKColors.White);

        //Act + Assert
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Rectangle_from_an_SKRect_is_stored_by_its_centre()
    {
        //Arrange
        var list = new DrawList();

        //Act
        list.Rectangle(new SKRect(10, 20, 30, 60), SKColors.Red);

        //Assert
        list.Commands[0].X.Should().Be(20f);
        list.Commands[0].Y.Should().Be(40f);
        list.Commands[0].Width.Should().Be(20f);
        list.Commands[0].Height.Should().Be(40f);
    }

    [Fact]
    public void the_constructor_rejects_a_negative_capacity()
    {
        //Arrange
        Action act = () => _ = new DrawList(capacity: -1);

        //Act + Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task a_reader_on_another_thread_only_ever_sees_whole_frames()
    {
        //Arrange - every command of frame n sits at x = n, so a torn frame would mix x values
        var list = new DrawList();
        var cancellation = TestContext.Current.CancellationToken;
        var torn = 0;
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var snapshot = list.Published;
                var commands = snapshot.Commands;
                for (var i = 0; i < commands.Count; i++)
                {
                    if (commands[i].X != commands[0].X || commands.Count != 50)
                        Interlocked.Increment(ref torn);
                }
            }
        }, cancellation);

        //Act
        for (var frame = 1; frame <= 2000; frame++)
        {
            list.Clear();
            for (var i = 0; i < 50; i++)
                list.Rectangle(frame, i, 1, 1, SKColors.Red);
            list.Publish();
        }

        stop.Cancel();
        await reader;

        //Assert
        torn.Should().Be(0);
        list.Published.Number.Should().Be(2000L);
    }

    private SKTypeface RegisterFont(string key)
    {
        var bytes = TryGetFontBytes();
        if (bytes is null)
            Assert.Skip("No font data is available on this machine.");

        _fontKeys.Add(key);
        return FontManager.Instance.LoadFromBytes(key, bytes!);
    }

    private static byte[]? TryGetFontBytes()
    {
        try
        {
            using var fontStream = SKTypeface.Default.OpenStream(out _);
            if (fontStream is null || fontStream.Length <= 0)
                return null;

            using var data = SKData.Create(fontStream);
            var bytes = data?.ToArray();
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
