using System;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.MuPT;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Audio.MusicGeneration.SkyTNT;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Covers how <see cref="GeneratedMusicOptions"/> map onto the music package's session options and
/// requests: which generator plays, the preset and word rules, the tempo, seed, seam and priming
/// settings - and that the session always leaves the audio output to the engine. Registering the
/// model packages' generators here loads nothing.
/// </summary>
public class GeneratedMusicRequestsTests : GeneratedMusicTestContext
{
    [Fact]
    public void ToSessionOptions_makes_the_application_own_the_output_at_the_engine_rate()
    {
        //Arrange & Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions(), 44100, out _, out _);

        //Assert
        session.ApplicationOwnsAudioOutput.Should().BeTrue("the engine owns the one audio output; the session must open none");
        session.SampleRate.Should().Be(44100);
    }

    [Fact]
    public void ToSessionOptions_with_no_model_registered_falls_back_to_the_embedded_replay_with_a_bare_request()
    {
        //Arrange
        var options = new GeneratedMusicOptions { Preset = "ClubArrangement", Seed = 5, BeatsPerMinute = 120 };

        //Act
        MusicGenerationOptions session = Map(options, SampleRate, out IMusicGenerator generator, out bool fellBack);

        //Assert
        fellBack.Should().BeTrue();
        generator.Name.Should().Be(EmbeddedReplay.Midi);
        session.Generator.Should().Be(EmbeddedReplay.Midi);
        session.Request.Seed.Should().BeNull("a replay refuses a seed, so the degraded path leaves it out");
        session.Request.Intent.Should().BeNull();
        session.SessionBeatsPerMinute.Should().Be(120, "the session tempo is never sent to the generator, so it still applies");
    }

    [Fact]
    public void ToSessionOptions_takes_the_first_registered_generator_that_is_not_a_built_in_replay()
    {
        //Arrange
        var first = new RecordingMusicGenerator("FirstTestGenerator", "TestFamily");
        MusicGeneratorRegistry.Register(first);
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);

        //Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions(), SampleRate, out IMusicGenerator generator, out bool fellBack);

