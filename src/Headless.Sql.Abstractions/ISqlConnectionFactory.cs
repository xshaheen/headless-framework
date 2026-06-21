// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Sql;

/// <summary>
/// Creates and opens raw database connections from a fixed connection string.
/// </summary>
/// <remarks>
/// Prefer injecting <see cref="ISqlCurrentConnection"/> when a single shared, lazily-opened connection
/// per scope is sufficient. Use this factory directly when you need independent connections — for example,
/// to run parallel queries or to manage connection lifetime explicitly.
/// </remarks>
[PublicAPI]
public interface ISqlConnectionFactory
{
    /// <summary>
    /// Returns the connection string this factory was configured with.
    /// </summary>
    /// <returns>The raw ADO.NET connection string.</returns>
    string GetConnectionString();

    /// <summary>
    /// Creates and opens a new database connection.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the open operation.</param>
    /// <returns>
    /// An already-open <see cref="DbConnection"/>. The caller is responsible for disposing it when
    /// the connection is no longer needed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the connection opens.
    /// </exception>
    ValueTask<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default);
}
