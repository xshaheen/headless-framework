// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>One captured log record with its category, for assertions on DI-resolved loggers.</summary>
public sealed record CategoryLogEntry(string Category, LogLevel Level, EventId EventId, string Message);

/// <summary>
/// An <see cref="ILoggerProvider" /> that records every entry from every category, so an integration scenario
/// can assert what the DI-resolved manager logged (the forgotten-completion warning, a runner fault) and what
/// it did not.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CategoryLogEntry> _entries = new();

    public IReadOnlyCollection<CategoryLogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName)
    {
        return new CategoryLogger(categoryName, _entries);
    }

    public void Dispose() { }

    private sealed class CategoryLogger(string category, ConcurrentQueue<CategoryLogEntry> entries) : ILogger
    {
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
            entries.Enqueue(new CategoryLogEntry(category, logLevel, eventId, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
