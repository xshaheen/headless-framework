// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;
using System.Data.Common;

namespace Headless.DistributedLocks.Postgres;

internal sealed class PostgresFencingTokenSource : IFencingTokenSource, IAsyncDisposable
{
    private const string _SequenceName = "headless_distributed_locks_fence";
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeSpan _commandTimeout;
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _sequenceEnsured;

    public PostgresFencingTokenSource(IOptions<PostgresDistributedLockOptions> options, NpgsqlDataSource dataSource)
    {
        // The data source is shared and owned by the DI registration; it is never disposed here.
        _dataSource = dataSource;
        _commandTimeout = options.Value.CommandTimeout;
    }

    public async ValueTask<long?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    )
    {
        // The handle connection comes from the multiplexing engine's pool and may be shared/dedicated under its own
        // monitoring; Postgres always issues the token on a freshly-opened pooled connection from its owned data
        // source, so the optional handle connection is intentionally ignored here.
        _ = connection;

        await using var pooledConnection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _EnsureSequenceAsync(pooledConnection, cancellationToken).ConfigureAwait(false);

        await using var command = pooledConnection.CreateCommand();
        command.CommandText = $"SELECT nextval('{_SequenceName}')";
        command.CommandTimeout = (int)_commandTimeout.TotalSeconds;

        return (long?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    // Runs the catalog-mutating DDL once per source lifetime instead of on every acquire, which
    // would otherwise hammer pg_class with catalog locks under contention. The in-process gate
    // serializes concurrent first-callers in this process; the transaction-scoped advisory lock plus
    // already-exists SqlState handling makes the CREATE safe across replicas, where PG's
    // CREATE SEQUENCE IF NOT EXISTS check is not atomic with the catalog insert.
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

            // Gate the DDL behind a transaction-scoped advisory lock keyed on the sequence name so racing
            // replicas serialize on the create rather than both passing the IF NOT EXISTS check and one
            // failing. The lock releases automatically on transaction end.
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using (var lockCommand = connection.CreateCommand())
                {
                    lockCommand.Transaction = transaction;
                    lockCommand.CommandText =
                        $"SELECT pg_advisory_xact_lock(hashtextextended('headless_fencing_init:{_SequenceName}', 0))";
                    lockCommand.CommandTimeout = (int)_commandTimeout.TotalSeconds;
                    await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $"CREATE SEQUENCE IF NOT EXISTS {_SequenceName}";
                    command.CommandTimeout = (int)_commandTimeout.TotalSeconds;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState is "42P06" or "42P07" or "23505")
            {
                // A concurrent replica created the sequence (or its schema) between our lock acquire and
                // the create: duplicate_schema / duplicate_table / unique_violation all mean "already
                // created", so treat as success. Roll back this transaction's no-op.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Volatile.Write(ref _sequenceEnsured, true);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        // The data source is shared and owned by the DI registration; only the local gate is disposed here.
        _ensureGate.Dispose();

        return default;
    }
}
