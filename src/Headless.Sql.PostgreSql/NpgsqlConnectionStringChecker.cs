// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Headless.Sql.PostgreSql;

/// <summary>
/// <see cref="IConnectionStringChecker"/> implementation for PostgreSQL that probes server
/// reachability and database existence via Npgsql.
/// </summary>
/// <remarks>
/// The check connects to the <c>postgres</c> maintenance database first (to verify server
/// reachability) and then calls <c>ChangeDatabaseAsync</c> to confirm the target database exists.
/// A connect timeout of 1 second is enforced so the probe fails fast. Any connection error is
/// caught, logged as a warning, and surfaced through the returned tuple rather than re-thrown.
/// </remarks>
/// <param name="logger">Logger used to record connection errors at warning level.</param>
[PublicAPI]
public sealed class NpgsqlConnectionStringChecker(ILogger<NpgsqlConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    /// <inheritdoc />
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
