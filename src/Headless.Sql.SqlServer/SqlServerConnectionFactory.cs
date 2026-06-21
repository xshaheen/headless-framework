// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Headless.Sql.SqlServer;

/// <summary>
/// <see cref="ISqlConnectionFactory"/> implementation that creates open <see cref="SqlConnection"/> instances.
/// </summary>
/// <param name="connectionString">The SQL Server connection string used for every created connection.</param>
[PublicAPI]
public sealed class SqlServerConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    /// <inheritdoc />
    public string GetConnectionString()
    {
        return connectionString;
    }

    /// <summary>
    /// Creates and opens a new <see cref="SqlConnection"/>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the open operation.</param>
    /// <returns>
    /// An already-open <see cref="SqlConnection"/>. The caller is responsible for disposing it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the connection opens.
    /// </exception>
    /// <exception cref="SqlException">
    /// Thrown when the server is unreachable or authentication fails.
    /// </exception>
    public async ValueTask<SqlConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        return await CreateNewConnectionAsync(cancellationToken);
    }
}
