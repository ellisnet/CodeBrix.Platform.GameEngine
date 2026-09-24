using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using Microsoft.Extensions.Logging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="StreamingMusicTrack"/>: the format it starts a provider at, the fill path (planar
/// to interleaved, silence for whatever the provider does not supply, chunking), its clock, its
/// states and when it raises Ended, the manager's fades, ducking and pause acting on it, the one-
/// stream-per-provider rule, and its logging. Nothing opens an audio device: the track plays through a
/// fake voice that the test pumps the way the audio thread would, and the manager's fades are ticked
/// by hand.
/// </summary>
public class StreamingMusicTrackTests : IDisposable
{
    private readonly MusicManager _manager = MusicManager.Instance;
    private readonly List<FakeStreamingMusicVoice> _voices = new();
    private readonly RecordingLogger _log = new();
    private readonly Func<ISampleProvider, IStreamingMusicVoice> _originalFactory;

    /// <summary>Puts the music system into a known state with no pinned output format.</summary>
    public StreamingMusicTrackTests()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
        AudioSystem.Shutdown();
        StreamingMusicRegistry.Instance.Unregister();

        _originalFactory = StreamingMusicTrack.DefaultVoiceFactory;
        StreamingMusicTrack.DefaultVoiceFactory = CreateFakeVoice;
    }

    /// <summary>Leaves nothing behind: the manager, the mixer and the output are process-wide.</summary>
    public void Dispose()
    {
        _manager.Stop();
        _manager.ClearDucks();
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
        StreamingMusicRegistry.Instance.Unregister();
        StreamingMusicTrack.DefaultVoiceFactory = _originalFactory;
        AudioSystem.Shutdown();
    }

    private FakeStreamingMusicVoice Voice => _voices[^1];

    // ----- construction and format -----

    [Fact]
    public void Constructor_rejects_a_null_provider()
    {
        //Arrange
        Action act = () => _ = new StreamingMusicTrack(null!);

        //Act & Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_names_the_track_after_its_provider() =>
        new StreamingMusicTrack(new FakeStreamingMusicProvider("Generated music")).Key.Should().Be("Generated music");

    [Fact]
    public void StartCore_starts_the_provider_at_the_pinned_device_format()
    {
        //Arrange
        AudioSystem.Initialize(32000, 1);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);

        //Act
        _manager.Play(track);

        //Assert
        provider.StartCalls.Should().Be(1);
        provider.LastSampleRate.Should().Be(32000);
        provider.LastChannels.Should().Be(1);
        track.SampleRate.Should().Be(32000);
        track.Channels.Should().Be(1);
        Voice.Stream.WaveFormat.SampleRate.Should().Be(32000);
        Voice.Stream.WaveFormat.Channels.Should().Be(1);
        Voice.Stream.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
    }

    [Fact]
    public void ResolveOutputFormat_uses_the_shared_outputs_configured_rate_when_nothing_is_pinned()
    {
        //Arrange
        SharedAudioOutput.Configure(22050, 2);

        //Act
        var (rate, channels) = StreamingMusicTrack.ResolveOutputFormat(out var unclaimed);

        //Assert
        rate.Should().Be(22050);
        channels.Should().Be(2);
        unclaimed.Should().BeFalse();
    }

    [Fact]
    public void ResolveOutputFormat_falls_back_to_the_unclaimed_rate_only_when_nothing_has_claimed_the_output()
    {
        //Arrange & Act
        var (rate, channels) = StreamingMusicTrack.ResolveOutputFormat(out var unclaimed);

        //Assert
        rate.Should().Be(StreamingMusicTrack.UnclaimedOutputSampleRate);
        channels.Should().Be(2);
        unclaimed.Should().BeTrue();
    }

    // ----- the fill path -----

    [Fact]
    public void A_late_starting_provider_plays_silence_and_then_its_audio()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { SilentRendersBeforeAudio = 3 };
        using var track = NewTrack(provider);
        var ended = CountEnded(track);

        //Act
        _manager.Play(track);
        var stateBefore = track.State;
        var silence = Enumerable.Range(0, 3).SelectMany(_ => Voice.Pump(256)).ToArray();
        var stateWhileSilent = track.State;
        var audio = Voice.Pump(256);

        //Assert
        stateBefore.Should().Be(StreamingMusicState.Starting);
        stateWhileSilent.Should().Be(StreamingMusicState.Starting);
        silence.Should().OnlyContain(sample => sample == 0f);
        AssertTone(provider, audio, firstFrame: 0, frames: 256, channels: 2);
        track.State.Should().Be(StreamingMusicState.Playing);
        ended.Count.Should().Be(0, "a slow start is an ordinary state, not an end");
    }

    [Fact]
    public void Fill_interleaves_the_left_and_right_planes()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);

        //Act
        var buffer = Voice.Pump(64);

        //Assert
        AssertTone(provider, buffer, firstFrame: 0, frames: 64, channels: 2);
    }

    [Fact]
    public void Fill_mixes_the_planes_down_for_a_mono_output()
    {
        //Arrange - right is the negation of left, so an honest down-mix is exactly silence, while
        //taking either plane alone would not be.
        AudioSystem.Initialize(48000, 1);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);

        //Act
        var buffer = Voice.Pump(64);

        //Assert
        buffer.Length.Should().Be(64);
        buffer.Should().OnlyContain(sample => sample == 0f);
        provider.FramesRendered.Should().Be(64);
    }

    [Fact]
    public void A_short_render_is_padded_with_silence_and_the_stream_carries_on()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { MaxFramesPerRender = 10 };
        using var track = NewTrack(provider);
        _manager.Play(track);

        //Act
        var first = Voice.Pump(64);
        var second = Voice.Pump(64);

        //Assert
        AssertTone(provider, first, firstFrame: 0, frames: 10, channels: 2);
        first.Skip(20).Should().OnlyContain(sample => sample == 0f);
        AssertTone(provider, second, firstFrame: 10, frames: 10, channels: 2);
        second.Skip(20).Should().OnlyContain(sample => sample == 0f);
        track.State.Should().Be(StreamingMusicState.Playing);
    }

    [Fact]
    public void A_request_larger_than_the_planes_is_rendered_in_chunks()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        var frames = (StreamingMusicTrack.PlaneFrames * 2) + 5;

        //Act
        var buffer = Voice.Pump(frames);

        //Assert
        AssertTone(provider, buffer, firstFrame: 0, frames: frames, channels: 2);
        provider.RenderCalls.Should().Be(3);
    }

    [Fact]
    public void A_stall_is_starved_silence_and_never_an_end()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { StallAfterFrames = 100 };
        using var track = NewTrack(provider);
        var ended = CountEnded(track);
        _manager.Play(track);

        //Act
        var beforeStall = Voice.Pump(256);
        var starvedState = track.State;
        var duringStall = Voice.Pump(256);
        provider.EndStall();
        var afterStall = Voice.Pump(64);

        //Assert
        AssertTone(provider, beforeStall, firstFrame: 0, frames: 100, channels: 2);
        beforeStall.Skip(200).Should().OnlyContain(sample => sample == 0f);
        starvedState.Should().Be(StreamingMusicState.Starved);
        duringStall.Should().OnlyContain(sample => sample == 0f);
        AssertTone(provider, afterStall, firstFrame: 100, frames: 64, channels: 2);
        track.State.Should().Be(StreamingMusicState.Playing);
        track.IsPlaying.Should().BeTrue("the track never stops because the music went quiet");
        ended.Count.Should().Be(0);
    }

    // ----- the clock and the shape of a stream -----

    [Fact]
    public void Position_counts_every_frame_pulled_including_silence_and_restarts_with_the_track()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { SilentRendersBeforeAudio = 2 };
        using var track = NewTrack(provider);
        _manager.Play(track);

        //Act
        Voice.Pump(4800);
        Voice.Pump(4800);
        Voice.Pump(4800);
        var position = track.Position;
        _manager.Play(track);

        //Assert
        position.TotalSeconds.Should().BeApproximately(0.3, 0.000001);
        track.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_stream_has_no_duration_always_loops_and_cannot_be_seeked()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        Voice.Pump(480);

        //Act
        track.IsLooping = false;
        track.Seek(TimeSpan.FromMinutes(1));

        //Assert
        track.Duration.Should().Be(TimeSpan.Zero);
        track.IsLooping.Should().BeTrue();
        track.Position.TotalSeconds.Should().BeApproximately(0.01, 0.000001);
        track.Timeline.Should().BeNull();
    }

    // ----- ending -----

    [Fact]
    public void A_provider_that_faults_raises_Ended_once_and_the_track_plays_silence()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        var ended = CountEnded(track);
        _manager.Play(track);
        Voice.Pump(64);

        //Act
        provider.FaultOnNextRender = true;
        var faulted = Voice.Pump(64);
        var after = Voice.Pump(64);

        //Assert
        faulted.Should().OnlyContain(sample => sample == 0f);
        after.Should().OnlyContain(sample => sample == 0f);
        ended.Count.Should().Be(1);
        track.State.Should().Be(StreamingMusicState.Faulted);
        track.Fault.Should().BeSameAs(provider.Fault);
        track.IsPlaying.Should().BeTrue("the track keeps its voice until the game stops it");
    }

    [Fact]
    public void A_render_that_throws_never_escapes_the_fill_path()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { ThrowOnRender = true };
        using var track = NewTrack(provider);
        var ended = CountEnded(track);
        _manager.Play(track);
        float[] buffer = Array.Empty<float>();

        //Act
        Action pump = () => buffer = Voice.Pump(64);

        //Assert
        pump.Should().NotThrow();
        buffer.Should().OnlyContain(sample => sample == 0f);
        track.State.Should().Be(StreamingMusicState.Faulted);
        track.Fault.Should().BeOfType<InvalidOperationException>();
        ended.Count.Should().Be(1);
        _log.Lines.Count(line => line.Level == LogLevel.Warning).Should().Be(1);
    }

    [Fact]
    public void An_Ended_handler_that_throws_never_escapes_the_fill_path()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        track.Ended += (_, _) => throw new InvalidOperationException("a careless handler");
        _manager.Play(track);
        provider.FaultOnNextRender = true;

        //Act
        Action pump = () => Voice.Pump(64);

        //Assert
        pump.Should().NotThrow();
        track.State.Should().Be(StreamingMusicState.Faulted);
    }

    [Fact]
    public void An_Ended_handler_may_stop_the_music_from_inside_the_fill_path()
    {
        //Arrange - Ended is raised after the fill path has let go of its locks, so a handler that
        //reacts by stopping (or changing) the music cannot deadlock or re-enter a half-done render.
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        track.Ended += (_, _) => _manager.Stop();
        _manager.Play(track);
        provider.FaultOnNextRender = true;
        var voice = Voice;

        //Act
        voice.Pump(64);

        //Assert
        _manager.NowPlaying.Should().BeNull();
        track.IsPlaying.Should().BeFalse();
        voice.Disposed.Should().BeTrue();
    }

    [Fact]
    public void A_provider_stopped_by_someone_else_raises_Ended()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        var ended = CountEnded(track);
        _manager.Play(track);
        Voice.Pump(64);

        //Act
        provider.Stop();
        var after = Voice.Pump(64);

        //Assert
        ended.Count.Should().Be(1);
        track.State.Should().Be(StreamingMusicState.Stopped);
        after.Should().OnlyContain(sample => sample == 0f);
        provider.RenderedWhileStopped.Should().BeFalse("the track stops pulling a provider that has ended");
    }

    [Fact]
    public void Stopping_through_the_manager_stops_the_provider_without_raising_Ended()
    {
        //Arrange - as for every music track: a playlist would otherwise advance on its own stop.
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        var ended = CountEnded(track);
        _manager.Play(track);
        var voice = Voice;

        //Act
        _manager.Stop();

        //Assert
        ended.Count.Should().Be(0);
        provider.StopCalls.Should().Be(1);
        provider.State.Should().Be(StreamingMusicState.Stopped);
        track.State.Should().Be(StreamingMusicState.Stopped);
        track.IsPlaying.Should().BeFalse();
        voice.Disposed.Should().BeTrue("a stopped track holds no audio resources");
    }

    [Fact]
    public void Dispose_stops_the_provider_but_never_disposes_it()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        var track = NewTrack(provider);
        _manager.Play(track);

        //Act
        track.Dispose();

        //Assert
        provider.StopCalls.Should().Be(1);
        provider.Disposed.Should().BeFalse();
        track.State.Should().Be(StreamingMusicState.Stopped);
    }

    // ----- the manager's controls -----

    [Fact]
    public void A_fade_in_reaches_the_voice()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);

        //Act
        _manager.Play(track, TimeSpan.FromSeconds(2));
        var atStart = Voice.Volume;
        _manager.Ticker.Tick(1.0);
        var halfway = Voice.Volume;
        _manager.Ticker.Tick(1.0);

        //Assert
        atStart.Should().Be(0f);
        halfway.Should().BeApproximately(0.5f, 0.01f);
        Voice.Volume.Should().Be(1f);
    }

    [Fact]
    public void A_fade_out_keeps_the_stream_playing_until_it_is_silent()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        var voice = Voice;

        //Act
        _manager.Stop(TimeSpan.FromSeconds(1));
        _manager.Ticker.Tick(0.5);
        var midwayStops = provider.StopCalls;
        var midwayAudio = voice.Pump(64);
        _manager.Ticker.Tick(0.5);

        //Assert
        midwayStops.Should().Be(0);
        AssertTone(provider, midwayAudio, firstFrame: 0, frames: 64, channels: 2);
        voice.Volume.Should().Be(0f);
        provider.StopCalls.Should().Be(1);
        voice.Disposed.Should().BeTrue();
    }

    [Fact]
    public void The_voice_is_on_the_music_bus_so_the_slider_and_ducking_apply()
    {
        //Arrange - the real voice, over a real StreamingAudioSource, with only the device start left out.
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        DevicelessSourceVoice? voice = null;
        using var track = new StreamingMusicTrack("stream", provider, stream => voice = new DevicelessSourceVoice(stream));
        AudioMixer.MusicVolume = 0.5f;

        //Act
        _manager.Play(track);
        track.Volume = 0.8f;
        using var duck = _manager.PushDuck(0.25f);

        //Assert
        voice.Should().NotBeNull();
        var source = voice!.Inner.Source;
        source.Bus.Should().Be(AudioBus.Music);
        source.Volume.Should().Be(0.8f);
        source.AppliedGain.Should().BeApproximately(0.8f * 0.5f * 0.25f, 0.0001f);
    }

    [Fact]
    public void The_voice_suspends_with_the_global_engine_pause_as_endless_material()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        DevicelessSourceVoice? voice = null;
        using var track = new StreamingMusicTrack("stream", provider, stream => voice = new DevicelessSourceVoice(stream));

        //Act
        _manager.Play(track);
        var pausable = (IEnginePausableAudio)voice!.Inner.Source;

        //Assert
        pausable.SuspendOnEnginePause.Should().Be(true);
        pausable.KnownDurationForEnginePause.Should().BeNull();
    }

    [Fact]
    public void SuspendOnEnginePause_passes_an_override_through_to_the_voice()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        var defaulted = Voice.SuspendOnEnginePause;

        //Act
        track.SuspendOnEnginePause = false;

        //Assert
        defaulted.Should().Be(true);
        Voice.SuspendOnEnginePause.Should().Be(false);
    }

    [Fact]
    public void Pausing_holds_the_stream_and_its_clock_where_they_are()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        Voice.Pump(480);

        //Act
        _manager.Pause();
        var whilePaused = Voice.Pump(480);
        var pausedPosition = track.Position;
        var pausedPlaying = track.IsPlaying;
        _manager.Resume();
        var resumed = Voice.Pump(64);

        //Assert
        whilePaused.Should().BeEmpty("a paused voice is not pulled");
        pausedPlaying.Should().BeFalse();
        pausedPosition.TotalSeconds.Should().BeApproximately(0.01, 0.000001);
        provider.StopCalls.Should().Be(0, "a pause is not a stop");
        AssertTone(provider, resumed, firstFrame: 480, frames: 64, channels: 2);
        track.IsPlaying.Should().BeTrue();
    }

    // ----- one stream per provider -----

    [Fact]
    public void A_second_track_over_the_same_provider_takes_the_stream_over()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var first = NewTrack(provider);
        using var second = NewTrack(provider);
        var firstEnded = CountEnded(first);
        _manager.Play(first);
        var firstVoice = Voice;
        firstVoice.Pump(480);

        //Act
        _manager.CrossfadeTo(second, TimeSpan.FromSeconds(2));
        var secondVoice = Voice;
        var outgoing = firstVoice.Pump(64);
        var incoming = secondVoice.Pump(64);
        _manager.Ticker.Tick(2.0);

        //Assert
        provider.StartCalls.Should().Be(2);
        provider.StopCalls.Should().Be(1, "the take-over restarts the provider once; the outgoing track's stop leaves it alone");
        outgoing.Should().OnlyContain(sample => sample == 0f);
        AssertTone(provider, incoming, firstFrame: 0, frames: 64, channels: 2);
        first.State.Should().Be(StreamingMusicState.Stopped);
        second.State.Should().Be(StreamingMusicState.Playing);
        firstEnded.Count.Should().Be(0);
        firstVoice.Disposed.Should().BeTrue();
        provider.ConcurrentRenderSeen.Should().BeFalse();
    }

    [Fact]
    public async Task Render_never_overlaps_itself_and_never_follows_the_stop()
    {
        //Arrange - two threads pulling at once (a crossfade's two voices) and a stop landing mid-pull.
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        _manager.Play(track);
        var stream = Voice.Stream;
        using var stopPulling = new CancellationTokenSource();

        void Pull()
        {
            var buffer = new float[512];
            while (!stopPulling.IsCancellationRequested)
            {
                stream.Read(buffer);
            }
        }

        var pullers = new[] { Task.Run(Pull, TestContext.Current.CancellationToken), Task.Run(Pull, TestContext.Current.CancellationToken) };
        SpinWait.SpinUntil(() => provider.RenderCalls > 200, TimeSpan.FromSeconds(5));

        //Act
        _manager.Stop();
        var callsAtStop = provider.RenderCalls;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var callsLater = provider.RenderCalls;
        stopPulling.Cancel();
        await Task.WhenAll(pullers);

        //Assert
        callsAtStop.Should().BeGreaterThan(200);
        callsLater.Should().Be(callsAtStop);
        provider.ConcurrentRenderSeen.Should().BeFalse();
        provider.RenderedWhileStopped.Should().BeFalse();
    }

    // ----- logging -----

    [Fact]
    public void Each_state_change_is_logged_once_at_Information()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var provider = new FakeStreamingMusicProvider { SilentRendersBeforeAudio = 1, Description = "sector one, andante" };
        using var track = NewTrack(provider);

        //Act
        _manager.Play(track);
        Voice.Pump(64);
        Voice.Pump(64);
        Voice.Pump(64);

        //Assert
        var lines = _log.Lines;
        lines.Should().OnlyContain(line => line.Level == LogLevel.Information);
        lines.Count(line => line.Message.Contains("starting 'Fake music' at 48000 Hz")).Should().Be(1);
        lines.Count(line => line.Message.EndsWith(": Starting")).Should().Be(1);
        lines.Count(line => line.Message.Contains(": Playing - sector one, andante")).Should().Be(1);
    }

    [Fact]
    public void Starved_flapping_is_logged_at_most_once_per_interval()
    {
        //Arrange
        AudioSystem.Initialize(48000, 2);
        var now = TimeSpan.FromSeconds(100);
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);
        track.ClockOverride = () => now;
        _manager.Play(track);

        //Act
        for (var i = 0; i < 10; i++)
        {
            provider.SetState(StreamingMusicState.Starved);
            provider.SetState(StreamingMusicState.Playing);
        }

        now += StreamingMusicTrack.StarvedLogInterval + TimeSpan.FromSeconds(1);
        provider.SetState(StreamingMusicState.Starved);
        provider.SetState(StreamingMusicState.Playing);

        //Assert
        var lines = _log.Lines;
        lines.Count(line => line.Message.EndsWith(": Starved")).Should().Be(1);
        lines.Count(line => line.Message.Contains(": Playing") && line.Message.Contains("not logged")).Should().Be(1);
    }

    [Fact]
    public void Starting_without_a_pinned_format_says_so_in_the_log()
    {
        //Arrange
        var provider = new FakeStreamingMusicProvider();
        using var track = NewTrack(provider);

        //Act
        _manager.Play(track);

        //Assert
        provider.LastSampleRate.Should().Be(StreamingMusicTrack.UnclaimedOutputSampleRate);
        _log.Lines.Count(line => line.Message.Contains("AudioSystem.Initialize")).Should().Be(1);
    }

    // ----- helpers -----

    private StreamingMusicTrack NewTrack(FakeStreamingMusicProvider provider)
        => new("stream", provider, CreateFakeVoice) { LoggerOverride = _log };

    private IStreamingMusicVoice CreateFakeVoice(ISampleProvider stream)
    {
        var voice = new FakeStreamingMusicVoice(stream);
        _voices.Add(voice);
        return voice;
    }

    private static EndedCounter CountEnded(MusicTrack track)
    {
        var counter = new EndedCounter();
        track.Ended += (_, _) => Interlocked.Increment(ref counter.Value);
        return counter;
    }

    private static void AssertTone(FakeStreamingMusicProvider provider, float[] buffer, long firstFrame, int frames, int channels)
    {
        buffer.Length.Should().BeGreaterThanOrEqualTo(frames * channels);

        for (var i = 0; i < frames; i++)
        {
            var expected = provider.ExpectedLeft(firstFrame + i);
            buffer[i * channels].Should().Be(expected);
            buffer[(i * channels) + 1].Should().Be(-expected);
        }
    }

    private sealed class EndedCounter
    {
        internal int Value;

        internal int Count => Volatile.Read(ref Value);
    }

    // The real voice with its device start left out: everything the track configures on it is real.
    private sealed class DevicelessSourceVoice : IStreamingMusicVoice
    {
        internal DevicelessSourceVoice(ISampleProvider stream) => Inner = new StreamingAudioSourceVoice(stream);

        internal StreamingAudioSourceVoice Inner { get; }

        public float Volume
        {
            get => Inner.Volume;
            set => Inner.Volume = value;
        }

        public bool? SuspendOnEnginePause
        {
            get => Inner.SuspendOnEnginePause;
            set => Inner.SuspendOnEnginePause = value;
        }

        public bool IsPlaying { get; private set; }

        public void Start() => IsPlaying = true;

        public void Stop() => IsPlaying = false;

        public void Dispose() => Inner.Dispose();
    }
}
