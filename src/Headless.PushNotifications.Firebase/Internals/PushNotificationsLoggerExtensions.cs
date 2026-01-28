// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.PushNotifications.Firebase.Internals;

internal static partial class PushNotificationsLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FcmFailedToSendPushNotification",
        Level = LogLevel.Critical,
        Message = "FCM: Failed to send notification to device. Token prefix: {ClientTokenPrefix}",
        SkipEnabledCheck = true
    )]
    public static partial void FailedToSendPushNotification(
        this ILogger logger,
        Exception exception,
        string clientTokenPrefix
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FcmRetryingRequest",
        Level = LogLevel.Warning,
        Message = "FCM: Retrying request (attempt {AttemptNumber}) after {DelaySeconds}s delay. Error: {ErrorMessage}",
        SkipEnabledCheck = true
    )]
    public static partial void LogRetryAttempt(
        this ILogger logger,
        int attemptNumber,
        double delaySeconds,
        string errorMessage
    );
}
