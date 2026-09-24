using System;
using System.Threading;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="StreamingMusicRegistry"/> — one active provider, replaced by stopping and never
/// by disposing — and <see cref="MusicManager.PlayStreaming"/>, the one call a game makes. Tracks
/// play through a fake voice, so no audio device is opened.
/// </summary>
public class StreamingMusicRegistryTests : IDisposable
{
    private readonly StreamingMusicRegistry _registry = StreamingMusicRegistry.Instance;
    private readonly MusicManager _manager = MusicManager.Instance;
    private readonly Func<ISampleProvider, IStreamingMusicVoice> _originalFactory;

    /// <summary>Starts every test with no provider, no music and a pinned output format.</summary>
    public StreamingMusicRegistryTests()
    {
        _manager.Stop();
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
        _registry.Unregister();
        AudioSystem.Initialize(48000, 2);

        _originalFactory = StreamingMusicTrack.DefaultVoiceFactory;
        StreamingMusicTrack.DefaultVoiceFactory = stream => new FakeStreamingMusicVoice(stream);
    }

    /// <summary>Leaves no provider, no music and no claimed output behind.</summary>
    public void Dispose()
    {
        _manager.Stop();
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
        _registry.Unregister();
        StreamingMusicTrack.DefaultVoiceFactory = _originalFactory;
        AudioSystem.Shutdown();
    }

    [Fact]
    public void The_engine_exposes_the_shared_registry_through_its_managers() =>
        Engine.Instance.Managers.StreamingMusic.Should().BeSameAs(_registry);

    [Fact]
    public void CreateTrack_without_a_provider_throws_an_InvalidOperationException_that_says_what_to_do()
    {
        //Arrange
        Action act = () => _registry.CreateTrack();

        //Act & Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*No streaming music provider is registered*Register*");
    }

    [Fact]
    public void Register_makes_a_provider_active_and_CreateTrack_builds_a_track_over_it()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider("Generated music");

        //Act
        _registry.Register(provider);
        using var track = _registry.CreateTrack();

        //Assert
        _registry.Provider.Should().BeSameAs(provider);
        _registry.HasProvider.Should().BeTrue();
        track.Provider.Should().BeSameAs(provider);
        track.Key.Should().Be("Generated music");
        track.State.Should().Be(StreamingMusicState.Stopped, "a new track is not playing yet");
        provider.StartCalls.Should().Be(0);
    }

    [Fact]
    public void Register_rejects_a_null_provider()
    {
        //Arrange
        Action act = () => _registry.Register(null!);

        //Act & Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_replaces_the_active_provider_by_stopping_it_and_never_disposing_it()
    {
        //Arrange
        var first = new FakeStreamingMusicProvider("first");
        var second = new FakeStreamingMusicProvider("second");
        _registry.Register(first);
        var track = _manager.PlayStreaming();
        var ended = 0;
        track.Ended += (_, _) => Interlocked.Increment(ref ended);

        //Act
        _registry.Register(second);

        //Assert
        _registry.Provider.Should().BeSameAs(second);
        first.StopCalls.Should().Be(1);
        first.Disposed.Should().BeFalse("disposal stays with whoever created the provider");
        ended.Should().Be(1, "the replaced provider was stopped under the track that was playing it");
        track.State.Should().Be(StreamingMusicState.Stopped);
    }

    [Fact]
    public void Registering_the_active_provider_again_does_nothing()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        _registry.Register(provider);
        _manager.PlayStreaming();

        //Act
        _registry.Register(provider);

        //Assert
        provider.StopCalls.Should().Be(0);
        provider.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void Unregister_stops_and_returns_the_provider_without_disposing_it()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        _registry.Register(provider);
        _manager.PlayStreaming();

        //Act
        var removed = _registry.Unregister();

        //Assert
        removed.Should().BeSameAs(provider);
        _registry.Provider.Should().BeNull();
        provider.StopCalls.Should().Be(1);
        provider.Disposed.Should().BeFalse();
    }

    [Fact]
    public void Unregister_with_nothing_registered_returns_null() => _registry.Unregister().Should().BeNull();

    [Fact]
    public void PlayStreaming_plays_the_registered_provider_with_a_fade_in()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        _registry.Register(provider);

        //Act
        var track = _manager.PlayStreaming(TimeSpan.FromSeconds(2));
        var atStart = track.Volume;
        _manager.Ticker.Tick(2.0);

        //Assert
        _manager.NowPlaying.Should().BeSameAs(track);
        provider.StartCalls.Should().Be(1);
        provider.LastSampleRate.Should().Be(48000);
        atStart.Should().Be(0f);
        track.Volume.Should().Be(1f);
        track.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void PlayStreaming_while_the_provider_is_already_streaming_keeps_the_same_track()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        _registry.Register(provider);
        var first = _manager.PlayStreaming();

        //Act
        var second = _manager.PlayStreaming(TimeSpan.FromSeconds(1));

        //Assert
        second.Should().BeSameAs(first);
        provider.StartCalls.Should().Be(1, "the stream is not restarted");
        provider.StopCalls.Should().Be(0);
    }

    [Fact]
    public void PlayStreaming_after_the_provider_faulted_starts_a_fresh_track()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        _registry.Register(provider);
        var first = _manager.PlayStreaming();
        provider.SetState(StreamingMusicState.Faulted);

        //Act
        var second = _manager.PlayStreaming();

        //Assert
        second.Should().NotBeSameAs(first);
        provider.StartCalls.Should().Be(2);
        second.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void PlayStreaming_without_a_provider_throws()
    {
        //Arrange
        Action act = () => _manager.PlayStreaming();

        //Act & Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
