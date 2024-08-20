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

    private SqlConnection _OpenConnection()
    {
        var connection = new SqlConnection(connectionString);
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
