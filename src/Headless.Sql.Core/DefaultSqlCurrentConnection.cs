// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Nito.AsyncEx;

namespace Headless.Sql;

/// <summary>
/// Default <see cref="ISqlCurrentConnection"/> implementation that lazily opens a connection on
/// first call and reuses it for subsequent calls within the same scope.
/// </summary>
/// <remarks>
/// Thread-safety is achieved with an <see cref="AsyncLock"/> (from Nito.AsyncEx): only one
/// concurrent caller opens the connection; others wait until it is ready. If the stored
/// connection is found in a non-open state it is disposed and a new one is created.
/// </remarks>
/// <param name="factory">
/// The factory used to create a new connection when one is not yet open.
/// </param>
[PublicAPI]
public sealed class DefaultSqlCurrentConnection(ISqlConnectionFactory factory) : ISqlCurrentConnection
{
    private DbConnection? _connection;
    private readonly AsyncLock _lock = new();

    /// <inheritdoc />
    public async ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = await factory.CreateNewConnectionAsync(cancellationToken).ConfigureAwait(false);

        return _connection;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is { State: ConnectionState.Open })
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
