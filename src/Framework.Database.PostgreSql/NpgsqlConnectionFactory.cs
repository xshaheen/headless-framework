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

    private async ValueTask<NpgsqlConnection> _OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(connectionString);
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
