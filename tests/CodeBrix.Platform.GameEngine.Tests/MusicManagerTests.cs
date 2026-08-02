using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MusicManager"/>: transport, fades, crossfades, ducking and playlists. Nothing
/// here opens an audio device — a fake track records what the manager asked it to do — and fades are
/// advanced by hand rather than slept through, so the assertions are exact instead of racy.
/// </summary>
public class MusicManagerTests : IDisposable
{
    private readonly MusicManager _manager = MusicManager.Instance;

    /// <summary>Puts the manager into a known state and takes manual control of the fade clock.</summary>
    public MusicManagerTests()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
    }

    /// <summary>Leaves nothing behind for the next test — the manager is a process-wide singleton.</summary>
    public void Dispose()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
    }

    // ----- transport -----

    [Fact]
    public void Play_makes_a_track_current_and_starts_it()
    {
        //Arrange
        var track = new FakeMusicTrack("theme");

        //Act
        _manager.Play(track);

        //Assert
        _manager.NowPlaying.Should().BeSameAs(track);
        track.Started.Should().Be(1);
        track.Volume.Should().Be(1f, "a play with no fade starts at full volume");
    }

    [Fact]
    public void Play_with_a_fade_in_starts_silent_and_reaches_full_volume()
    {
        //Arrange
        var track = new FakeMusicTrack("theme");

        //Act
        _manager.Play(track, TimeSpan.FromSeconds(2));
        var atStart = track.Volume;
        _manager.Ticker.Tick(1.0);
        var halfway = track.Volume;
        _manager.Ticker.Tick(1.0);

        //Assert
        atStart.Should().Be(0f);
        halfway.Should().BeApproximately(0.5f, 0.01f);
        track.Volume.Should().Be(1f);
        _manager.ActiveFadeCount.Should().Be(0, "the fade should retire when it completes");
    }

    [Fact]
    public void Play_stops_the_track_it_replaces()
    {
        //Arrange
        var first = new FakeMusicTrack("first");
        var second = new FakeMusicTrack("second");
        _manager.Play(first);

        //Act
        _manager.Play(second);

        //Assert
        first.Stopped.Should().Be(1);
        _manager.NowPlaying.Should().BeSameAs(second);
    }

    [Fact]
    public void Stop_with_a_fade_out_silences_the_track_before_stopping_it()
    {
        //Arrange
        var track = new FakeMusicTrack("theme");
        _manager.Play(track);

        //Act
        _manager.Stop(TimeSpan.FromSeconds(1));
        var duringFade = track.Stopped;
        _manager.Ticker.Tick(1.0);

        //Assert
        duringFade.Should().Be(0, "the track must keep playing while it fades");
        track.Volume.Should().Be(0f);
        track.Stopped.Should().Be(1);
        _manager.NowPlaying.Should().BeNull();
    }

    // ----- crossfade -----

    [Fact]
    public void A_crossfade_keeps_constant_power_through_the_middle()
    {
        //Arrange - the reason equal-power is the default: with a linear law both sides sit at 0.5
        // halfway, about 6 dB down, and the transition audibly dips.
        var outgoing = new FakeMusicTrack("outgoing");
        var incoming = new FakeMusicTrack("incoming");
        _manager.CrossfadeCurve = MusicFadeCurve.EqualPower;
        _manager.Play(outgoing);

        //Act
        _manager.CrossfadeTo(incoming, TimeSpan.FromSeconds(4));
        _manager.Ticker.Tick(2.0);

        //Assert - sum of squares is 1 at the midpoint for an equal-power pair.
        var power = (incoming.Volume * incoming.Volume) + (outgoing.Volume * outgoing.Volume);
        power.Should().BeApproximately(1f, 0.01f);
        incoming.Volume.Should().BeApproximately(0.707f, 0.01f);
        outgoing.Volume.Should().BeApproximately(0.707f, 0.01f);
    }

    [Fact]
    public void A_crossfade_stops_the_outgoing_track_only_once_it_is_silent()
    {
        //Arrange
        var outgoing = new FakeMusicTrack("outgoing");
        var incoming = new FakeMusicTrack("incoming");
        _manager.Play(outgoing);

        //Act
        _manager.CrossfadeTo(incoming, TimeSpan.FromSeconds(2));
        _manager.Ticker.Tick(1.0);
        var midway = outgoing.Stopped;
        _manager.Ticker.Tick(1.0);

        //Assert
        midway.Should().Be(0);
        outgoing.Stopped.Should().Be(1);
        outgoing.Volume.Should().BeApproximately(0f, 0.001f);
        incoming.Volume.Should().BeApproximately(1f, 0.001f);
        _manager.NowPlaying.Should().BeSameAs(incoming);
    }

    [Fact]
    public void A_crossfade_from_silence_is_a_fade_in()
    {
        //Arrange
        var track = new FakeMusicTrack("theme");

        //Act
        _manager.CrossfadeTo(track, TimeSpan.FromSeconds(2));
        _manager.Ticker.Tick(2.0);

        //Assert
        _manager.NowPlaying.Should().BeSameAs(track);
        track.Volume.Should().Be(1f);
    }

    // ----- ducking -----

    [Fact]
    public void A_duck_attenuates_the_music_bus_and_releases_on_dispose()
    {
        //Arrange & Act
        var duck = _manager.PushDuck(0.25f);

        //Assert
        AudioMixer.MusicDuckMultiplier.Should().Be(0.25f);

        //Act
        duck.Dispose();

        //Assert
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void Overlapping_ducks_are_reference_counted_and_the_deepest_wins()
    {
        //Arrange - two dialogue lines overlapping is the case this exists for: the music must not
        // pop back up when the FIRST one ends.
        var shallow = _manager.PushDuck(0.6f);
        var deep = _manager.PushDuck(0.2f);

        //Assert
        AudioMixer.MusicDuckMultiplier.Should().Be(0.2f, "the deepest duck wins");

        //Act
        deep.Dispose();

        //Assert
        AudioMixer.MusicDuckMultiplier.Should().Be(0.6f, "the shallower duck is still held");

        //Act
        shallow.Dispose();

        //Assert
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void Releasing_a_duck_twice_is_harmless()
    {
        //Arrange
        var duck = _manager.PushDuck(0.5f);
        var other = _manager.PushDuck(0.4f);

        //Act
        duck.Dispose();
        duck.Dispose();

        //Assert - the second release must not cancel someone else's duck.
        AudioMixer.MusicDuckMultiplier.Should().Be(0.4f);
        other.Dispose();
        AudioMixer.MusicDuckMultiplier.Should().Be(1f);
    }

    [Fact]
    public void A_duck_fades_rather_than_jumping_when_given_an_attack()
    {
        //Arrange & Act
        using var duck = _manager.PushDuck(0f, TimeSpan.FromSeconds(1));
        var immediately = AudioMixer.MusicDuckMultiplier;
        _manager.Ticker.Tick(0.5);
        var halfway = AudioMixer.MusicDuckMultiplier;
        _manager.Ticker.Tick(0.5);

        //Assert
        immediately.Should().Be(1f);
        halfway.Should().BeApproximately(0.5f, 0.01f);
        AudioMixer.MusicDuckMultiplier.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Ducking_leaves_the_players_music_volume_alone()
    {
        //Arrange
        AudioMixer.MusicVolume = 0.6f;

        //Act
        using var duck = _manager.PushDuck(0.5f);

        //Assert - the slider setting must survive, because the duck has to be undone later.
        AudioMixer.MusicVolume.Should().Be(0.6f);
        AudioMixer.EffectiveVolume(1f, AudioBus.Music).Should().BeApproximately(0.3f, 0.001f);
    }

    // ----- playlists -----

    [Fact]
    public void A_playlist_plays_its_tracks_in_order()
    {
        //Arrange
        var a = new FakeMusicTrack("a");
        var b = new FakeMusicTrack("b");
        var playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.None };
        playlist.Add(a);
        playlist.Add(b);

        //Act
        _manager.Play(playlist);
        var first = _manager.NowPlaying;
        a.RaiseEndedForTest();
        var second = _manager.NowPlaying;

        //Assert
        first.Should().BeSameAs(a);
        second.Should().BeSameAs(b);
    }

    [Fact]
    public void A_playlist_that_does_not_repeat_stops_after_the_last_track()
    {
        //Arrange
        var only = new FakeMusicTrack("only");
        var playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.None };
        playlist.Add(only);

        //Act
        _manager.Play(playlist);
        only.RaiseEndedForTest();

        //Assert
        _manager.NowPlaying.Should().BeNull();
        only.Stopped.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_repeat_all_playlist_wraps_to_the_first_track()
    {
        //Arrange
        var a = new FakeMusicTrack("a");
        var b = new FakeMusicTrack("b");
        var playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.All };
        playlist.Add(a);
        playlist.Add(b);

        //Act
        _manager.Play(playlist);
        a.RaiseEndedForTest();
        b.RaiseEndedForTest();

        //Assert
        _manager.NowPlaying.Should().BeSameAs(a);
    }

    [Fact]
    public void Repeat_one_replays_the_same_track()
    {
        //Arrange
        var a = new FakeMusicTrack("a");
        var b = new FakeMusicTrack("b");
        var playlist = new MusicPlaylist { RepeatMode = MusicRepeatMode.One };
        playlist.Add(a);
        playlist.Add(b);

        //Act
        _manager.Play(playlist);
        a.RaiseEndedForTest();

        //Assert
        _manager.NowPlaying.Should().BeSameAs(a);
    }

    [Fact]
    public void A_seeded_shuffle_is_reproducible()
    {
        //Arrange
        var namesA = OrderFromSeededShuffle(seed: 12345);
        var namesB = OrderFromSeededShuffle(seed: 12345);

        //Assert
        namesA.Should().Equal(namesB);
    }

    [Fact]
    public void A_shuffled_playlist_does_not_replay_the_track_it_just_finished()
    {
        //Arrange - the "shuffle played the same song twice" complaint, which comes from reshuffling
        // without looking at what was last heard.
        var playlist = new MusicPlaylist(shuffleSeed: 7) { RepeatMode = MusicRepeatMode.All, Shuffle = true };
        for (var i = 0; i < 4; i++)
        {
            playlist.Add(new FakeMusicTrack($"track{i}"));
        }

        //Act & Assert - walk several full laps; a wrap must never repeat across the boundary.
        var previous = playlist.MoveNext();
        for (var step = 0; step < 20; step++)
        {
            var next = playlist.MoveNext();
            next.Should().NotBeSameAs(previous, $"step {step} repeated a track back to back");
            previous = next;
        }
    }

    private static List<string> OrderFromSeededShuffle(int seed)
    {
        var playlist = new MusicPlaylist(seed) { Shuffle = true, RepeatMode = MusicRepeatMode.None };
        for (var i = 0; i < 6; i++)
        {
            playlist.Add(new FakeMusicTrack($"track{i}"));
        }

        var order = new List<string>();
        while (playlist.MoveNext() is { } track)
        {
            order.Add(track.Key);
        }

        return order;
    }

    /// <summary>
    /// A track that records what the manager did to it and makes no sound, so the transport, fade
    /// and playlist logic can be asserted exactly without an audio device.
    /// </summary>
    private sealed class FakeMusicTrack : MusicTrack
    {
        private float _applied = 1f;
        private bool _playing;

        internal FakeMusicTrack(string key)
            : base(key)
        { }

        internal int Started { get; private set; }

        internal int Stopped { get; private set; }

        internal float AppliedVolume => _applied;

        public override TimeSpan Position => TimeSpan.Zero;

        public override TimeSpan Duration => TimeSpan.FromSeconds(30);

        public override bool IsLooping { get; set; }

        public override bool IsPlaying => _playing;

        internal void RaiseEndedForTest() => RaiseEnded();

        public override void Seek(TimeSpan position) { }

        protected override void ApplyVolume(float volume) => _applied = volume;

        internal override void StartCore(bool fromStart)
        {
            Started++;
            _playing = true;
        }

        internal override void PauseCore() => _playing = false;

        internal override void ResumeCore() => _playing = true;

        internal override void StopCore()
        {
            Stopped++;
            _playing = false;
        }

        public override void Dispose() { }
    }
}
