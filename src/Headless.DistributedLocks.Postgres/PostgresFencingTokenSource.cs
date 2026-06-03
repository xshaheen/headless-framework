// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

internal sealed class PostgresFencingTokenSource : IFencingTokenSource, IAsyncDisposable
{
    private const string _SequenceName = "headless_distributed_locks_fence";
    private readonly bool _ownsDataSource;
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeSpan _commandTimeout;
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _sequenceEnsured;

    public PostgresFencingTokenSource(IOptions<PostgresDistributedLockOptions> options)
    {
        _dataSource = options.Value.DataSource ?? NpgsqlDataSource.Create(options.Value.ConnectionString!);
        _ownsDataSource = options.Value.DataSource is null;
        _commandTimeout = options.Value.CommandTimeout;
    }

    public async ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await _EnsureSequenceAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT nextval('{_SequenceName}')";
        command.CommandTimeout = (int)_commandTimeout.TotalSeconds;

        return (long?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    // Runs the catalog-mutating DDL once per source lifetime instead of on every acquire, which
    // would otherwise hammer pg_class with catalog locks under contention. Guarded so concurrent
    // first-callers serialize on the create rather than racing duplicate DDL.
    private async ValueTask _EnsureSequenceAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _sequenceEnsured))
        {
            return;
        }

        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_sequenceEnsured)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SEQUENCE IF NOT EXISTS {_SequenceName}";
            command.CommandTimeout = (int)_commandTimeout.TotalSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _sequenceEnsured, true);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ensureGate.Dispose();

        if (_ownsDataSource)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }
}
