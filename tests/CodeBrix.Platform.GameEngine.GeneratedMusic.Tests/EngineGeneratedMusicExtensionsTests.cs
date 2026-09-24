using System;
using System.Diagnostics;
using System.Threading;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Covers the one call a game makes, all the way through the engine: the provider is registered as
/// the engine's streaming music provider, played by the engine's music manager as a
/// <see cref="StreamingMusicTrack"/>, and heard through the music voice - a fake one, so no audio
/// device is ever opened. Also covers calling it again, starting later, and the degraded paths.
/// </summary>
public class EngineGeneratedMusicExtensionsTests : GeneratedMusicTestContext
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public void UseGeneratedMusic_registers_the_provider_and_plays_it_through_the_music_manager()
    {
        //Arrange & Act
        GeneratedMusicProvider provider = Engine.Instance.UseGeneratedMusic();

        //Assert
        Engine.Instance.Managers.StreamingMusic.Provider.Should().BeSameAs(provider);
        var track = MusicManager.Instance.NowPlaying.Should().BeOfType<StreamingMusicTrack>().Subject;
        track.Provider.Should().BeSameAs(provider);
        track.Key.Should().Be(GeneratedMusicOptions.DefaultTrackKey);
        track.SampleRate.Should().Be(SampleRate);
        provider.SampleRate.Should().Be(SampleRate, "the provider renders at the engine output's rate");
        Voices.Should().ContainSingle();
    }

    [Fact]
    public void UseGeneratedMusic_music_is_heard_through_the_engine_and_the_track_reports_Playing()
    {
        //Arrange
        GeneratedMusicProvider provider = Engine.Instance.UseGeneratedMusic();
        var track = (StreamingMusicTrack)MusicManager.Instance.NowPlaying!;

        //Act
        PumpUntilAudible(Voices[0]);

        //Assert
        track.State.Should().Be(StreamingMusicState.Playing);
        track.Position.Should().BeGreaterThan(TimeSpan.Zero, "the track's clock runs as it is pulled");
        provider.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void UseGeneratedMusic_plays_under_the_track_key_the_options_give()
    {
        //Arrange & Act
        Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions { TrackKey = "title-theme" });

        //Assert
        MusicManager.Instance.NowPlaying!.Key.Should().Be("title-theme");
    }

    [Fact]
    public void UseGeneratedMusic_without_StartImmediately_only_registers_and_PlayStreaming_starts_it_later()
    {
        //Arrange
        GeneratedMusicProvider provider = Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions { StartImmediately = false });
        MusicTrack? before = MusicManager.Instance.NowPlaying;
        StreamingMusicState stateBefore = provider.State;

        //Act
        StreamingMusicTrack track = MusicManager.Instance.PlayStreaming();
        PumpUntilAudible(Voices[0]);

        //Assert
        before.Should().BeNull();
        stateBefore.Should().Be(StreamingMusicState.Stopped);
        Engine.Instance.Managers.StreamingMusic.Provider.Should().BeSameAs(provider);
        track.Provider.Should().BeSameAs(provider);
        provider.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void UseGeneratedMusic_called_again_replaces_and_disposes_the_provider_it_created_before()
    {
        //Arrange
        GeneratedMusicProvider first = Engine.Instance.UseGeneratedMusic();
        var firstTrack = (StreamingMusicTrack)MusicManager.Instance.NowPlaying!;
        PumpUntilAudible(Voices[0]);
        var ended = 0;
        firstTrack.Ended += (_, _) => Interlocked.Increment(ref ended);

        //Act
        GeneratedMusicProvider second = Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions { TrackKey = "second" });
        PumpUntilAudible(Voices[^1]);

        //Assert
        second.Should().NotBeSameAs(first);
        Engine.Instance.Managers.StreamingMusic.Provider.Should().BeSameAs(second);
        MusicManager.Instance.NowPlaying!.Key.Should().Be("second");
        first.State.Should().Be(StreamingMusicState.Stopped);
        ended.Should().Be(1, "the replaced provider was stopped under the track that played it");
        Action useFirst = () => first.FollowUp("JigInD");
        useFirst.Should().Throw<ObjectDisposedException>("the provider the first call created was disposed");
        second.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void UseGeneratedMusic_with_no_instrument_library_leaves_the_engine_running_in_silence()
    {
        //Arrange
        MusicPackageRegistries.ResetInstrumentLibraries();

        //Act
        GeneratedMusicProvider provider = Engine.Instance.UseGeneratedMusic();
        WaitForState(provider, StreamingMusicState.Faulted, Generous);
        float[] block = Voices[0].Pump(BlockFrames);

        //Assert
        Peak(block).Should().Be(0f);
        var track = (StreamingMusicTrack)MusicManager.Instance.NowPlaying!;
        track.State.Should().Be(StreamingMusicState.Faulted);
        track.Fault!.Message.Should().Contain("GeneralMidiInstrumentLibrary.Register()");
    }

    [Fact]
    public void UseGeneratedMusic_rejects_null_arguments()
    {
        //Arrange
        Action noEngine = () => EngineGeneratedMusicExtensions.UseGeneratedMusic(null!);
        Action noOptions = () => Engine.Instance.UseGeneratedMusic(null!);

        //Act & Assert
        noEngine.Should().Throw<ArgumentNullException>();
        noOptions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseGeneratedMusic_refuses_a_misspelt_preset_before_touching_the_engine()
    {
        //Arrange
        Action act = () => Engine.Instance.UseGeneratedMusic(new GeneratedMusicOptions { Preset = "ClubArangement" });

        //Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*ClubArangement*ClubArrangement*");
        Engine.Instance.Managers.StreamingMusic.HasProvider.Should().BeFalse();
    }

    //Pulls the engine's music voice, as the audio device would, until it carries audible sound
    private static void PumpUntilAudible(FakeMusicVoice voice)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < Generous)
        {
            if (Peak(voice.Pump(BlockFrames)) > 0.001f) { return; }
            Thread.Sleep(2);
        }

        throw new TimeoutException($"The engine's music voice carried no audible sound in {Generous.TotalSeconds:0} s.");
    }
}
