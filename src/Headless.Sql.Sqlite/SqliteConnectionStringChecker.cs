// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Headless.Sql.Sqlite;

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
            logger.LogErrorCheckingConnectionString(e);

            return result;
        }
    }
}

internal static partial class SqliteConnectionStringCheckerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ErrorCheckingConnectionString",
        Level = LogLevel.Warning,
        Message = "Error while checking connection string"
    )]
    public static partial void LogErrorCheckingConnectionString(this ILogger logger, Exception exception);
}
