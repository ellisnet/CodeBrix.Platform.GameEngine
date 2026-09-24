using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates how a bundle's file name, folder name and licence header become a display name, a version
/// and the slug that namespaces its keys.
/// </summary>
public class KenneyNamesTests
{
    [Theory]
    [InlineData("kenney_brick-kit.zip", "Brick Kit")]
    [InlineData("kenney_puzzle-pack-1.zip", "Puzzle Pack 1")]
    [InlineData("/games/assets/kenney_sci-fi-sounds.zip", "Sci Fi Sounds")]
    [InlineData("KENNEY_blocky-characters_20.ZIP", "Blocky Characters 20")]
    [InlineData("Pixel Platformer", "Pixel Platformer")]
    [InlineData("/games/assets/Pixel Platformer/", "Pixel Platformer")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void PrettifyBundleFileName_titles_a_bundle_name(string? input, string expected)
        => KenneyNames.PrettifyBundleFileName(input).Should().Be(expected);

    [Fact]
    public void TryParseLicenseTitle_reads_the_title_and_version_of_a_real_licence_file()
    {
        //Arrange
        // The shape every Kenney pack ships: a tab-only first line, then the title.
        string licence = TestFixtures.LicenseText("Pixel Platformer", "1.2");

        //Act
        bool parsed = KenneyNames.TryParseLicenseTitle(licence, out string? title, out string? version);

        //Assert
        parsed.Should().BeTrue();
        title.Should().Be("Pixel Platformer");
        version.Should().Be("1.2");
    }

    [Fact]
    public void TryParseLicenseTitle_skips_a_decorative_rule_before_the_title()
    {
        //Arrange
        // Some packs open their licence with a row of '#' characters; that row is not the title.
        string licence = "\r\n" + new string('#', 79) + "\r\n\r\n\tSpace Shooter (Redux)\r\n\r\n\tCreated by Kenney";

        //Act
        bool parsed = KenneyNames.TryParseLicenseTitle(licence, out string? title, out string? version);

        //Assert
        parsed.Should().BeTrue();
        title.Should().Be("Space Shooter");
        version.Should().Be("Redux");
    }

    [Fact]
    public void TryParseLicenseTitle_falls_through_to_a_too_long_title_after_a_decorative_rule()
    {
        //Arrange
        // A rule, then a title line over the length limit: the rule must not be taken as the title
        // and the long line must still be rejected, so the caller falls back to the file name.
        string licence = new string('#', 79) + "\r\n\r\n\t" + new string('x', 84) + "\r\n";

        //Act
        bool parsed = KenneyNames.TryParseLicenseTitle(licence, out string? title, out _);

        //Assert
        parsed.Should().BeFalse();
        title.Should().BeNull();
    }

    [Fact]
    public void TryParseLicenseTitle_accepts_a_title_without_a_version()
    {
        //Act
        bool parsed = KenneyNames.TryParseLicenseTitle(
            "\tMonochrome Pirates\r\n\r\n\tCreated by Kenney", out string? title, out string? version);

        //Assert
        parsed.Should().BeTrue();
        title.Should().Be("Monochrome Pirates");
        version.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("http://creativecommons.org/publicdomain/zero/1.0/")]
    [InlineData("This content is free to use in personal, educational and commercial projects, " +
        "and this line is far too long to be anything like a pack title.")]
    public void TryParseLicenseTitle_rejects_a_line_that_is_licence_body_text(string? licence)
    {
        //Act
        bool parsed = KenneyNames.TryParseLicenseTitle(licence, out string? title, out string? version);

        //Assert
        parsed.Should().BeFalse();
        title.Should().BeNull();
        version.Should().BeNull();
    }

    [Theory]
    [InlineData("Puzzle Pack", "puzzle-pack")]
    [InlineData("Puzzle Pack 1", "puzzle-pack-1")]
    [InlineData("Sci-Fi Sounds", "sci-fi-sounds")]
    [InlineData("Simulated Bundle", "simulated-bundle")]
    [InlineData("UI Pack - Sci-fi", "ui-pack-sci-fi")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("Pico-8 City", "pico-8-city")]
    [InlineData("!!!", "pack")]
    [InlineData("", "pack")]
    [InlineData(null, "pack")]
    public void Slugify_makes_a_key_safe_pack_name(string? input, string expected)
        => KenneyNames.Slugify(input).Should().Be(expected);

    [Fact]
    public void Slugify_keeps_letters_outside_the_ascii_range()
        => KenneyNames.Slugify("Öl Kit").Should().Be("öl-kit");
}
