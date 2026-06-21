// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Npgsql;

namespace Headless.Sql.PostgreSql;

/// <summary>
/// <see cref="ISqlConnectionFactory"/> implementation that creates open <see cref="NpgsqlConnection"/> instances.
/// </summary>
/// <param name="connectionString">The Npgsql connection string used for every created connection.</param>
[PublicAPI]
public sealed class NpgsqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    /// <inheritdoc />
    public string GetConnectionString()
    {
        return connectionString;
    }

    /// <summary>
    /// Creates and opens a new <see cref="NpgsqlConnection"/>.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the open operation.</param>
    /// <returns>
    /// An already-open <see cref="NpgsqlConnection"/>. The caller is responsible for disposing it.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the connection opens.
    /// </exception>
    /// <exception cref="NpgsqlException">
    /// Thrown when the server is unreachable or authentication fails.
    /// </exception>
    public async ValueTask<NpgsqlConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        return await CreateNewConnectionAsync(cancellationToken).ConfigureAwait(false);
    }
}
