using System;
using System.Collections;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Saves and restores the <see cref="EngineLogger"/> static state (<c>_loggerFactory</c>,
/// <c>_usingExternalLoggerFactory</c> and the logger cache) around a test, so a test that swaps in
/// its own logger factory does not leak that factory into the rest of the assembly.
/// </summary>
internal sealed class EngineLoggerStateScope : IDisposable
{
    private static readonly FieldInfo LoggerFactoryField =
        typeof(EngineLogger).GetField("_loggerFactory", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo ExternalFactoryField =
        typeof(EngineLogger).GetField("_usingExternalLoggerFactory", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly FieldInfo LoggerCacheField =
        typeof(EngineLogger).GetField("_loggerCache", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly object? _originalFactory = LoggerFactoryField.GetValue(null);

    private readonly bool _originalExternalFactory = (bool)ExternalFactoryField.GetValue(null)!;

    /// <summary>
    /// Clears the captured factory state so the engine falls back to building its own factory,
    /// making a test independent of whatever ran before it.
    /// </summary>
    public void UseEngineOwnedFactory()
    {
        LoggerFactoryField.SetValue(null, null);
        ExternalFactoryField.SetValue(null, false);
        ClearLoggerCache();
    }

    /// <summary>Restores the logger factory state captured when this scope was created.</summary>
    public void Dispose()
    {
        LoggerFactoryField.SetValue(null, _originalFactory);
        ExternalFactoryField.SetValue(null, _originalExternalFactory);
        ClearLoggerCache();
    }

    private static void ClearLoggerCache()
    {
        var cache = LoggerCacheField.GetValue(null)!;
        cache.GetType().GetMethod(nameof(IDictionary.Clear))!.Invoke(cache, null);
    }
}

/// <summary>
/// A minimal <see cref="ILoggerFactory"/> that stands in for an application-supplied factory.
/// </summary>
internal sealed class TestLoggerFactory : ILoggerFactory
{
    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => TestLogger.Instance;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// A no-op <see cref="ILogger"/> used by <see cref="TestLoggerFactory"/>.
/// </summary>
internal sealed class TestLogger : ILogger
{
    /// <summary>Gets the shared no-op logger instance.</summary>
    public static TestLogger Instance { get; } = new();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoOpScope.Instance;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => false;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
