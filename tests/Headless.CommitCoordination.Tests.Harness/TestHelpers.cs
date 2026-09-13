// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.CommitCoordination;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>An <see cref="IServiceProvider" /> that resolves nothing — for coordinator paths that never touch DI.</summary>
public sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return null;
    }
}

/// <summary>
/// A relational handle with no live connection or transaction — enough to prove a scope carries the handle it
/// was opened with without standing up a database.
/// </summary>
public sealed class StubRelationalCommitContext : IRelationalCommitContext
{
    public DbConnection? Connection => null;

    public DbTransaction? Transaction => null;
}

/// <summary>One captured log record.</summary>
public sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

/// <summary>
/// An <see cref="ILogger{TCategoryName}" /> that records every entry, so a scenario can assert what the
/// coordinator logged (an ignored conflicting signal) and what it did not (a repeated same-outcome signal).
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

/// <summary>
/// A single-threaded <see cref="SynchronizationContext" /> for deadlock-probing sync-over-async paths: it pumps
/// posted continuations on one dedicated thread, so a body that blocks waiting on a continuation it posted here
/// deadlocks deterministically (surfaced as <see cref="Run" /> returning <see langword="false" /> on timeout).
/// </summary>
public sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Add((d, state));
    }

    public static bool Run(Action action, TimeSpan timeout)
    {
        using var completed = new ManualResetEventSlim(false);
        var context = new SingleThreadSynchronizationContext();

        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);

            try
            {
                action();
            }
            finally
            {
                completed.Set();
                context._queue.CompleteAdding();
            }

            foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();

        return completed.Wait(timeout);
    }
}
