// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.PushNotifications.Apns.Internals;

// Every message takes a masked device token: the raw token is a stable device identifier.
internal static partial class ApnsLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ApnsNotificationRejected",
        Level = LogLevel.Warning,
        Message = "APNs: Notification rejected with HTTP {StatusCode} ({Reason}). Token prefix: {DeviceTokenPrefix}"
    )]
    public static partial void LogNotificationRejected(
        this ILogger logger,
        int statusCode,
        string reason,
        string deviceTokenPrefix
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "ApnsSendFailed",
        Level = LogLevel.Error,
        Message = "APNs: Failed to send notification. Token prefix: {DeviceTokenPrefix}"
    )]
    public static partial void LogSendFailed(this ILogger logger, Exception exception, string deviceTokenPrefix);

    [LoggerMessage(
        EventId = 3,
        EventName = "ApnsProviderTokenExpired",
        Level = LogLevel.Information,
        Message = "APNs: Provider token generation {Generation} was rejected as expired; retrying once with generation {RetryGeneration}."
    )]
    public static partial void LogProviderTokenExpired(this ILogger logger, long generation, long retryGeneration);

    [LoggerMessage(
        EventId = 4,
        EventName = "ApnsDeviceTokenUnregistered",
        Level = LogLevel.Information,
        Message = "APNs: Device token is no longer registered (HTTP {StatusCode}, {Reason}). Token prefix: {DeviceTokenPrefix}"
    )]
    public static partial void LogDeviceTokenUnregistered(
        this ILogger logger,
        int statusCode,
        string reason,
        string deviceTokenPrefix
    );
}
