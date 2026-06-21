// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks.PostgreSql;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        EventName = "ReleaseListenerReconnecting",
        Message = "The LISTEN/NOTIFY release listener disconnected and is reconnecting (attempt {Attempt}); cross-process wake-ups degrade to polling until it recovers."
    )]
    public static partial void LogReleaseListenerReconnecting(this ILogger logger, Exception exception, int attempt);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        EventName = "ReleaseNotificationFanoutFailed",
        Message = "Fanning out a LISTEN/NOTIFY release notification for resource {Resource} to local waiters failed."
    )]
    public static partial void LogReleaseNotificationFanoutFailed(
        this ILogger logger,
        Exception exception,
        string resource
    );
}
