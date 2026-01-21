// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Framework.Sql.Sqlite;

[PublicAPI]
public sealed class SqliteConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public string GetConnectionString()
    {
        return connectionString;
    }

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
