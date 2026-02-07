// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Headless.Sql.SqlServer;

[PublicAPI]
public sealed class SqlServerConnectionStringChecker(ILogger<SqlServerConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);

        try
        {
            var connString = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 1 };

            var oldDatabaseName = connString.InitialCatalog;
            connString.InitialCatalog = "master";

            await using var conn = new SqlConnection(connString.ConnectionString);

            await conn.OpenAsync();
            result.Connected = true;

            await conn.ChangeDatabaseAsync(oldDatabaseName);
            result.DatabaseExists = true;

            await conn.CloseAsync();

            return result;
        }
        catch (Exception e)
        {
            logger.LogErrorCheckingConnectionString(e);

            return result;
        }
    }
}

internal static partial class SqlServerConnectionStringCheckerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ErrorCheckingConnectionString",
        Level = LogLevel.Warning,
        Message = "Error checking connection string"
    )]
    public static partial void LogErrorCheckingConnectionString(this ILogger logger, Exception exception);
}
