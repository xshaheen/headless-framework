// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Drives the shared raw-ADO runner directly over an in-memory SQLite connection, with a real scope factory
/// enlisting the transaction: the failure path must surface the operation's own exception even when disposing
/// scope-local state faults inside the rollback drain.
/// </summary>
public sealed class CoordinatedTransactionRunnerTests : TestBase
{
    [Fact]
    public async Task should_rethrow_the_operation_exception_and_log_when_scope_state_disposal_faults_on_rollback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        await _ExecuteAsync(connection, transaction: null, "CREATE TABLE probes (name TEXT NOT NULL)");

        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var logger = new CapturingLogger<CoordinatedTransactionRunnerTests>();
        var postCommitFaults = 0;
        ICommitCoordinator? enlisted = null;

        // Awaited inside the lambda so the connection is provably alive for the runner's whole task (CA2025).
        var act = async () =>
            await CoordinatedTransactionRunner.ExecuteAsync<SqliteConnection, SqliteTransaction, int>(
                connection,
                IsolationLevel.Serializable,
                static (c, isolation, _) => new ValueTask<SqliteTransaction>(c.BeginTransaction(isolation)),
                (c, t) => factory.Open(new RelationalCommitContext(() => c, () => t)),
                async (c, ct) =>
                {
                    enlisted = stack.Current;
                    enlisted!.GetOrAdd(static _ => new ThrowingDisposableState());
                    await _ExecuteAsync(
                        c,
                        (SqliteTransaction)enlisted.Relational!.Transaction!,
                        "INSERT INTO probes (name) VALUES ('rolled-back')",
                        ct
                    );

                    throw new InvalidOperationException("operation-fault");
                },
                logger,
                (_, _) => postCommitFaults++,
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("operation-fault");

        enlisted!.State.Should().Be(CommitCoordinatorState.RolledBack);
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().StartWith("Disposing scope-local state faulted while rolling back");
        postCommitFaults.Should().Be(0, "nothing committed, so the post-commit fault log must stay silent");
        (await _CountProbeRowsAsync(connection)).Should().Be(0, "the failed operation's row must not be durable");
        connection.State.Should().Be(ConnectionState.Open, "the runner closes only connections it opened");
    }

    private static async Task _ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken = default
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> _CountProbeRowsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM probes";

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private sealed class ThrowingDisposableState : IDisposable
    {
        public void Dispose()
        {
            throw new InvalidOperationException("dispose");
        }
    }
}
