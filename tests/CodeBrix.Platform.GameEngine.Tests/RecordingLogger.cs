using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>An <see cref="ILogger"/> that keeps every line it is given, for tests that count log output.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly object _gate = new();
    private readonly List<(LogLevel Level, string Message)> _lines = new();

    /// <summary>A snapshot of the lines logged so far.</summary>
    internal IReadOnlyList<(LogLevel Level, string Message)> Lines
    {
        get { lock (_gate) { return _lines.ToArray(); } }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
