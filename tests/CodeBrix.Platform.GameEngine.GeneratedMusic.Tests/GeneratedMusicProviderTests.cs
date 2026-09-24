using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Replay;
using CodeBrix.Platform.GameEngine.Audio;
using Microsoft.Extensions.Logging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Covers <see cref="GeneratedMusicProvider"/> driven directly, the way the engine's fill thread
/// drives it: its states, the music it renders (the embedded replay through the synthesized General
/// MIDI library - no model is loaded), stopping and restarting, the degraded paths, follow-ups and
/// the model memory rules.
/// </summary>
public class GeneratedMusicProviderTests : GeneratedMusicTestContext
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_new_provider_is_stopped_and_has_nothing_to_report()
    {
        //Arrange & Act
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());

        //Assert
        provider.Name.Should().Be(GeneratedMusicProvider.ProviderName);
        provider.State.Should().Be(StreamingMusicState.Stopped);
        provider.Fault.Should().BeNull();
        provider.Description.Should().Contain("not started");
        provider.ActiveSource.Should().BeNull();
        provider.ActiveSourceSummary.Should().BeEmpty();
        provider.Diagnostics.Should().BeNull();
        provider.GenerationError.Should().BeNull();
        provider.SampleRate.Should().Be(0);
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        //Arrange
        Action act = () => _ = new GeneratedMusicProvider(null!);

        //Act & Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_refuses_a_preset_name_that_no_family_has()
    {
        //Arrange
        Action act = () => _ = new GeneratedMusicProvider(new GeneratedMusicOptions { Preset = "NoSuchPreset" });

        //Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*NoSuchPreset*ClubArrangement*");
    }

    [Fact]
    public void Constructor_refuses_a_negative_master_volume()
    {
        //Arrange
        Action act = () => _ = new GeneratedMusicProvider(new GeneratedMusicOptions { MasterVolume = -0.1F });

        //Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_provider_keeps_its_own_copy_of_the_options()
    {
        //Arrange
        var options = new GeneratedMusicOptions { TrackKey = "theme" };
        using var provider = new GeneratedMusicProvider(options);

        //Act
        options.TrackKey = "changed";

        //Assert
        provider.Options.TrackKey.Should().Be("theme");
        provider.Options.Should().NotBeSameAs(provider.Options, "every read is a copy");
    }

    [Fact]
    public void Start_returns_at_once_in_the_Starting_state()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var clock = Stopwatch.StartNew();

        //Act
        provider.Start(SampleRate, 2);

        //Assert
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "slow work belongs on the worker");
        provider.State.Should().Be(StreamingMusicState.Starting, "only a render reports the music as playing");
        provider.SampleRate.Should().Be(SampleRate);
    }

    [Fact]
    public void Render_produces_audible_music_from_the_embedded_replay_within_a_bounded_number_of_pulls()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);

        //Act
        int pulls = PullUntilAudible(provider, Generous);

        //Assert
        pulls.Should().BeGreaterThan(0);
        provider.State.Should().Be(StreamingMusicState.Playing);
        provider.ActiveSource.Should().NotBeNull();
        provider.ActiveSource!.IsReplay.Should().BeTrue();
        provider.ActiveSource.InstrumentLibraryName.Should().Be("ModestSynthGm");
        provider.ActiveSourceSummary.Should().Contain(EmbeddedReplay.Midi);
        provider.Description.Should().Be(provider.ActiveSourceSummary);
        provider.Diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void Render_fills_every_frame_asked_for_once_the_music_plays()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        var left = new float[300];
        var right = new float[300];

        //Act
        int written = provider.Render(left, right);

        //Assert
        written.Should().Be(300);
    }

    [Fact]
    public void Render_before_the_first_start_renders_nothing()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());

        //Act
        int written = provider.Render(new float[64], new float[64]);

        //Assert
        written.Should().Be(0);
    }

    [Fact]
    public void With_no_model_registered_the_replay_plays_and_one_Information_line_names_the_fix()
    {
        //Arrange
        var logger = new RecordingLogger();
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { Preset = "ClubArrangement" })
        {
            LoggerOverride = logger,
        };

        //Act
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        provider.Stop();
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);

        //Assert
        var fallbackLines = logger.Lines.Where(line => line.Message.Contains("embedded replay")).ToArray();
        fallbackLines.Should().ContainSingle("the degraded path is announced once, not on every start");
        fallbackLines[0].Level.Should().Be(LogLevel.Information);
        fallbackLines[0].Message.Should().Contain("Register a model package's generator")
            .And.Contain("SkyTNTModel.Register()")
            .And.Contain("the preset 'ClubArrangement'");
        provider.ActiveSource!.IsReplay.Should().BeTrue();
    }

    [Fact]
    public void Stop_stops_the_music_reports_Stopped_and_renders_nothing_more()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        var changes = 0;
        provider.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        //Act
        provider.Stop();
        int written = provider.Render(new float[BlockFrames], new float[BlockFrames]);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Stopped);
        written.Should().Be(0);
        changes.Should().Be(1);
    }

    [Fact]
    public void Stop_is_safe_when_already_stopped()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        Action act = () =>
        {
            provider.Stop();
            provider.Stop();
        };

        //Act & Assert
        act.Should().NotThrow();
        provider.State.Should().Be(StreamingMusicState.Stopped);
    }

    [Fact]
    public void Start_after_Stop_begins_again_from_the_beginning()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        MusicDiagnostics first = provider.Diagnostics!;
        provider.Stop();

        //Act
        provider.Start(SampleRate, 2);
        MusicDiagnostics restarted = provider.Diagnostics!;
        StreamingMusicState afterStart = provider.State;
        PullUntilAudible(provider, Generous);

        //Assert
        afterStart.Should().Be(StreamingMusicState.Starting);
        restarted.Should().NotBeSameAs(first);
        restarted.SegmentCount.Should().Be(0, "a restart is a new session with nothing generated yet");
        provider.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void Start_is_stopped_while_it_is_still_starting_without_a_fault()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());

        //Act
        provider.Start(SampleRate, 2);
        provider.Stop();
        Thread.Sleep(200);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Stopped);
        provider.Fault.Should().BeNull();
        provider.Render(new float[64], new float[64]).Should().Be(0);
    }

    [Fact]
    public void With_no_instrument_library_registered_the_provider_faults_with_the_fix_and_renders_silence()
    {
        //Arrange
        MusicPackageRegistries.ResetInstrumentLibraries();
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        var faulted = 0;
        provider.StateChanged += (_, _) =>
        {
            if (provider.State == StreamingMusicState.Faulted) { Interlocked.Increment(ref faulted); }
        };

        //Act
        provider.Start(SampleRate, 2);
        WaitForState(provider, StreamingMusicState.Faulted, Generous);
        int written = provider.Render(new float[BlockFrames], new float[BlockFrames]);

        //Assert
        provider.Fault.Should().BeOfType<InvalidOperationException>();
        provider.Fault!.Message.Should().Contain("GeneralMidiInstrumentLibrary.Register()");
        written.Should().Be(0);
        faulted.Should().Be(1);
    }

    [Fact]
    public void An_instrument_library_that_is_not_registered_faults_the_music_rather_than_the_game()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { InstrumentLibrary = "NoSuchLibrary" });

        //Act
        provider.Start(SampleRate, 2);
        WaitForState(provider, StreamingMusicState.Faulted, Generous);

        //Assert
        provider.Fault!.Message.Should().Contain("NoSuchLibrary");
    }

    [Fact]
    public void A_generator_that_is_not_registered_faults_the_music_rather_than_the_game()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { Generator = "NoSuchGenerator" });
        Action act = () => provider.Start(SampleRate, 2);

        //Act & Assert
        act.Should().NotThrow();
        provider.State.Should().Be(StreamingMusicState.Faulted);
        provider.Fault!.Message.Should().Contain("NoSuchGenerator");
    }

    [Fact]
    public void A_preset_of_another_family_faults_the_music_with_the_presets_that_fit()
    {
        //Arrange
        MusicGeneratorRegistry.Register(new RecordingMusicGenerator("TestMuPT", GeneratedMusicRequests.MuPTFamily));
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { Preset = "ClubArrangement" });

        //Act
        provider.Start(SampleRate, 2);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Faulted);
        provider.Fault.Should().BeOfType<ArgumentException>();
        provider.Fault!.Message.Should().Contain("WaltzDuetInAMinor");
    }

    [Fact]
    public void Words_a_generator_will_not_read_fault_the_music_rather_than_the_game()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions
        {
            Generator = EmbeddedReplay.Abc,
            Text = "something heroic for the final boss",
        });

        //Act
        provider.Start(SampleRate, 2);
        WaitForState(provider, StreamingMusicState.Faulted, Generous);

        //Assert
        provider.Fault.Should().BeAssignableTo<MusicGenerationException>();
        provider.Render(new float[64], new float[64]).Should().Be(0);
    }

    [Fact]
    public void A_named_model_family_generator_plays_its_preset_request()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { Preset = "ClubArrangement", Seed = 3 });

        //Act
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);

        //Assert
        MusicRequest first = generator.Requests[0];
        first.Intent!.DrumKit.Should().Be(SkyTNTPresets.ClubArrangement.CreateRequest().Intent!.DrumKit);
        first.Seed.Should().Be(3);
        provider.ActiveSource!.IsReplay.Should().BeFalse();
        provider.ActiveSource.GeneratorName.Should().Be("TestSkyTNT");
    }

    [Fact]
    public void FollowUp_by_preset_name_asks_the_playing_generator_for_that_preset()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        int? drumKit = SkyTNTPresets.AmbientElectronica.CreateRequest().Intent!.DrumKit;

        //Act
        provider.FollowUp("ambientelectronica");

        //Assert
        PullUntil(provider, () => generator.Requests.Any(request => request.Intent is { DrumKit: not null } intent && intent.DrumKit == drumKit));
        provider.State.Should().NotBe(StreamingMusicState.Faulted);
    }

    [Fact]
    public void FollowUp_by_character_words_asks_for_them_as_character_words()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);

        //Act
        provider.FollowUp("dark driving");

        //Assert
        PullUntil(provider, () => generator.Requests.Any(request =>
            request.Intent is not null && request.Intent.CharacterWords.SequenceEqual(["dark", "driving"])));
    }

    [Fact]
    public void FollowUp_in_words_the_generator_does_not_read_throws_and_the_music_plays_on()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        Action act = () => provider.FollowUp("something darker for the boss");

        //Act & Assert
        act.Should().Throw<MusicGenerationException>();
        provider.State.Should().NotBe(StreamingMusicState.Faulted);
        PullUntilAudible(provider, Generous);
    }

    [Fact]
    public void FollowUp_to_a_preset_of_another_family_throws_a_clear_ArgumentException()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        Action act = () => provider.FollowUp("ClubArrangement");

        //Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*ClubArrangement*SkyTNT*");
    }

    [Fact]
    public void FollowUp_with_options_naming_an_unknown_preset_throws_a_clear_ArgumentException()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        Action act = () => provider.FollowUp(new GeneratedMusicOptions { Preset = "NoSuchPreset" });

        //Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*NoSuchPreset*");
    }

    [Fact]
    public void FollowUp_with_options_naming_an_unregistered_generator_throws_listing_what_is_registered()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        Action act = () => provider.FollowUp(new GeneratedMusicOptions { Generator = "NoSuchGenerator" });

        //Act & Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*NoSuchGenerator*");
    }

    [Fact]
    public void FollowUp_with_options_can_change_the_generator()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);

        //Act
        provider.FollowUp(new GeneratedMusicOptions { Generator = EmbeddedReplay.AbcSecond });

        //Assert
        PullUntil(provider, () => provider.ActiveSource?.GeneratorName == EmbeddedReplay.AbcSecond);
    }

    [Fact]
    public void FollowUp_made_while_the_music_is_starting_is_made_once_it_plays()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        int? drumKit = SkyTNTPresets.FourOnTheFloor.CreateRequest().Intent!.DrumKit;

        //Act
        provider.Start(SampleRate, 2);
        provider.FollowUp("FourOnTheFloor");

        //Assert
        PullUntil(provider, () => generator.Requests.Any(request => request.Intent is { DrumKit: not null } intent && intent.DrumKit == drumKit));
    }

    [Fact]
    public void FollowUp_when_not_started_throws_an_InvalidOperationException()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        Action act = () => provider.FollowUp("JigInD");

        //Act & Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*not playing*");
    }

    [Fact]
    public void FollowUp_rejects_blank_words_and_null_options()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        Action blank = () => provider.FollowUp("  ");
        Action none = () => provider.FollowUp((GeneratedMusicOptions)null!);

        //Act & Assert
        blank.Should().Throw<ArgumentException>();
        none.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Release_while_the_music_plays_is_refused()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        Action act = provider.Release;

        //Act & Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Stop it*");
    }

    [Fact]
    public void Release_after_Stop_gives_the_generators_memory_back()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        provider.Stop();

        //Act
        provider.Release();

        //Assert
        generator.ReleaseCount.Should().Be(1);
        generator.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void Dispose_stops_the_music_releases_the_generator_and_is_safe_to_repeat()
    {
        //Arrange
        var generator = new RecordingMusicGenerator("TestSkyTNT", GeneratedMusicRequests.SkyTNTFamily);
        MusicGeneratorRegistry.Register(generator);
        var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);

        //Act
        provider.Dispose();
        provider.Dispose();

        //Assert
        provider.State.Should().Be(StreamingMusicState.Stopped);
        generator.ReleaseCount.Should().Be(1);
        provider.Render(new float[64], new float[64]).Should().Be(0);
    }

    [Fact]
    public void Start_after_Dispose_reports_a_fault_instead_of_throwing()
    {
        //Arrange
        var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Dispose();

        //Act
        provider.Start(SampleRate, 2);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Faulted);
        provider.Fault.Should().BeOfType<ObjectDisposedException>();
    }

    [Fact]
    public void FollowUp_after_Dispose_throws_ObjectDisposedException()
    {
        //Arrange
        var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        provider.Dispose();
        Action act = () => provider.FollowUp("JigInD");

        //Act & Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    //Pulls the provider, as the fill thread would, until a condition holds or the time is up
    private static void PullUntil(IStreamingMusicProvider provider, Func<bool> condition)
    {
        var left = new float[BlockFrames];
        var right = new float[BlockFrames];
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            clock.Elapsed.Should().BeLessThan(Generous, "the condition should have held by now");
            provider.Render(left, right);
            Thread.Sleep(2);
        }
    }
}
