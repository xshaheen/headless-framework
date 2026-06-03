// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

internal sealed class PostgresFencingTokenSource : IFencingTokenSource, IAsyncDisposable
{
    private const string _SequenceName = "headless_distributed_locks_fence";
    private readonly bool _ownsDataSource;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresFencingTokenSource(IOptions<PostgresDistributedLockOptions> options)
    {
        _dataSource = options.Value.DataSource ?? NpgsqlDataSource.Create(options.Value.ConnectionString!);
        _ownsDataSource = options.Value.DataSource is null;
    }

    public async ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE SEQUENCE IF NOT EXISTS {_SequenceName};
            SELECT nextval('{_SequenceName}');
            """;

        return (long?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsDataSource)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }
}
