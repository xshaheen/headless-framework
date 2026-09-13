// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The explicit-signal contract of <c>SqlConnection.EnlistCommitCoordination</c> against a real SQL Server: the
/// caller signals after the transaction completes, and a dispose that arrives un-signalled after a completed
/// transaction is logged as a forgotten signal.
/// </summary>
[Collection<SqlServerCommitCoordinationFixture>]
public sealed class SqlServerExplicitEnlistmentTests(SqlServerCommitCoordinationFixture fixture) : TestBase
{
    private const string _ScopeCategory = "Headless.CommitCoordination.SqlServer.SqlServerCommitScope";

    [Fact]
    public async Task should_drain_once_when_the_caller_signals_committed_after_commit()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = _BuildServices(logs);
        var calls = 0;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (var scope = connection.EnlistCommitCoordination(transaction, services))
        {
            scope.Coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await _TouchAsync(connection, transaction);
            await transaction.CommitAsync(AbortToken);
            await scope.SignalAsync(CommitOutcome.Committed);
        }

        calls.Should().Be(1);
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_warn_and_discard_when_disposed_without_a_signal_after_commit()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = _BuildServices(logs);
        var calls = 0;
        ICommitCoordinator coordinator;

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (var scope = connection.EnlistCommitCoordination(transaction, services))
        {
            coordinator = scope.Coordinator;
            coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await _TouchAsync(connection, transaction);
            await transaction.CommitAsync(AbortToken);
            // Forgotten signal: nothing observes the SqlClient commit edge for the caller.
        }

        calls.Should().Be(0);
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        logs.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Category == _ScopeCategory)
            .Which.Message.Should()
            .Contain("disposed without a signal");
    }

    [Fact]
    public async Task should_not_warn_when_disposed_without_a_signal_before_the_transaction_completes()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = _BuildServices(logs);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        await using (connection.EnlistCommitCoordination(transaction, services))
        {
            await _TouchAsync(connection, transaction);
            // The operation failed: the scope is disposed while the transaction is still open.
        }

        await transaction.RollbackAsync(AbortToken);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    private static ServiceProvider _BuildServices(CapturingLoggerProvider logs)
    {
        return new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(logs))
            .AddSqlServerCommitCoordination()
            .BuildServiceProvider();
    }

    private static async Task _TouchAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand("SELECT 1", connection, transaction);
        await command.ExecuteScalarAsync(AbortToken);
    }
}
