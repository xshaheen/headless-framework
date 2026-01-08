// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Nito.AsyncEx;

namespace Framework.Sql;

[PublicAPI]
public interface ISqlCurrentConnection : IAsyncDisposable
{
    ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
}

[PublicAPI]
public sealed class DefaultSqlCurrentConnection(ISqlConnectionFactory factory) : ISqlCurrentConnection
{
    private DbConnection? _connection;
    private readonly AsyncLock _lock = new();

    public async ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = await factory.CreateNewConnectionAsync(cancellationToken);

        return _connection;
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
