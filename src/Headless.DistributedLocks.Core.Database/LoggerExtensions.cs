// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        EventName = "ConnectionScopedLockReleaseFailed",
        Message = "Connection-scoped lock release failed for resource {Resource} and lock id {LockId}."
    )]
    public static partial void LogConnectionScopedLockReleaseFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        EventName = "ReleaseWakePublishFailed",
        Message = "Publishing the release wake-up for resource {Resource} (lock id {LockId}) failed; waiters fall back to polling."
    )]
    public static partial void LogReleaseWakePublishFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );
}
