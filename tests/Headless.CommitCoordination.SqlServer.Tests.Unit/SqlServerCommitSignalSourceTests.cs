// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public sealed class SqlServerCommitSignalSourceTests : TestBase
{
    [Fact]
    public async Task should_signal_attached_scope_by_provider_key()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var calls = 0;
        var key = new object();
        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = key,
            },
            AbortToken
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );

            await source.SignalCommittedAsync(key);
        }

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_keep_owned_service_scope_alive_until_out_of_band_drain_completes()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        await using var provider = services.BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();
        var key = new object();
        ScopedMarker? marker = null;

        var scope = source.Attach(
            new CommitCoordinatorBindings { Services = callerScope.ServiceProvider, ProviderTransactionKey = key },
            AbortToken
        );

        scope.Coordinator.OnCommit(
            (context, _) =>
            {
                marker = context.Services.GetRequiredService<ScopedMarker>();

                return ValueTask.CompletedTask;
            }
        );

        await callerScope.DisposeAsync();
        await source.SignalCommittedAsync(key);
        await scope.DisposeAsync();

        marker.Should().NotBeNull();
    }

    [Fact]
    public async Task should_ignore_signal_for_unattached_key_silently()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );

        // No scope attached for this key — the diagnostic fires for every connection, so an absent key is the
        // normal case and must be a silent no-op (no throw) that completes SYNCHRONOUSLY: the fast-path returns
        // ValueTask.CompletedTask, which is what lets the observer's _Drain skip the Task allocation.
        var committed = source.SignalCommittedAsync(new object());
        var rolledBack = source.SignalRolledBackAsync(new object());

        committed
            .IsCompletedSuccessfully.Should()
            .BeTrue("the uncoordinated-key fast-path returns a completed ValueTask");
        rolledBack
            .IsCompletedSuccessfully.Should()
            .BeTrue("the uncoordinated-key fast-path returns a completed ValueTask");

        await committed;
        await rolledBack;
    }

    [Fact]
    public async Task should_drain_the_scope_correlated_by_client_connection_id_on_commit_when_diagnostic_observer()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
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
            AbortToken
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    committed.SetResult();

                    return ValueTask.CompletedTask;
                }
            );

            // Fire the synthetic SqlClient commit-after event the observer reflects over.
            observer.OnNext(
                new KeyValuePair<string, object?>(
                    SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                    new DiagnosticPayload(connection, Operation: "Commit")
                )
            );

            await committed.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }

        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_roll_back_the_scope_when_diagnostic_observer_commit_after_event_carries_rollback_operation()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var observer = new SqlServerCommitDiagnosticObserver(
            source,
            NullLogger<SqlServerCommitDiagnosticObserver>.Instance
        );

        await using var connection = new SqlConnection();
        var rolledBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commits = 0;

        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = connection.ClientConnectionId,
            },
            AbortToken
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    Interlocked.Increment(ref commits);

                    return ValueTask.CompletedTask;
                }
            );
            scope.Coordinator.OnRollback(
                (_, _) =>
                {
                    rolledBack.SetResult();

                    return ValueTask.CompletedTask;
                }
            );

            // Same commit-after event key, but the payload carries a "Rollback" operation — the observer must treat
            // it as a rollback edge, not a commit.
            observer.OnNext(
                new KeyValuePair<string, object?>(
                    SqlServerCommitDiagnosticObserver.SqlAfterCommitTransaction,
                    new DiagnosticPayload(connection, Operation: "Rollback")
                )
            );

            await rolledBack.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }

        commits.Should().Be(0, "a rollback-operation commit-after event must never run commit work");
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public void should_ignore_an_event_without_a_sql_connection_when_diagnostic_observer()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
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

    [Fact]
    public void should_throw_when_provider_key_already_has_active_scope()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var key = new object();
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = source.Attach(
            new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
            AbortToken
        );

        source
            .Invoking(x =>
                x.Attach(
                    new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
                    AbortToken
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "A SQL Server commit coordination scope is already attached for this provider transaction key."
            );
    }

    [Fact]
    public async Task should_preserve_a_successor_scope_when_a_disposed_predecessor_shared_the_same_key()
    {
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var key = new object();
        var firstCommits = 0;
        var secondCommits = 0;

        // Predecessor lives in its own async flow so its ambient frame never leaks into the test flow; this mirrors a
        // pooled connection reused across requests (same ClientConnectionId, independent ambient scopes).
        ICommitScope first = null!;
        await Task.Run(
            async () =>
            {
                first = source.Attach(
                    new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
                    AbortToken
                );
                first.Coordinator.OnCommit(
                    (_, _) =>
                    {
                        Interlocked.Increment(ref firstCommits);

                        return ValueTask.CompletedTask;
                    }
                );

                // Commit removes the predecessor from the registry (synchronous TryRemove) but does NOT dispose it.
                await source.SignalCommittedAsync(key);
            },
            AbortToken
        );

        // Successor reuses the same key now that the registry slot is free.
        var second = source.Attach(
            new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
            AbortToken
        );
        second.Coordinator.OnCommit(
            (_, _) =>
            {
                Interlocked.Increment(ref secondCommits);

                return ValueTask.CompletedTask;
            }
        );

        // Disposing the predecessor must NOT evict the successor that now owns the key (remove-if-equal).
        await first.DisposeAsync();

        await source.SignalCommittedAsync(key);
        await second.DisposeAsync();

        firstCommits.Should().Be(1);
        secondCommits.Should().Be(1, "the successor must remain registered after the predecessor is disposed");
        second.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    private sealed record DiagnosticPayload(SqlConnection Connection, string Operation);

    private sealed class ScopedMarker;
}
