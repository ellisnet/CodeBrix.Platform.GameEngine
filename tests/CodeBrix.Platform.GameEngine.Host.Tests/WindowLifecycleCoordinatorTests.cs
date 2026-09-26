using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Headless tests for the decisions behind <see cref="GameWindowLifecycle"/>: the order of hooks and the
/// engine pause, leaving a game's own pause alone, the opt-outs, and one failing host not stopping the rest.
/// </summary>
public class WindowLifecycleCoordinatorTests
{
    private readonly List<string> _log = new();
    private bool _enginePaused;

    private WindowLifecycleCoordinator NewCoordinator(params IWindowLifecycleParticipant[] participants) =>
        new(
            () => _enginePaused,
            () => { _enginePaused = true; _log.Add("pause"); },
            () => { _enginePaused = false; _log.Add("resume"); },
            () => participants);

    [Fact]
    public void hiding_runs_the_hidden_hooks_before_pausing_the_engine()
    {
        //Arrange
        var coordinator = NewCoordinator(new FakeParticipant("a", _log), new FakeParticipant("b", _log));

        //Act
        coordinator.OnVisibilityChanged(false);

        //Assert
        _log.Should().Equal("a:hidden", "b:hidden", "pause");
        coordinator.IsHidden.Should().BeTrue();
        coordinator.PausedByLifecycle.Should().BeTrue();
    }

    [Fact]
    public void showing_resumes_the_engine_before_running_the_shown_hooks()
    {
        //Arrange
        var coordinator = NewCoordinator(new FakeParticipant("a", _log));
        coordinator.OnVisibilityChanged(false);
        _log.Clear();

        //Act
        coordinator.OnVisibilityChanged(true);

        //Assert
        _log.Should().Equal("resume", "a:shown");
        _enginePaused.Should().BeFalse();
        coordinator.PausedByLifecycle.Should().BeFalse();
    }

    [Fact]
    public void a_pause_the_game_made_itself_is_neither_repeated_nor_lifted()
    {
        //Arrange
        _enginePaused = true;
        var coordinator = NewCoordinator(new FakeParticipant("a", _log));

        //Act
        coordinator.OnVisibilityChanged(false);
        coordinator.OnVisibilityChanged(true);

        //Assert
        _log.Should().Equal("a:hidden", "a:shown");
        _enginePaused.Should().BeTrue();
    }

    [Fact]
    public void repeated_reports_of_the_same_visibility_are_ignored()
    {
        //Arrange
        var coordinator = NewCoordinator(new FakeParticipant("a", _log));

        //Act
        coordinator.OnVisibilityChanged(true);
        coordinator.OnVisibilityChanged(false);
        coordinator.OnVisibilityChanged(false);
        coordinator.OnVisibilityChanged(true);
        coordinator.OnVisibilityChanged(true);

        //Assert
        _log.Should().Equal("a:hidden", "pause", "resume", "a:shown");
    }

    [Fact]
    public void PauseWhenHidden_off_still_runs_the_hooks_but_never_pauses()
    {
        //Arrange
        var coordinator = NewCoordinator(new FakeParticipant("a", _log));
        coordinator.PauseWhenHidden = false;

        //Act
        coordinator.OnVisibilityChanged(false);
        coordinator.OnVisibilityChanged(true);

        //Assert
        _log.Should().Equal("a:hidden", "a:shown");
    }

    [Fact]
    public void activation_refocuses_by_default_and_deactivation_is_reported()
    {
        //Arrange
        var coordinator = NewCoordinator(new FakeParticipant("a", _log));

        //Act
        coordinator.OnActivationChanged(true);
        coordinator.OnActivationChanged(false);
        coordinator.RefocusOnActivate = false;
        coordinator.OnActivationChanged(true);

        //Assert
        _log.Should().Equal("a:activated+refocus", "a:deactivated", "a:activated");
    }

    [Fact]
    public void a_host_whose_hook_throws_does_not_stop_the_others_or_the_pause()
    {
        //Arrange
        var coordinator = NewCoordinator(
            new FakeParticipant("bad", _log) { Throws = true },
            new FakeParticipant("good", _log));

        //Act
        coordinator.OnVisibilityChanged(false);

        //Assert
        _log.Should().Equal("bad:hidden", "good:hidden", "pause");
    }

    [Fact]
    public void the_constructor_rejects_missing_dependencies()
    {
        //Act
        Action act = () => _ = new WindowLifecycleCoordinator(null!, () => { }, () => { }, () => []);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class FakeParticipant(string name, List<string> log) : IWindowLifecycleParticipant
    {
        public bool Throws { get; init; }

        public GameSurfaceCanvas? LifecycleSurface => null;

        public void NotifyWindowHidden() => Record("hidden");

        public void NotifyWindowShown() => Record("shown");

        public void NotifyWindowActivated(bool refocusSurface) => Record(refocusSurface ? "activated+refocus" : "activated");

        public void NotifyWindowDeactivated() => Record("deactivated");

        private void Record(string what)
        {
            log.Add($"{name}:{what}");
            if (Throws)
                throw new InvalidOperationException("hook failed");
        }
    }
}
