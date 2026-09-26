using System;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>Covers the <see cref="DrawCommand"/> factories: what each kind stores and how inputs are clamped.</summary>
public class DrawCommandTests
{
    [Fact]
    public void ForImage_stores_the_picture_box_rotation_alpha_and_fit()
    {
        //Arrange
        using var bitmap = new SKBitmap(4, 2);
        using var image = SKImage.FromBitmap(bitmap);

        //Act
        var command = DrawCommand.ForImage(image, 10, 20, 30, 40, rotation: 90, alpha: 0.5, fit: DrawImageFit.Stretch);

        //Assert
        command.Kind.Should().Be(DrawCommandKind.Image);
        command.Image.Should().BeSameAs(image);
        command.X.Should().Be(10f);
        command.Y.Should().Be(20f);
        command.Width.Should().Be(30f);
        command.Height.Should().Be(40f);
        command.Rotation.Should().Be(90f);
        command.Alpha.Should().Be(0.5f);
        command.Fit.Should().Be(DrawImageFit.Stretch);
    }

    [Fact]
    public void ForImage_rejects_a_null_picture()
    {
        //Arrange
        Action act = () => DrawCommand.ForImage(null!, 0, 0, 1, 1);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-1.0, 0f)]
    [InlineData(0.25, 0.25f)]
    [InlineData(7.0, 1f)]
    [InlineData(double.NaN, 0f)]
    public void alpha_is_clamped_to_zero_to_one(double alpha, float expected) =>
        DrawCommand.ForRectangle(0, 0, 1, 1, SKColors.Red, alpha: alpha).Alpha.Should().Be(expected);

    [Fact]
    public void ForRectangle_keeps_fill_stroke_and_corner_radius_and_floors_negatives_at_zero()
    {
        //Arrange + Act
        var command = DrawCommand.ForRectangle(5, 6, 7, 8, SKColors.Blue, SKColors.White, strokeWidth: -2, cornerRadius: -3);

        //Assert
        command.Kind.Should().Be(DrawCommandKind.Rectangle);
        command.Color.Should().Be(SKColors.Blue);
        command.StrokeColor.Should().Be(SKColors.White);
        command.StrokeWidth.Should().Be(0f);
        command.CornerRadius.Should().Be(0f);
    }

    [Fact]
    public void ForRectangle_accepts_packed_argb_colors_and_defaults_to_no_outline()
    {
        //Arrange + Act
        var command = DrawCommand.ForRectangle(0, 0, 1, 1, 0x80FF0000);

        //Assert
        command.Color.Should().Be(new SKColor(255, 0, 0, 128));
        command.StrokeColor.Alpha.Should().Be((byte)0);
    }

    [Fact]
    public void ForCircle_stores_the_diameter_as_width_and_height()
    {
        //Arrange + Act
        var command = DrawCommand.ForCircle(1, 2, 3, SKColors.Green);

        //Assert
        command.Kind.Should().Be(DrawCommandKind.Circle);
        command.Width.Should().Be(6f);
        command.Height.Should().Be(6f);
    }

    [Fact]
    public void ForText_stores_the_typeface_size_and_alignment_and_turns_null_text_into_empty()
    {
        //Arrange
        var typeface = SKTypeface.Default;

        //Act
        var command = DrawCommand.ForText(null, 1, 2, typeface, 18, SKColors.White, SKTextAlign.Right);

        //Assert
        command.Kind.Should().Be(DrawCommandKind.Text);
        command.Text.Should().Be(string.Empty);
        command.Typeface.Should().BeSameAs(typeface);
        command.FontSize.Should().Be(18f);
        command.TextAlign.Should().Be(SKTextAlign.Right);
    }

    [Fact]
    public void ForText_rejects_a_null_typeface()
    {
        //Arrange
        Action act = () => DrawCommand.ForText("x", 0, 0, null!, 12, SKColors.White);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
