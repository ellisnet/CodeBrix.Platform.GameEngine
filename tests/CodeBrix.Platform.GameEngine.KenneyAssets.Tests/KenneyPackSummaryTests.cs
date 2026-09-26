using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the credit line a pack summary builds from its licence title or display name.
/// </summary>
public class KenneyPackSummaryTests
{
    [Fact]
    public void CreditLine_credits_the_licence_title() =>
        Summary("Space Shooter Remastered (1.0)").CreditLine
            .Should().Be("Space Shooter Remastered (1.0) - Kenney (CC0)");

    [Fact]
    public void CreditLine_falls_back_to_the_display_name_without_a_licence_title() =>
        Summary(null).CreditLine.Should().Be("Planets - Kenney (CC0)");

    [Fact]
    public void CreditLine_trims_the_licence_title() =>
        Summary("\tPlanets (1.0)  ").CreditLine.Should().Be("Planets (1.0) - Kenney (CC0)");

    private static KenneyPackSummary Summary(string? licenseTitle) => new()
    {
        Slug = "planets",
        DisplayName = "Planets",
        LicenseTitle = licenseTitle,
        SourcePath = "kenney_planets.zip",
    };
}
