// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Npgsql;

namespace Headless.Sql.PostgreSql;

[PublicAPI]
public sealed class NpgsqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public string GetConnectionString()
    {
        return connectionString;
    }

    public async ValueTask<NpgsqlConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    async ValueTask<DbConnection> ISqlConnectionFactory.CreateNewConnectionAsync(CancellationToken cancellationToken)
    {
        return await CreateNewConnectionAsync(cancellationToken);
    }
}
