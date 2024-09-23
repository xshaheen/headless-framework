// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Framework.Database.Sqlite;

[PublicAPI]
public sealed class SqliteConnectionStringChecker(ILogger<SqliteConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);

        try
        {
            await using var connection = new SqliteConnection(connectionString);

            await connection.OpenAsync();
            result.Connected = true;
            result.DatabaseExists = true;
            await connection.CloseAsync();

            return result;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Error while checking connection string");

            return result;
        }
    }
}
