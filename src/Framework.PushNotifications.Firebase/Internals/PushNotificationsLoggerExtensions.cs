// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Framework.PushNotifications.Firebase.Internals;

internal static partial class PushNotificationsLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSendPushNotification",
        Level = LogLevel.Critical,
        Message = "PushNotification: Failed to send notification to device. Token prefix: {ClientTokenPrefix}",
        SkipEnabledCheck = true
    )]
    public static partial void FailedToSendPushNotification(
        this ILogger logger,
        Exception exception,
        string clientTokenPrefix
    );
}
