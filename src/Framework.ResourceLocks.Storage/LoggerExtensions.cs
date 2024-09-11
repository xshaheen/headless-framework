// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks.Caching;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "LockReleaseStarted",
        Level = LogLevel.Trace,
        Message = "ReleaseAsync Start: {Resource} ({LockId})"
    )]
    internal static partial void LogReleaseStarted(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 2,
        EventName = "LockReleaseReleased",
        Level = LogLevel.Debug,
        Message = "Released lock: {Resource} ({LockId})"
    )]
    internal static partial void LogReleaseReleased(ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 3,
        EventName = "RenewingLock",
        Level = LogLevel.Debug,
        Message = "Renewing lock {Resource} ({LockId}) for {Duration:g}"
    )]
    internal static partial void LogRenewingLock(ILogger logger, string resource, string lockId, TimeSpan? duration);

    [LoggerMessage(
        EventId = 4,
        EventName = "LockReleased",
        Level = LogLevel.Trace,
        Message = "Got lock released message: {Resource} ({LockId})"
    )]
    internal static partial void LogLockReleased(ILogger logger, string resource, string lockId);
}
