// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.PostgreSql;

internal static partial class PostgreSqlLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EnsuringTablesCreated",
        Level = LogLevel.Debug,
        Message = "Ensuring messaging tables are created"
    )]
    public static partial void LogEnsuringTablesCreated(this ILogger logger);
}
