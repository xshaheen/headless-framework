// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

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
