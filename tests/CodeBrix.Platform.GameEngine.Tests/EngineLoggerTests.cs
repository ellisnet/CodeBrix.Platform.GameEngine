using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.Logging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Regression tests for <see cref="EngineLogger"/> ownership of the logger factory. Once an
/// application supplies its own factory, the engine must not replace it.
/// </summary>
public class EngineLoggerTests
{
    [Fact]
    public void SetLogLevel_does_not_replace_an_externally_provided_factory()
    {
        //Arrange
        using var state = new EngineLoggerStateScope();
        var externalFactory = new TestLoggerFactory();

        //Act
        EngineLogger.Initialize(externalFactory);
        EngineLogger.SetLogLevel(LogLevel.Trace);

        //Assert
        EngineLogger.EngineLoggerFactory.Should().BeSameAs(externalFactory);
    }

    [Fact]
    public void SetLogLevel_still_rebuilds_the_engine_owned_factory()
    {
        //Arrange
        using var state = new EngineLoggerStateScope();
        state.UseEngineOwnedFactory();
        var before = EngineLogger.EngineLoggerFactory;

        //Act
        EngineLogger.SetLogLevel(LogLevel.Warning);

        //Assert
        EngineLogger.EngineLoggerFactory.Should().NotBeSameAs(before);
    }
}
