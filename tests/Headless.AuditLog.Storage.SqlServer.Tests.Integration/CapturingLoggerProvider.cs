// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>Records every warning written through the host's logging, for assertions on deduplicated warnings.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _warnings = new();

    public IReadOnlyCollection<CapturedLogEntry> Warnings => _warnings;

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(_warnings);
    }

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLogEntry> warnings) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel == LogLevel.Warning;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                warnings.Enqueue(new CapturedLogEntry(eventId.Name, formatter(state, exception)));
            }
        }
    }
}

internal sealed record CapturedLogEntry(string? EventName, string Message);
