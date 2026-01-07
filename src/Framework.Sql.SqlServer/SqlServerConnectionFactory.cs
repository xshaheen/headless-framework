// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Framework.Sql.SqlServer;

[PublicAPI]
public sealed class SqlServerConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public string GetConnectionString()
    {
        return connectionString;
    }

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
