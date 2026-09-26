using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the per-source registration result: which statuses count as available and its log line.
/// </summary>
public class KenneySourceResultTests
{
    [Fact]
    public void IsAvailable_is_true_for_a_source_read_now_or_earlier()
    {
        //Assert
        Result(KenneySourceStatus.Read).IsAvailable.Should().BeTrue();
        Result(KenneySourceStatus.AlreadyRegistered).IsAvailable.Should().BeTrue();
        Result(KenneySourceStatus.Missing).IsAvailable.Should().BeFalse();
        Result(KenneySourceStatus.Unreadable).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void ToString_names_the_file_status_and_packs() =>
        (Result(KenneySourceStatus.Read) with
        {
            Packs = [new KenneyPackSummary { Slug = "planets", DisplayName = "Planets", SourcePath = "p" }],
        }).ToString().Should().Be("kenney_planets.zip: Read - planets (Planets)");

    [Fact]
    public void ToString_carries_the_message_of_a_source_that_contributed_nothing() =>
        (Result(KenneySourceStatus.Missing) with { Message = "not there" }).ToString()
            .Should().Be("kenney_planets.zip: Missing - not there");

    private static KenneySourceResult Result(KenneySourceStatus status) => new()
    {
        SourcePath = "/games/assets/kenney_planets.zip",
        Status = status,
    };
}
