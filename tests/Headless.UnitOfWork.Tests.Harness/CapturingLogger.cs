// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>One captured log record.</summary>
public sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

/// <summary>
/// An <see cref="ILogger{TCategoryName}" /> that records every entry, so a scenario can assert what the
/// manager logged (a leak warning, an ignored conflicting signal, a failure-callback fault) and what it did
/// not.
/// </summary>
public sealed class CapturingLogger<TCategory> : ILogger<TCategory>
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
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
        _entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
