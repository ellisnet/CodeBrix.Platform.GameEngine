using System;
using CodeBrix.Audio.MusicGeneration;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>Covers the defaults <see cref="GeneratedMusicOptions"/> documents, and copying.</summary>
public class GeneratedMusicOptionsTests
{
    [Fact]
    public void A_new_instance_carries_the_documented_defaults()
    {
        //Arrange & Act
        var options = new GeneratedMusicOptions();

        //Assert
        options.Generator.Should().BeNull();
        options.InstrumentLibrary.Should().BeNull();
        options.Preset.Should().BeNull();
        options.Text.Should().BeNull();
        options.BeatsPerMinute.Should().BeNull();
        options.Seed.Should().BeNull();
        options.SeamCrossfade.Should().Be(TimeSpan.FromSeconds(4));
        options.SegmentPriming.Should().BeNull();
        options.MasterVolume.Should().Be(1.0F);
        options.StartImmediately.Should().BeTrue();
        options.TrackKey.Should().Be("generated-music");
        options.FadeIn.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void The_default_constants_match_the_defaults()
    {
        //Arrange & Act
        var options = new GeneratedMusicOptions();

        //Assert
        options.TrackKey.Should().Be(GeneratedMusicOptions.DefaultTrackKey);
        options.FadeIn.Should().Be(GeneratedMusicOptions.DefaultFadeIn);
        options.SeamCrossfade.Should().Be(GeneratedMusicOptions.DefaultSeamCrossfade);
    }

    [Fact]
    public void Clone_makes_an_independent_copy()
    {
        //Arrange
        var options = new GeneratedMusicOptions
        {
            Generator = "SkyTNT",
            InstrumentLibrary = "FluidR3Gm",
            Preset = "ClubArrangement",
            Seed = 7,
            BeatsPerMinute = 126,
            SegmentPriming = SegmentPriming.Fresh,
            MasterVolume = 0.5F,
            StartImmediately = false,
        };

        //Act
        GeneratedMusicOptions copy = options.Clone();
        options.Preset = "FourOnTheFloor";

        //Assert
        copy.Should().NotBeSameAs(options);
        copy.Generator.Should().Be("SkyTNT");
        copy.InstrumentLibrary.Should().Be("FluidR3Gm");
        copy.Preset.Should().Be("ClubArrangement");
        copy.Seed.Should().Be(7);
        copy.BeatsPerMinute.Should().Be(126);
        copy.SegmentPriming.Should().Be(SegmentPriming.Fresh);
        copy.MasterVolume.Should().Be(0.5F);
        copy.StartImmediately.Should().BeFalse();
    }
}
