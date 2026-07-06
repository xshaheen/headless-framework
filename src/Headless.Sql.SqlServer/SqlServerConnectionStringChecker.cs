// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Headless.Sql.SqlServer;

/// <summary>
/// <see cref="IConnectionStringChecker"/> implementation for SQL Server that probes server
/// reachability and database existence via <c>Microsoft.Data.SqlClient</c>.
/// </summary>
/// <remarks>
/// The check connects to the <c>master</c> system database first (to verify server reachability)
/// and then calls <c>ChangeDatabaseAsync</c> to confirm the target catalog exists. A connect
/// timeout of 1 second is enforced so the probe fails fast. Any connection error is caught,
/// logged as a warning, and surfaced through the returned tuple rather than re-thrown.
/// </remarks>
/// <param name="logger">Logger used to record connection errors at warning level.</param>
[PublicAPI]
public sealed class SqlServerConnectionStringChecker(ILogger<SqlServerConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    /// <inheritdoc />
    public async Task<ConnectionCheckResult> CheckAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        var connected = false;

        try
        {
            var connString = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 1 };

            var oldDatabaseName = connString.InitialCatalog;
            connString.InitialCatalog = "master";

            await using var conn = new SqlConnection(connString.ConnectionString);

            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            connected = true;

            await conn.ChangeDatabaseAsync(oldDatabaseName, cancellationToken).ConfigureAwait(false);

            await conn.CloseAsync().ConfigureAwait(false);

            return new ConnectionCheckResult(Connected: true, DatabaseExists: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogErrorCheckingConnectionString(e);

            return new ConnectionCheckResult(connected, DatabaseExists: false);
        }
    }
}

internal static partial class SqlServerConnectionStringCheckerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ErrorCheckingConnectionString",
        Level = LogLevel.Warning,
        Message = "Error while checking connection string"
    )]
    public static partial void LogErrorCheckingConnectionString(this ILogger logger, Exception exception);
}
