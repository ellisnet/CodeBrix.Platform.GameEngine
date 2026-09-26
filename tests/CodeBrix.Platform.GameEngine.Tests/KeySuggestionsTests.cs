using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the "Did you mean" suggestions a not-found asset key or region name message carries: which
/// candidates count as close, their order, the bound on how many, and that a failing candidate list
/// never replaces the error.
/// </summary>
public class KeySuggestionsTests
{
    private static readonly string[] Keys =
    [
        "kenney:space/PNG/Enemies/enemyBlack1",
        "kenney:space/PNG/Enemies/enemyBlack2",
        "kenney:space/PNG/Enemies/enemyBlue1",
        "kenney:space/PNG/Lasers/laserRed01",
        "kenney:space/Audio/laser1",
        "kenney:planets/Planets/planet00",
    ];

    [Fact]
    public void Closest_finds_a_misspelled_name()
    {
        //Act
        IReadOnlyList<string> closest = KeySuggestions.Closest("kenney:space/PNG/Enemies/enemyBlak1", Keys);

        //Assert
        closest[0].Should().Be("kenney:space/PNG/Enemies/enemyBlack1");
    }

    [Fact]
    public void Closest_finds_the_right_name_in_the_wrong_folder()
    {
        //Act
        IReadOnlyList<string> closest = KeySuggestions.Closest("kenney:space/Sprites/laserRed01", Keys);

        //Assert
        closest[0].Should().Be("kenney:space/PNG/Lasers/laserRed01");
    }

    [Fact]
    public void Closest_ignores_case()
    {
        //Act
        IReadOnlyList<string> closest = KeySuggestions.Closest("KENNEY:PLANETS/PLANETS/PLANET0", Keys);

        //Assert
        closest[0].Should().Be("kenney:planets/Planets/planet00");
    }

    [Fact]
    public void Closest_returns_at_most_the_bound_best_first()
    {
        //Act
        IReadOnlyList<string> closest = KeySuggestions.Closest("kenney:space/PNG/Enemies/enemyBlack3", Keys);

        //Assert
        closest.Count.Should().Be(KeySuggestions.MaxSuggestions);
        closest.Take(2).Should().BeEquivalentTo(
            ["kenney:space/PNG/Enemies/enemyBlack1", "kenney:space/PNG/Enemies/enemyBlack2"]);
    }

    [Fact]
    public void Closest_suggests_nothing_that_is_not_close() =>
        KeySuggestions.Closest("kenney:music/Tracks/theme", Keys).Should().BeEmpty();

    [Fact]
    public void Closest_suggests_nothing_for_a_blank_key() =>
        KeySuggestions.Closest(" ", Keys).Should().BeEmpty();

    [Fact]
    public void DidYouMean_builds_the_sentence_to_append() =>
        KeySuggestions.DidYouMean("ballBlu", () => ["ballBlue", "ballGrey", "paddleRed"])
            .Should().Be(" Did you mean: 'ballBlue'?");

    [Fact]
    public void DidYouMean_is_empty_when_nothing_is_close() =>
        KeySuggestions.DidYouMean("ballBlue", () => ["paddle_long_red"]).Should().BeEmpty();

    [Fact]
    public void DidYouMean_is_empty_when_the_candidates_cannot_be_listed() =>
        KeySuggestions.DidYouMean("ballBlue", () => throw new InvalidOperationException("broken"))
            .Should().BeEmpty();
}
