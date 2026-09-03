using System;
using CodeBrix.Platform.GameEngine.Extensibility;
using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Regression tests for <see cref="ServiceCollectionExtensions.AddEngineLogging"/>, which used to
/// register an <see cref="ILoggerFactory"/> whose factory resolved <see cref="ILoggerFactory"/> -
/// a circular dependency that threw as soon as anything resolved it.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEngineLogging_uses_the_registered_factory_without_circular_resolution()
    {
        //Arrange
        using var state = new EngineLoggerStateScope();
        state.UseEngineOwnedFactory();
        var services = new ServiceCollection();

        //Act
        services.AddEngineLogging();
        using var provider = services.BuildServiceProvider();
        var resolvedFactory = provider.GetRequiredService<ILoggerFactory>();

        //Assert
        resolvedFactory.Should().BeSameAs(EngineLogger.EngineLoggerFactory);
    }

    [Fact]
    public void AddEngineLogging_adopts_a_factory_instance_that_was_registered_directly()
    {
        //Arrange
        using var state = new EngineLoggerStateScope();
        var externalFactory = new TestLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(externalFactory);

        //Act
        services.AddEngineLogging();
        using var provider = services.BuildServiceProvider();
        var resolvedFactory = provider.GetRequiredService<ILoggerFactory>();

        //Assert
        resolvedFactory.Should().BeSameAs(externalFactory);
        EngineLogger.EngineLoggerFactory.Should().BeSameAs(externalFactory);
    }

    [Fact]
    public void AddEngineLogging_throws_when_the_service_collection_is_null()
    {
        //Arrange
        IServiceCollection services = null!;

        //Act
        Action act = () => services.AddEngineLogging();

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
