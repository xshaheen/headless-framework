// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.SqlServer;

internal static partial class SqlServerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EnsuringTablesCreated",
        Level = LogLevel.Debug,
        Message = "Ensuring messaging tables are created"
    )]
    public static partial void LogEnsuringTablesCreated(this ILogger logger);
}
