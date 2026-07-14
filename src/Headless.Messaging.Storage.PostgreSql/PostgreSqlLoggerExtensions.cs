// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Storage.PostgreSql;

internal static partial class PostgreSqlLoggerExtensions
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

    [LoggerMessage(
        EventId = 3,
        EventName = "InvalidIndexDropped",
        Level = LogLevel.Warning,
        Message = "Dropped invalid index {IndexName} in schema {Schema} (likely a SIGTERM-interrupted CREATE INDEX CONCURRENTLY); it will be recreated."
    )]
    public static partial void LogInvalidIndexDropped(this ILogger logger, string indexName, string schema);

    [LoggerMessage(
        EventId = 4,
        EventName = "SchedulerBatchFetched",
        Level = LogLevel.Debug,
        Message = "Fetched {Count} delayed/queued message(s) for scheduling from {Table}."
    )]
    public static partial void LogSchedulerBatchFetched(this ILogger logger, int count, string table);

    [LoggerMessage(
        EventId = 5,
        EventName = "PoisonMessageTerminalMarkFailed",
        Level = LogLevel.Warning,
        Message = "Failed to mark poison message {StorageId} terminal in {Table}; the batch proceeds and the row stays leased until its lease expires."
    )]
    public static partial void LogPoisonMessageTerminalMarkFailed(
        this ILogger logger,
        Guid storageId,
        string table,
        Exception exception
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "TrgmExtensionUnavailable",
        Level = LogLevel.Warning,
        Message = "Could not ensure the pg_trgm extension (SqlState {SqlState}: {Reason}). Dashboard content (ILIKE) search will be unavailable until a DBA installs pg_trgm. Messaging write/retry paths are unaffected."
    )]
    public static partial void LogTrgmExtensionUnavailable(this ILogger logger, string? sqlState, string reason);

    [LoggerMessage(
        EventId = 7,
        EventName = "TrgmContentIndexSkipped",
        Level = LogLevel.Information,
        Message = "pg_trgm is not installed; skipping the dashboard content trigram indexes. Install pg_trgm to enable dashboard content search. Messaging write/retry paths are unaffected."
    )]
    public static partial void LogTrgmContentIndexSkipped(this ILogger logger);
}
