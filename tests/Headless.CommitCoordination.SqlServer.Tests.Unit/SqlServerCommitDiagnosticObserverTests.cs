// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public sealed class SqlServerCommitDiagnosticObserverTests : TestBase
{
    [Fact]
    public async Task should_return_promptly_when_wait_for_drains_no_drains_are_pending()
    {
        var (observer, _, _) = _CreateObserver();

        // No drains were ever tracked; the shutdown wait must complete without spinning its bounded iterations.
        await observer.WaitForDrainsAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_log_drain_fault_and_not_propagate_a_drain_origin_cancellation_when_wait_for_drains()
    {
        // Pins the catch-filter behavior: an OperationCanceledException ORIGINATING FROM A DRAIN (commit callback
        // throws OCE) while the shutdown token is NOT canceled must be logged as a drain fault and swallowed —
        // only a cancellation of the shutdown token itself may propagate out of WaitForDrainsAsync.
        var (observer, source, logger) = _CreateObserver();
        await using var connection = new SqlConnection();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = connection.ClientConnectionId,
            },
            CancellationToken.None
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                async (_, _) =>
                {
                    await gate.Task;

                    throw new OperationCanceledException();
                }
            );

            // Track a pending drain (the commit callback is parked on the gate).
            observer.OnNext(
                new KeyValuePair<string, object?>(
                    SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                    new DiagnosticPayload(connection, Operation: "Commit")
                )
            );

            // The first snapshot is taken synchronously before the await, so the pending drain is observed; the
            // gate then cancels the drain while the shutdown token stays un-canceled.
            var wait = observer.WaitForDrainsAsync(CancellationToken.None);
            gate.SetResult();

            await wait.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }

        logger
            .Entries.Should()
            .Contain(
                entry => entry.Level == LogLevel.Error && entry.Message.Contains("signal drain faulted"),
                "a drain-origin cancellation is a drain fault, not a shutdown cancellation"
            );
    }

    [Fact]
    public async Task should_throw_when_wait_for_drains_the_shutdown_token_is_canceled_during_the_wait()
    {
        var (observer, source, _) = _CreateObserver();
        await using var connection = new SqlConnection();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = connection.ClientConnectionId,
            },
            CancellationToken.None
        );

        scope.Coordinator.OnCommit(async (_, _) => await gate.Task);

        observer.OnNext(
            new KeyValuePair<string, object?>(
                SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                new DiagnosticPayload(connection, Operation: "Commit")
            )
        );

        var wait = observer.WaitForDrainsAsync(cts.Token);
        await cts.CancelAsync();

        await wait.Invoking(async x => await x.WaitAsync(TimeSpan.FromSeconds(5), AbortToken))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        // Release the parked drain so the scope disposes cleanly.
        gate.SetResult();
        await scope.DisposeAsync();
    }

    private static (
        SqlServerCommitDiagnosticObserver Observer,
        SqlServerCommitSignalSource Source,
        CollectingLogger Logger
    ) _CreateObserver()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var logger = new CollectingLogger();
        var observer = new SqlServerCommitDiagnosticObserver(source, logger);

        return (observer, source, logger);
    }

    private sealed record DiagnosticPayload(SqlConnection Connection, string Operation);

    private sealed class CollectingLogger : ILogger<SqlServerCommitDiagnosticObserver>
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
            lock (_entries)
            {
                _entries.Add((logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
