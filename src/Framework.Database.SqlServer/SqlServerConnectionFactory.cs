// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Data;
using Microsoft.Data.SqlClient;

namespace Framework.Database.SqlServer;

[PublicAPI]
public sealed class SqlServerConnectionFactory(string connectionString)
    : ISqlConnectionFactory,
        IAsyncDisposable,
        IDisposable
{
    private SqlConnection? _connection;

    public string GetConnectionString() => connectionString;

    public async ValueTask<IDbConnection> CreateNewConnectionAsync() => await _OpenConnection();

    public async ValueTask<IDbConnection> GetOpenConnectionAsync()
    {
        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = await _OpenConnection();

        return _connection;
    }

    private async ValueTask<SqlConnection> _OpenConnection()
    {
        var connection = new SqlConnection(connectionString);
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
