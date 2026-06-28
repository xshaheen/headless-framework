// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Capstone integration tests for the SQL Server out-of-band commit-detection path: the
/// <c>SqlClientDiagnosticListener</c>-driven detector that turns a real provider transaction edge into a commit
/// coordination signal, with no application-driven <c>SignalAsync</c> call.
/// </summary>
/// <remarks>
/// These tests do <b>not</b> sleep/poll for the drain. The off-thread drain (scheduled by the diagnostic observer via
/// <c>Task.ContinueWith</c>) is observed deterministically: the registered <c>OnCommit</c>/<c>OnRollback</c> callback
/// completes a <see cref="TaskCompletionSource" /> (continuations run asynchronously so the diagnostic thread is never
/// blocked), and the test awaits that TCS. The <c>WaitAsync</c> timeout is a failsafe to turn a hang into a clear test
/// failure, never the success signal.
/// </remarks>
[Collection<SqlServerCommitCoordinationFixture>]
public sealed class SqlServerOutOfBandCommitDetectionTests(SqlServerCommitCoordinationFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan _DrainTimeout = TimeSpan.FromSeconds(15);

    private ServiceProvider _services = null!;
    private SqlServerCommitDiagnosticHostedService _diagnostic = null!;

    public async ValueTask InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSqlServerCommitCoordination();
        collection.AddScoped<ScopedProbe>();
        _services = collection.BuildServiceProvider();

        // The diagnostic only routes after AllListeners.Subscribe runs. Start the hosted service in setup so the
        // subscription is live before the first commit; AllListeners.Subscribe replays the already-registered
        // SqlClientDiagnosticListener synchronously, so the observer is wired the moment StartAsync returns.
        _diagnostic = _services.GetServices<IHostedService>().OfType<SqlServerCommitDiagnosticHostedService>().Single();

        await _diagnostic.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Tear down this test's subscription so observers never accumulate across the run (cross-test diagnostic bleed).
        await _diagnostic.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task should_drain_exactly_once_when_the_sql_server_transaction_commits_out_of_band()
    {
        var ct = TestContext.Current.CancellationToken;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitCount = 0;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        // Enlist AFTER open: ClientConnectionId is Guid.Empty until the connection is opened, and the diagnostic
        // observer correlates the commit edge by that same id on the same open connection.
        var scope = connection.EnlistCommitCoordination(tx, _services);

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    // A single commit can surface both WriteTransactionCommitAfter and WriteConnectionCloseBefore; the
                    // coordinator's first-wins (Active->terminal CompareExchange) collapses them to a single drain.
                    Interlocked.Increment(ref commitCount);
                    drained.TrySetResult();

                    return ValueTask.CompletedTask;
                }
            );

            await _DoTrivialWriteAsync(connection, tx, ct);

            await tx.CommitAsync(ct);

            // Success is the TCS completing (the off-thread drain ran), not the timeout elapsing.
            await drained.Task.WaitAsync(_DrainTimeout, ct);
        }

        // Give the connection-close edge a window to (wrongly) double-drain, then assert it was collapsed to one.
        await connection.CloseAsync();

        Volatile.Read(ref commitCount).Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_discard_when_the_sql_server_transaction_rolls_back_out_of_band()
    {
        var ct = TestContext.Current.CancellationToken;
        var rolledBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitCount = 0;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var scope = connection.EnlistCommitCoordination(tx, _services);

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    Interlocked.Increment(ref commitCount);

                    return ValueTask.CompletedTask;
                }
            );

            // Cannot await a commit callback that must never fire; await the rollback edge deterministically instead.
            scope.Coordinator.OnRollback(
                (_, _) =>
                {
                    rolledBack.TrySetResult();

                    return ValueTask.CompletedTask;
                }
            );

            await _DoTrivialWriteAsync(connection, tx, ct);

            await tx.RollbackAsync(ct);

            await rolledBack.Task.WaitAsync(_DrainTimeout, ct);
        }

        Volatile.Read(ref commitCount).Should().Be(0);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
    }

    [Fact]
    public async Task should_resolve_a_scoped_service_inside_the_out_of_band_commit_callback()
    {
        var ct = TestContext.Current.CancellationToken;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolvedProbe = false;
        var relationalContextSeen = false;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await using var providerScope = _services.CreateAsyncScope();
        var scope = connection.EnlistCommitCoordination(tx, providerScope.ServiceProvider);

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (context, _) =>
                {
                    // CommitContext.Services must resolve a scoped service during the drain.
                    resolvedProbe = context.Services.GetRequiredService<ScopedProbe>() is not null;
                    relationalContextSeen = context.TryGetCapability(out IRelationalCommitContext? _);
                    drained.TrySetResult();

                    return ValueTask.CompletedTask;
                }
            );

            await _DoTrivialWriteAsync(connection, tx, ct);

            await tx.CommitAsync(ct);

            await drained.Task.WaitAsync(_DrainTimeout, ct);
        }

        resolvedProbe.Should().BeTrue();
        relationalContextSeen.Should().BeTrue();
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }

    [Fact]
    public async Task should_drain_each_transaction_independently_when_reusing_one_pooled_connection()
    {
        // Two sequential coordinated transactions on the SAME open connection share one ClientConnectionId (the
        // out-of-band correlation key). Proves the keyed registry isolates them: each transaction's work drains
        // exactly once on its OWN commit, with no cross-transaction drain or scope overwrite.
        var ct = TestContext.Current.CancellationToken;
        var firstDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCount = 0;
        var secondCount = 0;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var tx1 = (SqlTransaction)await connection.BeginTransactionAsync(ct))
        {
            var scope1 = connection.EnlistCommitCoordination(tx1, _services);

            await using (scope1)
            {
                scope1.Coordinator.OnCommit(
                    (_, _) =>
                    {
                        Interlocked.Increment(ref firstCount);
                        firstDrained.TrySetResult();

                        return ValueTask.CompletedTask;
                    }
                );

                await _WriteProbeAsync(connection, tx1, "#commit_probe_first", ct);
                await tx1.CommitAsync(ct);
                await firstDrained.Task.WaitAsync(_DrainTimeout, ct);
            }
        }

        // Reuse the SAME still-open connection: identical ClientConnectionId, the collision-prone case.
        await using (var tx2 = (SqlTransaction)await connection.BeginTransactionAsync(ct))
        {
            var scope2 = connection.EnlistCommitCoordination(tx2, _services);

            await using (scope2)
            {
                scope2.Coordinator.OnCommit(
                    (_, _) =>
                    {
                        Interlocked.Increment(ref secondCount);
                        secondDrained.TrySetResult();

                        return ValueTask.CompletedTask;
                    }
                );

                await _WriteProbeAsync(connection, tx2, "#commit_probe_second", ct);
                await tx2.CommitAsync(ct);
                await secondDrained.Task.WaitAsync(_DrainTimeout, ct);
            }
        }

        Volatile
            .Read(ref firstCount)
            .Should()
            .Be(1, "the first transaction's work drains exactly once on its own commit");
        Volatile
            .Read(ref secondCount)
            .Should()
            .Be(1, "the second transaction's work drains exactly once on its own commit, never on the first");
    }

    private static async Task _DoTrivialWriteAsync(SqlConnection connection, SqlTransaction tx, CancellationToken ct)
    {
        await _WriteProbeAsync(connection, tx, "#commit_probe", ct);
    }

    private static async Task _WriteProbeAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string probe,
        CancellationToken ct
    )
    {
        // A real statement inside the tx so the commit/rollback is a genuine durable edge, not a no-op the provider
        // might elide. A session-scoped temp table is dropped automatically when the connection closes; distinct
        // names let two transactions reuse one connection session without a name collision.
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"CREATE TABLE {probe} (id INT NOT NULL); INSERT INTO {probe} (id) VALUES (1);";
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class ScopedProbe;
}
