// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Constants;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

#pragma warning disable CA2100 // The only interpolated value is the schema, validated against the PostgreSQL identifier rules at startup and quoted here.
/// <summary>
/// Implements <see cref="IFencingTokenSource"/> over a PostgreSQL database sequence named
/// <c>headless_distributed_locks_fence</c>, created inside the feature-owned
/// <see cref="DistributedLocksStorageOptions.Schema"/>. Each call to <see cref="NextAsync"/> returns the next
/// value from the sequence, guaranteeing a strictly-increasing token across all processes connected to the same
/// database.
/// </summary>
/// <remarks>
/// <para>
/// The schema and sequence are created lazily on first use (<see cref="_EnsureSequenceAsync"/>). Creation is
/// serialized across in-process callers via a <see cref="SemaphoreSlim"/> and across replicas via a
/// transaction-scoped advisory lock keyed on the qualified sequence name, so the
/// <c>CREATE SEQUENCE IF NOT EXISTS</c> is safe against concurrent process start-up.
/// </para>
/// <para>
/// This source always opens a fresh pooled connection from its owned <see cref="NpgsqlDataSource"/>
/// regardless of the handle connection supplied to <see cref="NextAsync"/>; the handle connection is
/// intentionally ignored because the multiplexing engine may share it with other lock operations.
/// </para>
/// </remarks>
/// <remarks>
/// Initializes the fencing-token source over the shared Npgsql data source, taking the command timeout
/// from the resolved provider options. The backing database sequence is created lazily on first use.
/// </remarks>
/// <param name="options">Provider options supplying the command timeout.</param>
/// <param name="dataSource">
/// The shared <see cref="NpgsqlDataSource"/> injected by the DI registration. Not disposed here;
/// disposal is owned by <see cref="PostgresLockDataSource"/>.
/// </param>
internal sealed class PostgresFencingTokenSource(
    IOptions<PostgreSqlDistributedLockOptions> options,
    IOptions<DistributedLocksStorageOptions> storageOptions,
    NpgsqlDataSource dataSource
) : IFencingTokenSource, IAsyncDisposable
{
    private const string _SequenceName = "headless_distributed_locks_fence";
    private readonly TimeSpan _commandTimeout = options.Value.CommandTimeout;
    private readonly string _schema = storageOptions.Value.Schema;

    // Quoted rather than bare: PostgreSQL case-folds unquoted identifiers, so a mixed-case schema would be
    // created under one name and read back under another.
    private readonly string _qualifiedSequence = $"""
        "{storageOptions.Value.Schema}"."{_SequenceName}"
        """;

    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _sequenceEnsured;

    /// <inheritdoc/>
    /// <remarks>
    /// Always opens a fresh connection from the owned <see cref="NpgsqlDataSource"/>; the optional
    /// <paramref name="connection"/> argument is intentionally ignored. Ensures the database sequence
    /// exists on first call (see <see cref="_EnsureSequenceAsync"/>). Underlying Npgsql errors propagate
    /// to the caller.
    /// </remarks>
    /// <param name="resource">Ignored; the Postgres sequence is shared across all resources.</param>
    /// <param name="connection">
    /// Ignored; the token is always issued on a fresh pooled connection so the handle connection is not
    /// disturbed.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the sequence or sequence-init command.</param>
    /// <returns>A token carrying the next strictly-increasing sequence value.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the sequence value is returned.
    /// </exception>
    public async ValueTask<LockFencingToken?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    )
    {
        // The handle connection comes from the multiplexing engine's pool and may be shared/dedicated under its own
        // monitoring; Postgres always issues the token on a freshly-opened pooled connection from its owned data
        // source, so the optional handle connection is intentionally ignored here.
        _ = connection;

        await using var pooledConnection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await _EnsureSequenceAsync(pooledConnection, cancellationToken).ConfigureAwait(false);

        await using var command = pooledConnection.CreateCommand();
        command.CommandText = $"SELECT nextval('{_qualifiedSequence}')";
        command.CommandTimeout = (int)_commandTimeout.TotalSeconds;

        var value = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        return new LockFencingToken(value);
    }

    // Runs the catalog-mutating DDL once per source lifetime instead of on every acquire, which
    // would otherwise hammer pg_class with catalog locks under contention. The in-process gate
    // serializes concurrent first-callers in this process; the transaction-scoped advisory locks plus
    // one rerun on an already-exists SqlState make the CREATE safe across replicas and foreign creators,
    // where PG's IF NOT EXISTS check is not atomic with the catalog insert.
    private async ValueTask _EnsureSequenceAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _sequenceEnsured))
        {
            return;
        }

        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (Volatile.Read(ref _sequenceEnsured))
            {
                return;
            }

            // A schema or sequence creator outside our advisory locks (a consumer's EF migration, other application
            // code) can still commit the same CREATE first, failing this transaction with 42P06/42P07 or 23505 on the
            // catalog unique index. The rollback discards the sequence too, so treating that as success would leave
            // every later nextval failing. The conflicting creator has committed by the time we see the error, so one
            // rerun in a fresh transaction passes the IF NOT EXISTS guards; a second failure propagates and the next
            // caller tries again, because the ensured flag is set only after a commit.
            for (var attempt = 1; ; attempt++)
            {
                // Gate the DDL behind a transaction-scoped advisory lock keyed on the sequence name so racing
                // replicas serialize on the create rather than both passing the IF NOT EXISTS check and one
                // failing. The lock releases automatically on transaction end. The key names this feature so it
                // never shares an advisory lock with the Fencing feature's own storage initializer.
                await using var transaction = await connection
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await using (var lockCommand = connection.CreateCommand())
                    {
                        lockCommand.Transaction = transaction;
                        lockCommand.CommandText =
                            $"SELECT pg_advisory_xact_lock(hashtextextended('headless_distributed_locks_init:{_qualifiedSequence}', 0))";
                        lockCommand.CommandTimeout = (int)_commandTimeout.TotalSeconds;
                        await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = $"""
                            {PostgreSqlSchemaInitLock.AcquireStatement(_schema)}
                            CREATE SCHEMA IF NOT EXISTS "{_schema}";
                            CREATE SEQUENCE IF NOT EXISTS {_qualifiedSequence};
                            """;
                        command.CommandTimeout = (int)_commandTimeout.TotalSeconds;
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    Volatile.Write(ref _sequenceEnsured, value: true);

                    return;
                }
                catch (PostgresException exception)
                    when (attempt == 1
                        && exception.SqlState
                            is SqlErrorCodes.PostgreSql.DuplicateSchema
                                or SqlErrorCodes.PostgreSql.DuplicateTable
                                or SqlErrorCodes.PostgreSql.UniqueViolation
                    )
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    /// <summary>
    /// Disposes the in-process <see cref="SemaphoreSlim"/> gate. The shared
    /// <see cref="NpgsqlDataSource"/> is not disposed here; disposal is owned by the DI registration.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // The data source is shared and owned by the DI registration; only the local gate is disposed here.
        _ensureGate.Dispose();

        return default;
    }
}
#pragma warning restore CA2100
