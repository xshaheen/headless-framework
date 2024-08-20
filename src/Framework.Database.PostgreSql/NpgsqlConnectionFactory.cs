using System.Data;
using Npgsql;

namespace Framework.Database.PostgreSql;

[PublicAPI]
public sealed class NpgsqlConnectionFactory(string connectionString)
    : ISqlConnectionFactory,
        IAsyncDisposable,
        IDisposable
{
    private NpgsqlConnection? _connection;

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

    private NpgsqlConnection _OpenConnection()
    {
        var connection = new NpgsqlConnection(connectionString);
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
