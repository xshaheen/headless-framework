// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Source-generated logger extensions for the connection-scoped distributed lock infrastructure.</summary>
internal static partial class LoggerExtensions
{
    /// <summary>Warns that a connection-scoped lock release callback threw an exception.</summary>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        EventName = "ConnectionScopedLockReleaseFailed",
        Message = "Connection-scoped lock release failed for resource {Resource} and lock id {LeaseId}."
    )]
    public static partial void LogConnectionScopedLockReleaseFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    /// <summary>Warns that the post-release wake-up publish failed; waiters will still be notified via polling.</summary>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        EventName = "ReleaseWakePublishFailed",
        Message = "Publishing the release wake-up for resource {Resource} (lock id {LeaseId}) failed; waiters fall back to polling."
    )]
    public static partial void LogReleaseWakePublishFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );
}
