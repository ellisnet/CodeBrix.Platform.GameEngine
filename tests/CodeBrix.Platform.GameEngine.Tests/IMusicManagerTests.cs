using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="IMusicManager"/> as the seam it is meant to be: <see cref="MusicManager"/>
/// implements it, and a music policy written against it is testable with a recording fake and no
/// audio at all.
/// </summary>
public class IMusicManagerTests
{
    [Fact]
    public void MusicManager_is_an_IMusicManager() =>
        (MusicManager.Instance as IMusicManager).Should().NotBeNull();

    [Fact]
    public void A_music_policy_can_be_tested_against_a_recording_fake()
    {
        //Arrange
        var music = new RecordingMusicManager();
        var policy = new ExamplePolicy(music);

        //Act
        policy.SetMusicSlider(0.6);
        policy.OnPause();
        policy.OnPause();
        policy.OnResume();
        policy.OnGameOver();

        //Assert
        music.MusicVolume.Should().Be(0.6f);
        music.Calls.Should().Equal("PushDuck 0.35", "release", "Stinger game-over Sfx held 0.2");
    }

    /// <summary>A tiny policy written only against the seam, as a game's would be.</summary>
    private sealed class ExamplePolicy(IMusicManager music)
    {
        private IDisposable? _pauseDuck;
        private IDisposable? _gameOverDuck;

        public void SetMusicSlider(double value) => music.MusicVolume = (float)Math.Clamp(value, 0.0, 1.0);

        public void OnPause() => _pauseDuck ??= music.PushDuck(0.35f, TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.5));

        public void OnResume()
        {
            _pauseDuck?.Dispose();
            _pauseDuck = null;
        }

        public void OnGameOver() =>
            _gameOverDuck ??= music.PlayStingerWithHeldDuck("game-over", 0.2f, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1.5));
    }

    private sealed class RecordingMusicManager : IMusicManager
    {
        public List<string> Calls { get; } = [];

        public float MusicVolume { get; set; } = 1f;

        public float DuckMultiplier => 1f;

        public IDisposable PushDuck(float depth, TimeSpan attack = default, TimeSpan release = default)
        {
            Calls.Add(string.Create(CultureInfo.InvariantCulture, $"PushDuck {depth:0.##}"));
            return new Releaser(Calls);
        }

        public void Duck(float depth, TimeSpan attack, TimeSpan hold, TimeSpan release) => Calls.Add(string.Create(CultureInfo.InvariantCulture, $"Duck {depth:0.##}"));

        public void ClearDucks(TimeSpan release = default) => Calls.Add("ClearDucks");

        public bool PlayStinger(string resourceKey, float volume = 1.0f, bool duckMusic = false, float duckDepth = 0.3f)
        {
            Calls.Add($"Stinger {resourceKey}");
            return true;
        }

        public bool PlayStingerOnBus(string resourceKey, AudioBus bus, float volume = 1.0f, bool duckMusic = false, float duckDepth = 0.3f)
        {
            Calls.Add($"Stinger {resourceKey} {bus}");
            return true;
        }

        public IDisposable PlayStingerWithHeldDuck(string resourceKey, float duckDepth, TimeSpan attack = default, TimeSpan release = default,
                                                   AudioBus bus = AudioBus.Sfx, float volume = 1.0f)
        {
            Calls.Add(string.Create(CultureInfo.InvariantCulture, $"Stinger {resourceKey} {bus} held {duckDepth:0.##}"));
            return new Releaser(Calls);
        }

        private sealed class Releaser(List<string> calls) : IDisposable
        {
            public void Dispose() => calls.Add("release");
        }
    }
}
