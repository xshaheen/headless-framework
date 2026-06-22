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
        EventName = "FailedToReadFromL2Cache",
        Level = LogLevel.Warning,
        Message = "Failed to read from L2 cache for key {Key}, treating it as a miss"
    )]
    public static partial void LogFailedToReadFromL2Cache(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 10,
        EventName = "SettingKey",
        Level = LogLevel.Trace,
        Message = "Setting key {Key} with expiration: {Expiration}"
    )]
    public static partial void LogSettingKey(this ILogger logger, string key, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 11,
        EventName = "SettingKeys",
        Level = LogLevel.Trace,
        Message = "Setting keys {Keys} with expiration: {Expiration}"
    )]
    public static partial void LogSettingKeys(this ILogger logger, ICollection<string> keys, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 12,
        EventName = "AddingKeyToLocalCache",
        Level = LogLevel.Trace,
        Message = "Adding key {Key} to local cache with expiration: {Expiration}"
    )]
    public static partial void LogAddingKeyToLocalCache(this ILogger logger, string key, TimeSpan? expiration);

    [LoggerMessage(
        EventId = 13,
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

    [LoggerMessage(
        EventId = 14,
        EventName = "IgnoredStaleRemoteInvalidation",
        Level = LogLevel.Debug,
        Message = "Ignored a remote invalidation for key {Key}: a pending local auto-recovery operation for the key is at least as new"
    )]
    public static partial void LogIgnoredStaleRemoteInvalidation(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 15,
        EventName = "FailedBulkL2CacheOperation",
        Level = LogLevel.Warning,
        Message = "Failed to perform a bulk L2 cache operation for {KeyCount} key(s); degrading to partial L1 result"
    )]
    public static partial void LogFailedBulkL2CacheOperation(this ILogger logger, Exception exception, int keyCount);

    [LoggerMessage(
        EventId = 20,
        EventName = "BulkDistributedCacheReadDegraded",
        Level = LogLevel.Warning,
        Message = "Bulk L2 cache read for {KeyCount} key(s) did not complete ({Reason}); degrading to partial L1 result"
    )]
    public static partial void LogBulkDistributedCacheReadDegraded(this ILogger logger, int keyCount, string reason);

    [LoggerMessage(
        EventId = 16,
        EventName = "BackgroundDistributedCacheOperationFailed",
        Level = LogLevel.Error,
        Message = "A backgrounded L2 cache operation faulted unexpectedly for key {Key} ({ExceptionType}); the caller already returned"
    )]
    public static partial void LogBackgroundDistributedCacheOperationFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 17,
        EventName = "DistributedCacheReadTimedOut",
        Level = LogLevel.Warning,
        Message = "Distributed L2 cache read timed out for key {Key} after {Timeout} ({TimeoutKind}); degrading to local fallback or miss"
    )]
    public static partial void LogDistributedCacheReadTimedOut(
        this ILogger logger,
        string key,
        TimeSpan timeout,
        string timeoutKind
    );

    [LoggerMessage(
        EventId = 18,
        EventName = "DistributedCacheCircuitOpened",
        Level = LogLevel.Warning,
        Message = "Distributed L2 cache circuit opened for {Duration} after a failure on key {Key}"
    )]
    public static partial void LogDistributedCacheCircuitOpened(
        this ILogger logger,
        Exception exception,
        string key,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 19,
        EventName = "DistributedCacheCircuitClosed",
        Level = LogLevel.Warning,
        Message = "Distributed L2 cache circuit closed; L2 operations are enabled again"
    )]
    public static partial void LogDistributedCacheCircuitClosed(this ILogger logger);

    [LoggerMessage(
        EventId = 23,
        EventName = "FailedToRefreshL2Cache",
        Level = LogLevel.Warning,
        Message = "Failed to refresh sliding TTL on L2 cache for key {Key}; L1 TTL was still re-armed"
    )]
    public static partial void LogFailedToRefreshL2Cache(this ILogger logger, Exception exception, string key);
}
