// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

// The event ids keep the values they had when the root lock package logged these events, so existing log queries and
// alerts keep matching.
internal static partial class DistributedLocksMessagingLoggerExtensions
{
    [LoggerMessage(
        EventId = 14,
        EventName = "ProcessingLockReleased",
        Level = LogLevel.Debug,
        Message = "Processing lock released {MessageId} for {Resource}"
    )]
    internal static partial void LogProcessingLockReleased(this ILogger logger, string? messageId, string resource);

    [LoggerMessage(
        EventId = 17,
        EventName = "LockReleasePublishFailed",
        Level = LogLevel.Warning,
        Message = "Lock released but wake-up publish failed; waiters will fall back to polling: R={Resource} Id={LeaseId}"
    )]
    internal static partial void LogLockReleasePublishFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );
}
