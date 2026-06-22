// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Storage.SqlServer;

internal static partial class SqlServerLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EnsuringTablesCreated",
        Level = LogLevel.Debug,
        Message = "Ensuring messaging tables are created"
    )]
    public static partial void LogEnsuringTablesCreated(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "PoisonMessageSkipped",
        Level = LogLevel.Warning,
        Message = "Skipping un-deserializable message {StorageId} during retry pickup from {Table}; the batch proceeds and the row stays leased until its lease expires."
    )]
    public static partial void LogPoisonMessageSkipped(
        this ILogger logger,
        Guid storageId,
        string table,
        Exception exception
    );
}
