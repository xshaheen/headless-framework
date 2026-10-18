// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>Records the level, event id, and formatted message of every log entry, so a test can assert on them.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, EventId EventId, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(_entries);
    }

    public void Dispose() { }

    private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var message = formatter(state, exception);

            // Document generation may log from any thread.
            lock (entries)
            {
                entries.Add((logLevel, eventId, message));
            }
        }
    }
}
