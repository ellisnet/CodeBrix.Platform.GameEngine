using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Covers the key check result: its summary line and its lookup by key.
/// </summary>
public class KenneyKeyCheckTests
{
    private static readonly KenneyKeyCheck Check = new(
    [
        new KenneyKeyStatus("kenney:a/Audio/laser", true, GameAssetKind.Audio, 1024 * 1024, 0),
        new KenneyKeyStatus("kenney:a/Sheet/atlas", true, GameAssetKind.SpriteAtlas, 1024 * 1024 / 2, 12),
        new KenneyKeyStatus("kenney:a/Audio/lazer", false, GameAssetKind.Unknown, 0, 0),
    ]);

    [Fact]
    public void ToString_summarizes_kinds_missing_keys_and_size() =>
        Check.ToString().Should().Be("3 key(s) checked - Audio 1, SpriteAtlas 1 - 1 missing, 1.5 MB");

    [Fact]
    public void ToString_of_nothing_found_says_so() =>
        new KenneyKeyCheck([new KenneyKeyStatus("x", false, GameAssetKind.Unknown, 0, 0)]).ToString()
            .Should().Be("1 key(s) checked - none found - 1 missing, 0.0 MB");

    [Fact]
    public void Indexer_finds_a_key_case_insensitively() =>
        Check["KENNEY:A/SHEET/ATLAS"].AtlasFrameCount.Should().Be(12);

    [Fact]
    public void Indexer_throws_for_a_key_that_was_not_checked()
    {
        //Act
        Action act = () => _ = Check["kenney:a/Other"];

        //Assert
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void MissingKeys_and_AllFound_follow_the_statuses()
    {
        //Assert
        Check.MissingKeys.Should().BeEquivalentTo(["kenney:a/Audio/lazer"]);
        Check.AllFound.Should().BeFalse();
        Check.TotalSizeBytes.Should().Be(1024 * 1024 * 3 / 2);
    }
}
