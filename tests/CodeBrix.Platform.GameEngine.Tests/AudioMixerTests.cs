using System;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="AudioMixer"/>: the master/music/sfx bus arithmetic and its live application to
/// voices that are already playing. Resets the mixer around every test — it is process-global state,
/// and leaking a changed volume would quietly alter every later audio test.
/// </summary>
public class AudioMixerTests : IDisposable
{
    /// <summary>Restores the mixer defaults before each test.</summary>
    public AudioMixerTests() => AudioMixer.Reset();

    /// <summary>Restores the mixer defaults after each test.</summary>
    public void Dispose() => AudioMixer.Reset();

    [Fact]
    public void AudioMixer_defaults_every_volume_to_unity()
    {
        //Arrange & Act & Assert - a game that never touches the mixer must sound unchanged.
        AudioMixer.MasterVolume.Should().Be(1f);
        AudioMixer.MusicVolume.Should().Be(1f);
        AudioMixer.SfxVolume.Should().Be(1f);
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
        AudioMixer.EffectiveVolume(1f, AudioBus.Music).Should().Be(1f);
        AudioMixer.EffectiveVolume(1f, AudioBus.Sfx).Should().Be(1f);
    }

    [Fact]
    public void AudioMixer_multiplies_the_voice_volume_by_its_bus_and_the_master()
    {
        //Arrange
        AudioMixer.MasterVolume = 0.5f;
        AudioMixer.MusicVolume = 0.5f;
        AudioMixer.SfxVolume = 1.0f;

        //Act
        var music = AudioMixer.EffectiveVolume(0.8f, AudioBus.Music);
        var sfx = AudioMixer.EffectiveVolume(0.8f, AudioBus.Sfx);

        //Assert
        music.Should().BeApproximately(0.8f * 0.5f * 0.5f, 0.0001f);
        sfx.Should().BeApproximately(0.8f * 1.0f * 0.5f, 0.0001f);
    }

    [Fact]
    public void The_none_bus_answers_to_the_master_volume_alone()
    {
        //Arrange
        AudioMixer.MasterVolume = 0.5f;
        AudioMixer.MusicVolume = 0f;
        AudioMixer.SfxVolume = 0f;

        //Act
        var unbussed = AudioMixer.EffectiveVolume(1f, AudioBus.None);

        //Assert
        unbussed.Should().Be(0.5f);
        AudioMixer.GetBusVolume(AudioBus.None).Should().Be(1f);
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(1.5f, 1f)]
    public void AudioMixer_clamps_a_volume_into_range(float set, float expected)
    {
        //Arrange & Act
        AudioMixer.MusicVolume = set;

        //Assert
        AudioMixer.MusicVolume.Should().Be(expected);
    }

    [Fact]
    public void A_bus_volume_change_reaches_a_voice_that_is_already_playing()
    {
        //Arrange - a raw-PCM resource is the cheapest real voice; it needs no file and no codec.
        const string key = "mixer_live_change_test";
        var manager = AudioResourceManager.Instance;

        try
        {
            var resource = manager.LoadFromPcm(key, new byte[64], 11025, 8);
            resource.Bus.Should().Be(AudioBus.Sfx, "resources default to the effects bus");
            resource.Volume = 1f;

            //Act - the point of the whole design: no walking of live voices by the game.
            AudioMixer.SfxVolume = 0.25f;

            //Assert - the voice's own Volume is untouched; only its audible gain moved.
            resource.Volume.Should().Be(1f);
            AudioMixer.EffectiveVolume(resource.Volume, resource.Bus).Should().BeApproximately(0.25f, 0.0001f);
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void Moving_a_voice_to_the_music_bus_changes_which_slider_controls_it()
    {
        //Arrange
        const string key = "mixer_bus_move_test";
        var manager = AudioResourceManager.Instance;

        try
        {
            var resource = manager.LoadFromPcm(key, new byte[64], 11025, 8);
            AudioMixer.SfxVolume = 0f;
            AudioMixer.MusicVolume = 1f;

            //Act
            resource.Bus = AudioBus.Music;

            //Assert
            AudioMixer.EffectiveVolume(resource.Volume, resource.Bus).Should().Be(1f);
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void Ducking_attenuates_the_music_bus_without_touching_the_music_volume()
    {
        //Arrange - the reason ducking is a separate multiplier: it must not overwrite the value the
        // player chose on their music slider, because it has to be restored afterwards.
        AudioMixer.MusicVolume = 0.8f;

        //Act
        AudioMixer.SetMusicDuckMultiplier(0.25f);

        //Assert
        AudioMixer.MusicVolume.Should().Be(0.8f);
        AudioMixer.MusicDuckMultiplier.Should().Be(0.25f);
        AudioMixer.EffectiveVolume(1f, AudioBus.Music).Should().BeApproximately(0.8f * 0.25f, 0.0001f);
        AudioMixer.EffectiveVolume(1f, AudioBus.Sfx).Should().Be(1f, "ducking is a music-bus concern");
    }

    [Fact]
    public void Reset_restores_the_defaults_including_ducking()
    {
        //Arrange
        AudioMixer.MasterVolume = 0.1f;
        AudioMixer.MusicVolume = 0.2f;
        AudioMixer.SfxVolume = 0.3f;
        AudioMixer.SetMusicDuckMultiplier(0.4f);

        //Act
        AudioMixer.Reset();

        //Assert
        AudioMixer.MasterVolume.Should().Be(1f);
        AudioMixer.MusicVolume.Should().Be(1f);
        AudioMixer.SfxVolume.Should().Be(1f);
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
    }
}
