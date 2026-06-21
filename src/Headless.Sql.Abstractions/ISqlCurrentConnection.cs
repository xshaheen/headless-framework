// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Nito.AsyncEx;

namespace Headless.Sql;

/// <summary>
/// Provides a single, lazily-opened database connection that is shared within a scope.
/// </summary>
/// <remarks>
/// Intended for scoped DI lifetimes. Multiple callers within the same scope receive the same
/// underlying <see cref="DbConnection"/> without re-opening it. Dispose the scope (or the
/// implementation directly) to close and release the connection.
/// </remarks>
[PublicAPI]
public interface ISqlCurrentConnection : IAsyncDisposable
{
    /// <summary>
    /// Returns the ambient open connection for the current scope, opening it on first access.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the open operation on first access.</param>
    /// <returns>
    /// An open <see cref="DbConnection"/> whose lifetime is tied to the current scope. Do not
    /// dispose the returned connection directly; dispose the <see cref="ISqlCurrentConnection"/>
    /// instance instead.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the connection opens.
    /// </exception>
    ValueTask<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default);
}

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
        if (_connection?.State is ConnectionState.Open)
        {
            await (_connection?.DisposeAsync() ?? ValueTask.CompletedTask).ConfigureAwait(false);
            _connection = null;
        }
    }
}
