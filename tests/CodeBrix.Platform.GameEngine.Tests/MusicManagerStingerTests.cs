using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MusicManager"/>'s stingers - the music-bus form, the chosen-bus form and the
/// held-duck form - and the <see cref="IMusicManager"/> surface it implements. No audio device is
/// opened: stinger voices are handed to a recording hook instead of being played, and finished by
/// hand; fades are advanced by hand.
/// </summary>
public class MusicManagerStingerTests : IDisposable
{
    private const string Key = "stinger_test_sound";
    private const string MissingKey = "stinger_test_no_such_sound";

    private readonly MusicManager _manager = MusicManager.Instance;
    private readonly List<AudioResource> _played = new();

    /// <summary>Loads a short sound, takes manual control of the fade clock and records stinger voices.</summary>
    public MusicManagerStingerTests()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        _manager.StingerPlaybackForTests = voice => _played.Add(voice);
        AudioMixer.Reset();
        AudioResourceManager.Instance.LoadFromStream(Key, new MemoryStream(BuildMonoWav(8000, 80)), ".wav");
    }

    /// <summary>Finishes any stinger left playing and puts the singletons back.</summary>
    public void Dispose()
    {
        foreach (var voice in _manager.PlayingStingers)
        {
            _manager.CompleteStingerForTests(voice);
        }

        _manager.StingerPlaybackForTests = null;
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        AudioResourceManager.Instance.Unload(Key);
        AudioMixer.Reset();
    }

    [Fact]
    public void PlayStinger_still_plays_on_the_music_bus()
    {
        //Act
        var started = _manager.PlayStinger(Key, 0.8f);

        //Assert
        started.Should().BeTrue();
        _played.Count.Should().Be(1);
        _played[0].Bus.Should().Be(AudioBus.Music);
        _played[0].Volume.Should().BeApproximately(0.8f, 0.0001f);
        _played[0].IsLooping.Should().BeFalse();
    }

    [Fact]
    public void PlayStingerOnBus_plays_on_the_effects_bus_so_the_music_slider_leaves_it_alone()
    {
        //Arrange
        AudioMixer.MusicVolume = 0f;

        //Act
        var started = _manager.PlayStingerOnBus(Key, AudioBus.Sfx, 1.5f);

        //Assert
        started.Should().BeTrue();
        _played.Count.Should().Be(1);
        _played[0].Bus.Should().Be(AudioBus.Sfx);
        _played[0].Volume.Should().Be(1f, "the volume is clamped to 0..1");
        AudioMixer.EffectiveVolume(_played[0].Volume, _played[0].Bus).Should().Be(1f);
        _manager.PlayingStingers.Should().Contain(_played[0]);
    }

    [Fact]
    public void PlayStingerOnBus_with_a_duck_ducks_the_music_until_the_stinger_finishes()
    {
        //Act
        _manager.PlayStingerOnBus(Key, AudioBus.Sfx, duckMusic: true, duckDepth: 0.25f);
        _manager.Ticker.Tick(0.15);
        var ducked = _manager.DuckMultiplier;
        _manager.CompleteStingerForTests(_played[0]).Should().BeTrue();
        _manager.Ticker.Tick(0.4);

        //Assert
        ducked.Should().BeApproximately(0.25f, 0.001f);
        _manager.DuckMultiplier.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void PlayStingerOnBus_without_a_duck_leaves_the_music_alone()
    {
        //Act
        _manager.PlayStingerOnBus(Key, AudioBus.Sfx);
        _manager.Ticker.Tick(1.0);

        //Assert
        _manager.DuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void A_finished_stinger_is_unloaded_and_forgotten()
    {
        //Arrange
        _manager.PlayStingerOnBus(Key, AudioBus.Sfx);
        var voice = _played[0];

        //Act
        _manager.CompleteStingerForTests(voice);

        //Assert
        _manager.PlayingStingers.Should().NotContain(voice);
        AudioResourceManager.Instance.Contains(voice.Key).Should().BeFalse();
        AudioResourceManager.Instance.Contains(Key).Should().BeTrue("only the stinger's own voice is unloaded");
        _manager.CompleteStingerForTests(voice).Should().BeFalse();
    }

    [Fact]
    public void PlayStingerOnBus_returns_false_for_an_unloaded_key_and_does_not_duck()
    {
        //Act
        var started = _manager.PlayStingerOnBus(MissingKey, AudioBus.Sfx, duckMusic: true);

        //Assert
        started.Should().BeFalse();
        _played.Should().BeEmpty();
        _manager.DuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void PlayStingerWithHeldDuck_holds_the_duck_after_the_stinger_finishes_until_disposed()
    {
        //Act
        var duck = _manager.PlayStingerWithHeldDuck(Key, 0.2f);
        var voice = _played[0];
        _manager.CompleteStingerForTests(voice);
        var afterStinger = _manager.DuckMultiplier;
        duck.Dispose();

        //Assert
        voice.Bus.Should().Be(AudioBus.Sfx, "the held-duck form plays on the effects bus by default");
        afterStinger.Should().Be(0.2f);
        _manager.DuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void PlayStingerWithHeldDuck_fades_with_the_given_attack_and_release()
    {
        //Act
        var duck = _manager.PlayStingerWithHeldDuck(Key, 0f, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), AudioBus.Music, 0.5f);
        _manager.Ticker.Tick(1.0);
        var halfway = _manager.DuckMultiplier;
        _manager.Ticker.Tick(1.0);
        var down = _manager.DuckMultiplier;
        duck.Dispose();
        _manager.Ticker.Tick(1.0);

        //Assert
        _played[0].Bus.Should().Be(AudioBus.Music);
        _played[0].Volume.Should().Be(0.5f);
        halfway.Should().BeApproximately(0.5f, 0.01f);
        down.Should().BeApproximately(0f, 0.001f);
        _manager.DuckMultiplier.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void PlayStingerWithHeldDuck_still_ducks_when_the_key_is_not_loaded()
    {
        //Act
        using var duck = _manager.PlayStingerWithHeldDuck(MissingKey, 0.3f);

        //Assert
        _played.Should().BeEmpty();
        _manager.DuckMultiplier.Should().Be(0.3f);
    }

    [Fact]
    public void MusicVolume_is_the_music_slider()
    {
        //Act
        _manager.MusicVolume = 0.4f;

        //Assert
        AudioMixer.MusicVolume.Should().Be(0.4f);
        _manager.MusicVolume.Should().Be(0.4f);
    }

    [Fact]
    public void The_manager_drives_the_same_music_bus_through_IMusicManager()
    {
        //Arrange
        IMusicManager music = _manager;

        //Act
        var duck = music.PushDuck(0.5f);
        var ducked = music.DuckMultiplier;
        music.ClearDucks();
        var started = music.PlayStingerOnBus(Key, AudioBus.Sfx);

        //Assert
        ducked.Should().Be(0.5f);
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
        started.Should().BeTrue();
        duck.Dispose();
    }

    private static byte[] BuildMonoWav(int sampleRate, int sampleCount)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        int dataLength = sampleCount * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int i = 0; i < sampleCount; i++)
        {
            writer.Write((short)(i * 7));
        }

        writer.Flush();
        return ms.ToArray();
    }
}
