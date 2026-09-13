// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.BackgroundServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transactions;

internal static class FakeClock
{
    /// <summary>
    /// Advances the fake clock in <paramref name="step" /> increments until <paramref name="until" /> completes. The
    /// deadline timers under test are armed just AFTER the hook that released the test, so a single advance can race
    /// the arming and leave the timer parked forever; repeating the advance makes the wait deterministic.
    /// </summary>
    public static async Task AdvanceUntilAsync(
        FakeTimeProvider timeProvider,
        TimeSpan step,
        Task until,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < 500 && !until.IsCompleted; attempt++)
        {
            timeProvider.Advance(step);
            _ = await Task.WhenAny(until, Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken));
        }

        await until.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }
}

/// <summary>A post-commit signal whose side effect is supplied by the test.</summary>
internal sealed record TestPostCommitSignal(string Scope, Func<DateTimeOffset, CancellationToken, Task> SideEffect)
    : JobsPostCommitSignal(Scope)
{
    public static TestPostCommitSignal NoOp(string jobScope = "noop")
    {
        return new(jobScope, static (_, _) => Task.CompletedTask);
    }

    public override Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return SideEffect(now, cancellationToken);
    }
}

/// <summary>
/// Records LoggerMessage-emitted entries so a test can assert on a warning without a logging mock, and lets a test
/// await the entry a background worker will write.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public async Task<(LogLevel Level, Exception? Exception)> WaitForAsync(
        Func<(LogLevel Level, Exception? Exception), bool> predicate,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            Task changed;

            lock (_gate)
            {
                foreach (var entry in _entries)
                {
                    if (predicate(entry))
                    {
                        return entry;
                    }
                }

                changed = _changed.Task;
            }

            // Bounded so a worker that never logs fails the test instead of hanging the run.
            await changed.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

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
        TaskCompletionSource changed;

        lock (_gate)
        {
            _entries.Add((logLevel, exception));
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
