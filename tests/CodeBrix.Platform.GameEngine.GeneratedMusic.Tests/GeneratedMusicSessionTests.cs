using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Covers the test seams over generated music: <see cref="IGeneratedMusicSession"/> as the provider
/// implements it, <see cref="GeneratedMusicSourceInfo"/>, and <see cref="EngineGeneratedMusicStarter"/>
/// as the real <see cref="IGeneratedMusicStarter"/>. Also shows the seams standing in for the engine
/// with a scripted fake, the way a game's own music tests use them.
/// </summary>
public class GeneratedMusicSessionTests : GeneratedMusicTestContext
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_new_provider_reports_nothing_through_the_session()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions { Preset = "ClubArrangement" });

        //Act
        IGeneratedMusicSession session = provider;

        //Assert
        session.State.Should().Be(StreamingMusicState.Stopped);
        session.Fault.Should().BeNull();
        session.GenerationError.Should().BeNull();
        session.ActiveSourceSummary.Should().BeEmpty();
        session.ActiveSourceInfo.Should().BeNull();
        session.StarvationGapCount.Should().Be(0);
        session.DiagnosticsSummary.Should().BeEmpty();
        session.Options.Preset.Should().Be("ClubArrangement");
    }

    [Fact]
    public void Once_the_music_plays_the_session_reports_the_source_and_diagnostics_as_plain_values()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        IGeneratedMusicSession session = provider;
        provider.Start(SampleRate, 2);

        //Act
        PullUntilAudible(provider, Generous);

        //Assert
        var info = session.ActiveSourceInfo;
        info.Should().NotBeNull();
        info!.IsReplay.Should().BeTrue();
        info.GeneratorName.Should().Be(provider.ActiveSource!.GeneratorName);
        info.GeneratorFamily.Should().Be(provider.ActiveSource.GeneratorFamily);
        info.InstrumentLibraryName.Should().Be("ModestSynthGm");
        session.ActiveSourceSummary.Should().Be(provider.ActiveSourceSummary);
        session.DiagnosticsSummary.Should().Be(provider.Diagnostics!.ToString());
        session.StarvationGapCount.Should().Be(provider.Diagnostics.StarvationGapCount);
    }

    [Fact]
    public void StateChanged_through_the_session_is_the_providers_event()
    {
        //Arrange
        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions());
        IGeneratedMusicSession session = provider;
        var changes = 0;
        void OnChanged(object? sender, EventArgs args) => Interlocked.Increment(ref changes);
        session.StateChanged += OnChanged;

        //Act
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, Generous);
        session.StateChanged -= OnChanged;
        var seen = Volatile.Read(ref changes);
        provider.Stop();

        //Assert
        seen.Should().BeGreaterThan(0);
        Volatile.Read(ref changes).Should().Be(seen, "an unsubscribed handler hears nothing more");
    }

    [Fact]
    public void EngineGeneratedMusicStarter_starts_through_UseGeneratedMusic_and_returns_the_provider()
    {
        //Arrange
        IGeneratedMusicStarter starter = new EngineGeneratedMusicStarter();

        //Act
        var session = starter.Start(new GeneratedMusicOptions { TrackKey = "starter-theme" });

        //Assert
        var provider = session.Should().BeOfType<GeneratedMusicProvider>().Subject;
        Engine.Instance.Managers.StreamingMusic.Provider.Should().BeSameAs(provider);
        MusicManager.Instance.NowPlaying!.Key.Should().Be("starter-theme");
    }

    [Fact]
    public void EngineGeneratedMusicStarter_for_one_engine_replaces_the_session_it_started_before()
    {
        //Arrange
        var starter = new EngineGeneratedMusicStarter(Engine.Instance);
        var first = starter.Start(new GeneratedMusicOptions { StartImmediately = false });

        //Act
        var second = starter.Start(new GeneratedMusicOptions { StartImmediately = false });

        //Assert
        second.Should().NotBeSameAs(first);
        Engine.Instance.Managers.StreamingMusic.Provider.Should().BeSameAs(second);
    }

    [Fact]
    public void EngineGeneratedMusicStarter_rejects_a_null_engine() =>
        ((Action)(() => _ = new EngineGeneratedMusicStarter(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void EngineGeneratedMusicStarter_rejects_null_options() =>
        ((Action)(() => new EngineGeneratedMusicStarter().Start(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void GeneratedMusicSourceInfo_holds_its_values_and_prints_one_line()
    {
        //Act
        var model = new GeneratedMusicSourceInfo("SkyTNT", "SkyTNT", "FluidR3Gm", false);
        var replay = new GeneratedMusicSourceInfo("Replay", "Replay", "ModestSynthGm", true);

        //Assert
        model.GeneratorName.Should().Be("SkyTNT");
        model.GeneratorFamily.Should().Be("SkyTNT");
        model.InstrumentLibraryName.Should().Be("FluidR3Gm");
        model.IsReplay.Should().BeFalse();
        model.ToString().Should().Be("SkyTNT (SkyTNT) through FluidR3Gm");
        replay.ToString().Should().Be("Replay (Replay) through ModestSynthGm, replay");
    }

    [Fact]
    public void GeneratedMusicSourceInfo_rejects_null_names()
    {
        //Arrange
        Action generator = () => _ = new GeneratedMusicSourceInfo(null!, "f", "l", false);
        Action family = () => _ = new GeneratedMusicSourceInfo("g", null!, "l", false);
        Action library = () => _ = new GeneratedMusicSourceInfo("g", "f", null!, false);

        //Assert
        generator.Should().Throw<ArgumentNullException>();
        family.Should().Throw<ArgumentNullException>();
        library.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_music_policy_can_be_tested_against_fakes_of_the_seams()
    {
        //Arrange - a policy the size of a real one's core: start a session, log what plays, move it on.
        var starter = new ScriptedStarter();
        var policy = new ExamplePolicy(starter);

        //Act
        policy.Begin();
        starter.Session.RaiseState(StreamingMusicState.Playing);
        policy.EnterLevel(2);

        //Assert
        starter.Started.Should().ContainSingle().Which.Preset.Should().Be("calm");
        starter.Session.FollowUps.Should().ContainSingle().Which.Should().Be("battle");
        policy.Log.Should().Contain("playing: Replay (Replay) through ModestSynthGm, replay");
    }

    /// <summary>A tiny music policy written only against the seams, as a game's would be.</summary>
    private sealed class ExamplePolicy(IGeneratedMusicStarter starter)
    {
        private IGeneratedMusicSession? _session;

        public List<string> Log { get; } = [];

        public void Begin()
        {
            var session = starter.Start(new GeneratedMusicOptions { Preset = "calm" });
            _session = session;
            session.StateChanged += (_, _) =>
            {
                if (session.State == StreamingMusicState.Playing)
                {
                    Log.Add($"playing: {session.ActiveSourceInfo}");
                }
            };
        }

        public void EnterLevel(int level) => _session?.FollowUp(level > 1 ? "battle" : "calm");
    }

    private sealed class ScriptedStarter : IGeneratedMusicStarter
    {
        public ScriptedSession Session { get; } = new();

        public List<GeneratedMusicOptions> Started { get; } = [];

        public IGeneratedMusicSession Start(GeneratedMusicOptions options)
        {
            Started.Add(options);
            return Session;
        }
    }

    private sealed class ScriptedSession : IGeneratedMusicSession
    {
        public event EventHandler? StateChanged;

        public StreamingMusicState State { get; private set; } = StreamingMusicState.Starting;

        public Exception? Fault => null;

        public Exception? GenerationError => null;

        public GeneratedMusicOptions Options { get; } = new();

        public string ActiveSourceSummary => ActiveSourceInfo?.ToString() ?? string.Empty;

        public GeneratedMusicSourceInfo? ActiveSourceInfo { get; private set; }

        public int StarvationGapCount => 0;

        public string DiagnosticsSummary => string.Empty;

        public List<string> FollowUps { get; } = [];

        public void FollowUp(string presetOrText) => FollowUps.Add(presetOrText);

        public void FollowUp(GeneratedMusicOptions options) => FollowUps.Add(options.Preset ?? string.Empty);

        public void RaiseState(StreamingMusicState state)
        {
            State = state;
            ActiveSourceInfo = new GeneratedMusicSourceInfo("Replay", "Replay", "ModestSynthGm", true);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
