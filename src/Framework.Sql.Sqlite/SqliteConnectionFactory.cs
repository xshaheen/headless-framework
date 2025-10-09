// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Nito.AsyncEx;

namespace Framework.Sql.Sqlite;

[PublicAPI]
public sealed class SqliteConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    private SqliteConnection? _connection;
    private readonly AsyncLock _lock = new();

    public string GetConnectionString()
    {
        return connectionString;
    }

    public async ValueTask<SqliteConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    public async ValueTask<SqliteConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync();

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
