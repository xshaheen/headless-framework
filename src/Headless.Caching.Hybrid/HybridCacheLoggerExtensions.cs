// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Caching;

internal static partial class HybridCacheLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "LocalCacheHit",
        Level = LogLevel.Trace,
        Message = "Local cache hit: {Key}"
    )]
    public static partial void LogLocalCacheHit(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 2,
        EventName = "LocalCacheMiss",
        Level = LogLevel.Trace,
        Message = "Local cache miss: {Key}"
    )]
    public static partial void LogLocalCacheMiss(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "SettingLocalCacheKey",
        Level = LogLevel.Trace,
        Message = "Setting local cache key: {Key} with expiration: {Expiration}"
    )]
    public static partial void LogSettingLocalCacheKey(this ILogger logger, string key, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 4,
        EventName = "FlushedLocalCache",
        Level = LogLevel.Trace,
        Message = "Flushed local cache"
    )]
    public static partial void LogFlushedLocalCache(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "UnknownInvalidateCacheMessage",
        Level = LogLevel.Warning,
        Message = "Unknown invalidate cache message"
    )]
    public static partial void LogUnknownInvalidateCacheMessage(this ILogger logger);

    [LoggerMessage(
        EventId = 6,
        EventName = "InvalidatingLocalCacheFromRemote",
        Level = LogLevel.Trace,
        Message = "Invalidating local cache from remote: id={CacheId} keyCount={KeyCount} hasPrefix={HasPrefix} flushAll={FlushAll}"
    )]
    public static partial void LogInvalidatingLocalCacheFromRemote(
        this ILogger logger,
        string? cacheId,
        int keyCount,
        bool hasPrefix,
        bool flushAll
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "FailedToWriteToL2Cache",
        Level = LogLevel.Warning,
        Message = "Failed to write to L2 cache for key {Key}, L1 will still be populated"
    )]
    public static partial void LogFailedToWriteToL2Cache(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 8,
        EventName = "BatchSettingLocalCacheKeys",
        Level = LogLevel.Trace,
        Message = "Batch setting {Count} local cache keys with expiration: {Expiration}"
    )]
    public static partial void LogBatchSettingLocalCacheKeys(this ILogger logger, int count, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 9,
        EventName = "SettingKey",
        Level = LogLevel.Trace,
        Message = "Setting key {Key} with expiration: {Expiration}"
    )]
    public static partial void LogSettingKey(this ILogger logger, string key, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 10,
        EventName = "SettingKeys",
        Level = LogLevel.Trace,
        Message = "Setting keys {Keys} with expiration: {Expiration}"
    )]
    public static partial void LogSettingKeys(this ILogger logger, ICollection<string> keys, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 11,
        EventName = "AddingKeyToLocalCache",
        Level = LogLevel.Trace,
        Message = "Adding key {Key} to local cache with expiration: {Expiration}"
    )]
    public static partial void LogAddingKeyToLocalCache(this ILogger logger, string key, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 12,
        EventName = "FailedToPublishCacheInvalidation",
        Level = LogLevel.Warning,
        Message = "Failed to publish cache invalidation (keyCount={KeyCount}, hasPrefix={HasPrefix}, flushAll={FlushAll}), other instances may serve stale data until TTL expires"
    )]
    public static partial void LogFailedToPublishCacheInvalidation(
        this ILogger logger,
        Exception exception,
        int keyCount,
        bool hasPrefix,
        bool flushAll
    );
}
