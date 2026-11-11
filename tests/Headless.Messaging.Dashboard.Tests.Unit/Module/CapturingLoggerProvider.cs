// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests.Module;

/// <summary>Captures every log line a host writes, formatted with its exception, so a test can search them.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lines.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
