// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Headless.Sql.PostgreSql;

[PublicAPI]
public sealed class NpgsqlConnectionStringChecker(ILogger<NpgsqlConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);

        try
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 1 };

            var oldDatabaseName = connectionBuilder.Database;
            connectionBuilder.Database = "postgres";

            await using var conn = new NpgsqlConnection(connectionBuilder.ConnectionString);

            await conn.OpenAsync();
            result.Connected = true;

            await conn.ChangeDatabaseAsync(oldDatabaseName!);
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

internal static partial class NpgsqlConnectionStringCheckerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ErrorCheckingConnectionString",
        Level = LogLevel.Warning,
        Message = "Error while checking connection string"
    )]
    public static partial void LogErrorCheckingConnectionString(this ILogger logger, Exception exception);
}
