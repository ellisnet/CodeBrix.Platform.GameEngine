using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the settings a game registers Kenney bundles with: the defaults it inherits by passing
/// nothing but paths, and the record semantics that let one set of house settings be varied.
/// </summary>
public class KenneyAssetsOptionsTests
{
    [Fact]
    public void Defaults_register_with_the_kenney_provider_and_forgive_an_unreadable_source()
    {
        //Arrange & Act
        KenneyAssetsOptions options = new();

        //Assert
        options.Sources.Should().BeEmpty();
        options.ProviderId.Should().Be("kenney");
        options.ProviderId.Should().Be(KenneyGameAssetProvider.DefaultProviderId);
        options.RecursiveFolders.Should().BeFalse();
        options.IgnoreUnreadableSources.Should().BeTrue();
    }

    [Fact]
    public void An_altered_copy_keeps_the_untouched_members()
    {
        //Arrange
        KenneyAssetsOptions house = new() { ProviderId = "mods", IgnoreUnreadableSources = false };

        //Act
        KenneyAssetsOptions altered = house with { Sources = ["one.zip", "two.zip"] };

        //Assert
        altered.ProviderId.Should().Be("mods");
        altered.IgnoreUnreadableSources.Should().BeFalse();
        altered.Sources.Count.Should().Be(2);
        house.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Two_option_sets_with_the_same_values_are_equal()
    {
        //Arrange & Act
        KenneyAssetsOptions first = new() { ProviderId = "mods", RecursiveFolders = true };
        KenneyAssetsOptions second = new() { ProviderId = "mods", RecursiveFolders = true };

        //Assert
        first.Should().Be(second);
    }
}
