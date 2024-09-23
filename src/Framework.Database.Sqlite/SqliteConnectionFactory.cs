// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Data;
using Microsoft.Data.Sqlite;

namespace Framework.Database.Sqlite;

[PublicAPI]
public sealed class SqliteConnectionFactory(string connectionString)
    : ISqlConnectionFactory,
        IAsyncDisposable,
        IDisposable
{
    private SqliteConnection? _connection;

    public string GetConnectionString() => connectionString;

    public async ValueTask<IDbConnection> CreateNewConnectionAsync() => await _OpenConnectionAsync();

    public async ValueTask<IDbConnection> GetOpenConnectionAsync()
    {
        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = await _OpenConnectionAsync();

        return _connection;
    }

    private async ValueTask<SqliteConnection> _OpenConnectionAsync()
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        return connection;
    }

    public void Dispose()
    {
        if (_connection?.State is ConnectionState.Open)
        {
            _connection.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection?.State is ConnectionState.Open)
        {
            await _connection.DisposeAsync();
        }
    }
}
