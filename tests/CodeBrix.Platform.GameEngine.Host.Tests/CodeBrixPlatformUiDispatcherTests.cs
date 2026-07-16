using System;
using CodeBrix.Platform.GameEngine.Host.Threading;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Headless-safe tests for <see cref="CodeBrixPlatformUiDispatcher"/>. Behavior that requires a
/// live CodeBrix.Platform UI head (constructing a real DispatcherQueue / GameSurfaceCanvas and
/// driving frames) is exercised interactively on a running head, not in this headless suite.
/// </summary>
public class CodeBrixPlatformUiDispatcherTests
{
    [Fact]
    public void Constructor_rejects_null_dispatcher_queue()
    {
        //Act
        var act = () => new CodeBrixPlatformUiDispatcher(null!);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact(Skip = "Requires a live CodeBrix.Platform head. DispatcherQueue.GetForCurrentThread() throws " +
                 "NotSupportedException ('Ref assembly') when the platform runtime head is absent, so this " +
                 "runs interactively on a real head (Win/Mac), not in the headless suite.")]
    public void ForCurrentThread_returns_null_off_a_ui_thread()
        => CodeBrixPlatformUiDispatcher.ForCurrentThread().Should().BeNull();
}
