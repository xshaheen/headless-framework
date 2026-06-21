// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Headless.Sql.Sqlite;

/// <summary>
/// <see cref="IConnectionStringChecker"/> implementation for SQLite that probes whether a
/// database file can be opened.
/// </summary>
/// <remarks>
/// Unlike the server-based checkers, SQLite does not have a separate server-reachability step.
/// If <c>OpenAsync</c> succeeds both <c>Connected</c> and <c>DatabaseExists</c> are set to
/// <see langword="true"/> because opening a SQLite connection and accessing the database are the
/// same operation. Any error is caught, logged as a warning, and surfaced through the returned
/// tuple rather than re-thrown.
/// </remarks>
/// <param name="logger">Logger used to record connection errors at warning level.</param>
[PublicAPI]
public sealed class SqliteConnectionStringChecker(ILogger<SqliteConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    /// <inheritdoc />
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