        //Assert
        fellBack.Should().BeFalse();
        generator.Should().BeSameAs(first);
        session.Generator.Should().Be("FirstTestGenerator");
    }

    [Fact]
    public void ToSessionOptions_uses_the_generator_the_options_name()
    {
        //Arrange
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);
        MusicGeneratorRegistry.Register(MuPTModel.Instance);

        //Act
        MusicGenerationOptions session = Map(
            new GeneratedMusicOptions { Generator = MuPTModel.GeneratorName }, SampleRate, out IMusicGenerator generator, out _);

        //Assert
        generator.Should().BeSameAs(MuPTModel.Instance);
        session.Generator.Should().Be(MuPTModel.GeneratorName);
    }

    [Fact]
    public void ToSessionOptions_with_an_unregistered_generator_throws_listing_what_is_registered()
    {
        //Arrange
        Action act = () => Map(new GeneratedMusicOptions { Generator = "NoSuchGenerator" }, SampleRate, out _, out _);

        //Act & Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*NoSuchGenerator*");
    }

    [Fact]
    public void ToSessionOptions_turns_a_preset_into_its_request_and_its_suggested_voicing()
    {
        //Arrange
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);
        MusicPreset preset = SkyTNTPresets.ClubArrangement;

        //Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions { Preset = "clubarrangement" }, SampleRate, out _, out _);

        //Assert
        MusicRequest expected = preset.CreateRequest();
        session.Request.Intent.Should().NotBeNull();
        session.Request.Intent!.DrumKit.Should().Be(expected.Intent!.DrumKit);
        session.Request.Intent.BeatsPerMinute.Should().Be(expected.Intent.BeatsPerMinute);
        session.Rendition.Should().Be(preset.SuggestedRendition);
    }

    [Fact]
    public void ToSessionOptions_refuses_a_preset_written_for_another_family_and_lists_the_ones_that_fit()
    {
        //Arrange
        MusicGeneratorRegistry.Register(MuPTModel.Instance);
        Action act = () => Map(new GeneratedMusicOptions { Preset = "ClubArrangement" }, SampleRate, out _, out _);

        //Act & Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ClubArrangement*SkyTNT*MuPT*WaltzDuetInAMinor*");
    }

    [Fact]
    public void ToSessionOptions_holds_a_given_tempo_as_the_session_tempo()
    {
        //Arrange & Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions { BeatsPerMinute = 132 }, SampleRate, out _, out _);

        //Assert
        session.SessionBeatsPerMinute.Should().Be(132);
        session.TempoPolicy.Should().Be(SessionTempoPolicy.Carry);
    }

    [Fact]
    public void ToSessionOptions_without_a_tempo_lets_every_piece_keep_its_own()
    {
        //Arrange & Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions(), SampleRate, out _, out _);

        //Assert
        session.SessionBeatsPerMinute.Should().BeNull();
        session.TempoPolicy.Should().Be(SessionTempoPolicy.Adopt);
    }

    [Fact]
    public void ToSessionOptions_passes_the_seed_the_volume_the_library_and_the_seam_crossfade_on()
    {
        //Arrange
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);
        var options = new GeneratedMusicOptions
        {
            Seed = 42,
            MasterVolume = 0.6F,
            InstrumentLibrary = " FluidR3Gm ",
            SeamCrossfade = TimeSpan.FromSeconds(2),
        };

        //Act
        MusicGenerationOptions session = Map(options, SampleRate, out _, out _);

        //Assert
        session.Request.Seed.Should().Be(42);
        session.MasterVolume.Should().Be(0.6F);
        session.InstrumentLibrary.Should().Be("FluidR3Gm");
        session.SeamCrossfade.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ToSessionOptions_treats_a_negative_seam_crossfade_as_a_hard_join()
    {
        //Arrange & Act
        MusicGenerationOptions session = Map(
            new GeneratedMusicOptions { SeamCrossfade = TimeSpan.FromSeconds(-1) }, SampleRate, out _, out _);

        //Assert
        session.SeamCrossfade.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ToSessionOptions_leaves_the_instrument_library_to_the_default_when_none_is_named()
    {
        //Arrange & Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions { InstrumentLibrary = "  " }, SampleRate, out _, out _);

        //Assert
        session.InstrumentLibrary.Should().BeNull();
    }

    [Fact]
    public void ToSessionOptions_takes_turns_between_primed_and_fresh_segments_for_SkyTNT()
    {
        //Arrange
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);

        //Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions(), SampleRate, out _, out _);

        //Assert
        session.SegmentPriming.Should().Be(SegmentPriming.Alternate);
    }

    [Fact]
    public void ToSessionOptions_starts_every_segment_fresh_for_MuPT()
    {
        //Arrange
        MusicGeneratorRegistry.Register(MuPTModel.Instance);

        //Act
        MusicGenerationOptions session = Map(new GeneratedMusicOptions(), SampleRate, out _, out _);

        //Assert
        session.SegmentPriming.Should().Be(SegmentPriming.Fresh);
    }

    [Fact]
    public void ToSessionOptions_keeps_the_package_default_priming_for_another_family_and_honours_an_explicit_choice()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new RecordingMusicGenerator("OtherFamilyGenerator", "Other"));

        //Act
        MusicGenerationOptions byDefault = Map(new GeneratedMusicOptions(), SampleRate, out _, out _);
        MusicGenerationOptions chosen = Map(
            new GeneratedMusicOptions { SegmentPriming = SegmentPriming.Fresh }, SampleRate, out _, out _);

        //Assert
        byDefault.SegmentPriming.Should().Be(new MusicGenerationOptions().SegmentPriming);
        chosen.SegmentPriming.Should().Be(SegmentPriming.Fresh);
    }

    [Fact]
    public void ToSessionOptions_lets_words_win_over_a_preset()
    {
        //Arrange
        MusicGeneratorRegistry.Register(SkyTNTModel.Instance);

        //Act
        MusicGenerationOptions session = Map(
            new GeneratedMusicOptions { Preset = "ClubArrangement", Text = "dark" }, SampleRate, out _, out _);

        //Assert
        session.Request.Intent.Should().NotBeNull();
        session.Request.Intent!.CharacterWords.Should().ContainSingle().Which.Should().Be("dark");
        session.Request.Intent.DrumKit.Should().BeNull("the preset was not used");
    }

    [Fact]
    public void FromWords_turns_known_character_words_into_character_words()
    {
        //Arrange & Act
        MusicRequest request = GeneratedMusicRequests.FromWords("dark, driving");

        //Assert
        request.Text.Should().BeNull();
        request.Intent.Should().NotBeNull();
        request.Intent!.CharacterWords.Should().Equal("dark", "driving");
    }

    [Fact]
    public void FromWords_passes_anything_else_on_as_free_text()
    {
        //Arrange & Act
        MusicRequest request = GeneratedMusicRequests.FromWords("  something for a boss fight  ");

        //Assert
        request.Text.Should().Be("something for a boss fight");
    }

    [Fact]
    public void EnsurePresetExists_refuses_a_name_that_is_no_preset_and_lists_the_presets()
    {
        //Arrange
        Action act = () => GeneratedMusicRequests.EnsurePresetExists("NoSuchPreset");

        //Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*NoSuchPreset*ClubArrangement*JigInD*");
    }

    [Fact]
    public void EnsurePresetExists_accepts_no_preset_and_any_preset_of_any_family()
    {
        //Arrange
        Action act = () =>
        {
            GeneratedMusicRequests.EnsurePresetExists(null);
            GeneratedMusicRequests.EnsurePresetExists("FourOnTheFloor");
            GeneratedMusicRequests.EnsurePresetExists("jigind");
        };

        //Act & Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void The_family_words_are_the_ones_the_model_packages_report() =>
        (GeneratedMusicRequests.SkyTNTFamily, GeneratedMusicRequests.MuPTFamily)
            .Should().Be((SkyTNTModel.Instance.Family, MuPTModel.Instance.Family));

    private static MusicGenerationOptions Map(
        GeneratedMusicOptions options, int sampleRate, out IMusicGenerator generator, out bool fellBack) =>
        GeneratedMusicRequests.ToSessionOptions(options, sampleRate, out generator, out fellBack);
}
