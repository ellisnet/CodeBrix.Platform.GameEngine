using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Tests for the registry of live game hosts that <see cref="GameWindowLifecycle"/> notifies.
/// </summary>
public class WindowLifecycleParticipantsTests
{
    [Fact]
    public void a_host_is_listed_once_until_it_is_removed()
    {
        //Arrange
        var participant = new Participant();

        try
        {
            //Act
            WindowLifecycleParticipants.Add(participant);
            WindowLifecycleParticipants.Add(participant);
            var listed = WindowLifecycleParticipants.Snapshot();
            WindowLifecycleParticipants.Remove(participant);

            //Assert
            listed.Should().ContainSingle(item => ReferenceEquals(item, participant));
            WindowLifecycleParticipants.Snapshot().Should().NotContain(participant);
        }
        finally
        {
            WindowLifecycleParticipants.Remove(participant);
        }
    }

    [Fact]
    public void removing_a_host_that_was_never_added_does_nothing()
    {
        //Act
        WindowLifecycleParticipants.Remove(new Participant());

        //Assert
        WindowLifecycleParticipants.Snapshot().Should().NotBeNull();
    }

    private sealed class Participant : IWindowLifecycleParticipant
    {
        public GameSurfaceCanvas? LifecycleSurface => null;

        public void NotifyWindowHidden()
        {
        }

        public void NotifyWindowShown()
        {
        }

        public void NotifyWindowActivated(bool refocusSurface)
        {
        }

        public void NotifyWindowDeactivated()
        {
        }
    }
}
