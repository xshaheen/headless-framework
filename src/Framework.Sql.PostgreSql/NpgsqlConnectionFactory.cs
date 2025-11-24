// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Nito.AsyncEx;
using Npgsql;

namespace Framework.Sql.PostgreSql;

[PublicAPI]
public sealed class NpgsqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    private NpgsqlConnection? _connection;
    private readonly AsyncLock _lock = new();

    public string GetConnectionString()
    {
        return connectionString;
    }

    public async ValueTask<NpgsqlConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    public async ValueTask<NpgsqlConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = await CreateNewConnectionAsync(cancellationToken);

        return _connection;
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        return await CreateNewConnectionAsync(cancellationToken);
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await GetOpenConnectionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection?.State is ConnectionState.Open)
        {
            await (_connection?.DisposeAsync() ?? ValueTask.CompletedTask);
            _connection = null;
        }
    }
}
