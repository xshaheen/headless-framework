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

    public IDbConnection CreateNewConnection() => _OpenConnection();

    public IDbConnection GetOpenConnection()
    {
        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = _OpenConnection();

        return _connection;
    }

    private SqliteConnection _OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

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
