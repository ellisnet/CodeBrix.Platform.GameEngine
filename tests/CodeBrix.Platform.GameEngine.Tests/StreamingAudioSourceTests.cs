using System;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="StreamingAudioSource"/>'s mixer wiring. Nothing is played: constructing a source
/// claims a voice on the shared output, so the audio system is pinned before each test and shut down
/// after it.
/// </summary>
public class StreamingAudioSourceTests : IDisposable
{
    /// <summary>Pins the output format and restores the mixer defaults.</summary>
    public StreamingAudioSourceTests()
    {
        AudioMixer.Reset();
        AudioSystem.Initialize(48000, 2);
    }

    /// <summary>Restores the mixer and un-claims the shared output.</summary>
    public void Dispose()
    {
        AudioMixer.Reset();
        AudioSystem.Shutdown();
    }

    [Fact]
    public void Constructor_applies_the_mixers_current_music_gain()
    {
        //Arrange - the slider is down and a duck is active BEFORE the stream exists, which is exactly
        //when the gain used to be left at full until some volume happened to change.
        AudioMixer.MusicVolume = 0.5f;
        AudioMixer.SetMusicDuckMultiplier(0.5f);

        //Act
        using var source = new StreamingAudioSource(buffer => buffer.Clear());

        //Assert
        source.Bus.Should().Be(AudioBus.Music);
        source.AppliedGain.Should().BeApproximately(0.25f, 0.0001f);
    }

    [Fact]
    public void Volume_is_scaled_by_the_bus_the_duck_and_the_master()
    {
        //Arrange
        using var source = new StreamingAudioSource(buffer => buffer.Clear());
        AudioMixer.MasterVolume = 0.5f;
        AudioMixer.MusicVolume = 0.8f;

        //Act
        source.Volume = 0.5f;
        AudioMixer.SetMusicDuckMultiplier(0.5f);

        //Assert
        source.AppliedGain.Should().BeApproximately(0.5f * 0.8f * 0.5f * 0.5f, 0.0001f);
    }
}
