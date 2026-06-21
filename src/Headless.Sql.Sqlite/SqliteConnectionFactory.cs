// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Headless.Sql.Sqlite;

/// <summary>
/// <see cref="ISqlConnectionFactory"/> implementation that creates open <see cref="SqliteConnection"/> instances.
/// </summary>
/// <param name="connectionString">The SQLite connection string used for every created connection.</param>
[PublicAPI]
public sealed class SqliteConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    /// <inheritdoc />
    public string GetConnectionString()
    {
        return connectionString;
    }

    /// <summary>
    /// Creates and opens a new <see cref="SqliteConnection"/>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the open operation.</param>
    /// <returns>
    /// An already-open <see cref="SqliteConnection"/>. The caller is responsible for disposing it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the connection opens.
    /// </exception>
    /// <exception cref="SqliteException">
    /// Thrown when the database file cannot be opened or created (for example, due to a permissions
    /// error or an invalid connection string).
    /// </exception>
    public async ValueTask<SqliteConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        return await CreateNewConnectionAsync(cancellationToken);
    }
}
