using System;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers quantised transitions on <see cref="MusicManager"/>: waiting for a beat or a bar before a
/// change starts, and the jump points a MIDI file's markers become. A fake track supplies a position
/// and a timeline, and the fade clock is advanced by hand, so every wait asserted here is exact.
/// </summary>
public class MusicQuantizedTransitionTests : IDisposable
{
    private readonly MusicManager _manager = MusicManager.Instance;

    /// <summary>Puts the manager into a known state and takes manual control of the fade clock.</summary>
    public MusicQuantizedTransitionTests()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
    }

    /// <summary>Leaves nothing behind — the manager is a process-wide singleton.</summary>
    public void Dispose()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
    }

    [Fact]
    public void A_bar_quantised_crossfade_waits_for_the_bar_line_before_it_starts()
    {
        //Arrange - 120 BPM in 4/4: a bar is 2 seconds. We are half a second into bar two.
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);

        //Act
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Assert - queued, not started.
        _manager.HasPendingTransition.Should().BeTrue();
        _manager.NowPlaying.Should().BeSameAs(playing);
        next.Started.Should().BeFalse();

        //Act - 1.4 of the 1.5 second wait.
        _manager.Ticker.Tick(1.4);

        //Assert - still waiting.
        next.Started.Should().BeFalse();

        //Act - cross the bar line.
        _manager.Ticker.Tick(0.2);

        //Assert
        _manager.HasPendingTransition.Should().BeFalse();
        next.Started.Should().BeTrue();
        _manager.NowPlaying.Should().BeSameAs(next);
    }

    [Fact]
    public void A_beat_quantised_transition_waits_only_for_the_beat()
    {
        //Arrange - a beat is half a second at 120 BPM; we are 0.3 into one.
        var playing = NewTrack("playing", TimeSpan.FromSeconds(0.3));
        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);

        //Act
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Beat);
        _manager.Ticker.Tick(0.19);

        //Assert
        next.Started.Should().BeFalse();

        //Act
        _manager.Ticker.Tick(0.02);

        //Assert
        next.Started.Should().BeTrue();
    }

    [Fact]
    public void A_transition_asked_for_exactly_on_a_boundary_happens_at_once()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.FromSeconds(4.0));
        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);

        //Act
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Assert - waiting a whole extra bar because we were punctual would be the wrong answer.
        _manager.HasPendingTransition.Should().BeFalse();
        next.Started.Should().BeTrue();
    }

    [Fact]
    public void A_track_without_a_timeline_transitions_immediately()
    {
        //Arrange - a decoded audio file whose tempo the game never supplied, so Timeline stays null.
        var playing = new FakeTimedTrack("playing", TimeSpan.FromSeconds(2.5));
        playing.Timeline.Should().BeNull();

        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);

        //Act
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Assert - it happens, rather than being silently dropped or guessed at.
        _manager.HasPendingTransition.Should().BeFalse();
        next.Started.Should().BeTrue();
    }

    [Fact]
    public void Quantising_with_nothing_playing_starts_the_track_at_once()
    {
        //Arrange
        var next = NewTrack("next", TimeSpan.Zero);

        //Act - there is no grid to wait for, and no music to stay in step with.
        _manager.Play(next, TimeSpan.Zero, MusicTransitionQuantize.Bar);

        //Assert
        next.Started.Should().BeTrue();
        _manager.HasPendingTransition.Should().BeFalse();
    }

    [Fact]
    public void A_queued_transition_freezes_with_the_engine_pause()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Act - the ticker is what freezes, and the queued transition rides on it, so a transition
        //cannot fire while the game is paused.
        _manager.Ticker.Freeze();
        _manager.Ticker.IsFrozen.Should().BeTrue();

        //Assert
        next.Started.Should().BeFalse();

        //Act
        _manager.Ticker.Unfreeze();
        _manager.Ticker.Tick(1.5);

        //Assert
        next.Started.Should().BeTrue();
    }

    [Fact]
    public void CancelPendingTransition_drops_a_queued_change_and_leaves_the_music_alone()
    {
        //Arrange - the enemy died before the bar line arrived.
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        var next = NewTrack("next", TimeSpan.Zero);
        _manager.Play(playing);
        _manager.CrossfadeTo(next, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Act
        _manager.CancelPendingTransition();
        _manager.Ticker.Tick(5.0);

        //Assert
        _manager.HasPendingTransition.Should().BeFalse();
        next.Started.Should().BeFalse();
        _manager.NowPlaying.Should().BeSameAs(playing);
    }

    [Fact]
    public void Starting_something_else_outright_cancels_a_queued_transition()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        var queued = NewTrack("queued", TimeSpan.Zero);
        var instead = NewTrack("instead", TimeSpan.Zero);
        _manager.Play(playing);
        _manager.CrossfadeTo(queued, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);

        //Act
        _manager.Play(instead);
        _manager.Ticker.Tick(5.0);

        //Assert - the queued change must not land after the game changed its mind.
        queued.Started.Should().BeFalse();
        _manager.NowPlaying.Should().BeSameAs(instead);
    }

    [Fact]
    public void A_second_queued_transition_replaces_the_first()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        var first = NewTrack("first", TimeSpan.Zero);
        var second = NewTrack("second", TimeSpan.Zero);
        _manager.Play(playing);

        //Act
        _manager.CrossfadeTo(first, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);
        _manager.CrossfadeTo(second, TimeSpan.FromSeconds(1), MusicTransitionQuantize.Bar);
        _manager.Ticker.Tick(1.6);

        //Assert
        first.Started.Should().BeFalse();
        second.Started.Should().BeTrue();
    }

    [Fact]
    public void A_quantised_stop_waits_for_the_bar_too()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.FromSeconds(2.5));
        _manager.Play(playing);

        //Act
        _manager.Stop(TimeSpan.Zero, MusicTransitionQuantize.Bar);

        //Assert
        _manager.NowPlaying.Should().BeSameAs(playing);

        //Act
        _manager.Ticker.Tick(1.6);

        //Assert
        _manager.NowPlaying.Should().BeNull();
        playing.Stopped.Should().BeTrue();
    }

    // ----- markers -----

    [Fact]
    public void JumpToMarker_seeks_the_current_track()
    {
        //Arrange
        var timeline = new MusicTimeline(120, 4, 0, new[]
        {
            new MusicMarker("chorus", TimeSpan.FromSeconds(8)),
        });

        var playing = NewTrack("playing", TimeSpan.Zero, timeline);
        _manager.Play(playing);

        //Act
        var jumped = _manager.JumpToMarker("CHORUS");

        //Assert
        jumped.Should().BeTrue();
        playing.SoughtTo.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void JumpToMarker_reports_failure_rather_than_seeking_somewhere_arbitrary()
    {
        //Arrange
        var playing = NewTrack("playing", TimeSpan.Zero);
        _manager.Play(playing);

        //Act & Assert
        _manager.JumpToMarker("nowhere").Should().BeFalse();
        playing.SoughtTo.Should().BeNull();
    }

    [Fact]
    public void JumpToMarker_is_false_when_nothing_is_playing()
    {
        //Arrange & Act & Assert
        _manager.JumpToMarker("chorus").Should().BeFalse();
    }

    // ----- helpers -----

    private static FakeTimedTrack NewTrack(string key, TimeSpan position, MusicTimeline? timeline = null)
        => new(key, position) { Timeline = timeline ?? new MusicTimeline(120, 4) };

    /// <summary>A track that reports a fixed position and records what the manager asked it to do.</summary>
    private sealed class FakeTimedTrack : MusicTrack
    {
        private readonly TimeSpan _position;

        internal FakeTimedTrack(string key, TimeSpan position)
            : base(key)
            => _position = position;

        internal bool Started { get; private set; }

        internal bool Stopped { get; private set; }

        internal TimeSpan? SoughtTo { get; private set; }

        public override TimeSpan Position => _position;

        public override TimeSpan Duration => TimeSpan.FromMinutes(3);

        public override bool IsLooping { get; set; }

        public override bool IsPlaying => Started && !Stopped;

        public override void Seek(TimeSpan position) => SoughtTo = position;

        protected override void ApplyVolume(float volume)
        {
            // Nothing is sounding; the manager's arithmetic is what these tests are about.
        }

        internal override void StartCore(bool fromStart)
        {
            Started = true;
            Stopped = false;
        }

        internal override void PauseCore()
        {
        }

        internal override void ResumeCore()
        {
        }

        internal override void StopCore() => Stopped = true;

        public override void Dispose()
        {
        }
    }
}
