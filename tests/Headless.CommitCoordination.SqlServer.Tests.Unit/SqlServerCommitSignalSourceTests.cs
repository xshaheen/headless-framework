// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public sealed class SqlServerCommitSignalSourceTests
{
    [Fact]
    public async Task should_signal_attached_scope_by_provider_key()
    {
        var source = new SqlServerCommitSignalSource(new CommitScopeFactory(new CommitScopeStack()));
        var calls = 0;
        var key = new object();
        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = key,
            },
            CancellationToken.None
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit((_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await source.SignalCommittedAsync(key, CancellationToken.None);
        }

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_ignore_signal_for_unattached_key_silently()
    {
        var source = new SqlServerCommitSignalSource(new CommitScopeFactory(new CommitScopeStack()));

        // No scope attached for this key — the diagnostic fires for every connection, so an absent key is the
        // normal case and must be a silent no-op (no throw).
        await source.SignalCommittedAsync(new object(), CancellationToken.None);
        await source.SignalRolledBackAsync(new object(), CancellationToken.None);
    }

    [Fact]
    public async Task diagnostic_observer_should_drain_the_scope_correlated_by_client_connection_id_on_commit()
    {
        var source = new SqlServerCommitSignalSource(new CommitScopeFactory(new CommitScopeStack()));
        var observer = new SqlServerCommitDiagnosticObserver(
            source,
            NullLogger<SqlServerCommitDiagnosticObserver>.Instance
        );

        await using var connection = new SqlConnection();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
            scope.Coordinator.OnCommit((_, _) =>
            {
                committed.SetResult();

                return ValueTask.CompletedTask;
            });

            // Fire the synthetic SqlClient commit-after event the observer reflects over.
            observer.OnNext(
                new KeyValuePair<string, object?>(
                    SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                    new DiagnosticPayload(connection, Operation: "Commit")
                )
            );

            await committed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public void diagnostic_observer_should_ignore_an_event_without_a_sql_connection()
    {
        var source = new SqlServerCommitSignalSource(new CommitScopeFactory(new CommitScopeStack()));
        var observer = new SqlServerCommitDiagnosticObserver(
            source,
            NullLogger<SqlServerCommitDiagnosticObserver>.Instance
        );

        // Payload with no SqlConnection — the observer cannot correlate it and must not throw.
        observer.OnNext(
            new KeyValuePair<string, object?>(
                SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                new { Operation = "Commit" }
            )
        );
    }

    private sealed record DiagnosticPayload(SqlConnection Connection, string Operation);
}
