using CodeBrix.Platform.GameEngine.Drawing.Direct;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Verifies that <see cref="TextBlock"/> follows the engine's conventional zoom contract: values above
/// one zoom world-space content in, while view-space text stays expressed directly in screen pixels.
/// </summary>
public class TextBlockTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void ResolveTextScale_scene_layer_mode_uses_the_viewport_zoom(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.SceneLayer, viewportZoom);

        //Assert
        scale.Should().Be(viewportZoom);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void ResolveTextScale_view_mode_remains_screen_sized(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.View, viewportZoom);

        //Assert
        scale.Should().Be(1f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void ResolveTextScale_invalid_scene_layer_zoom_falls_back_to_one(float viewportZoom)
    {
        //Arrange + Act
        float scale = TextBlock.ResolveTextScale(DirectDrawingMode.SceneLayer, viewportZoom);

        //Assert
        scale.Should().Be(1f);
    }
}
