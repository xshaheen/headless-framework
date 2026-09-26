// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.PushNotifications.Apns.Internals;

// Every device-token message takes a masked token: the raw token is a stable device identifier.
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

    [LoggerMessage(
        EventId = 5,
        EventName = "ApnsCertificateExpiringSoon",
        Level = LogLevel.Warning,
        Message = "APNs: The certificate of the '{Instance}' instance expires at {ExpiresAt} (in {DaysLeft} days). Renew it in the Apple Developer account."
    )]
    public static partial void LogCertificateExpiringSoon(
        this ILogger logger,
        string instance,
        DateTimeOffset expiresAt,
        int daysLeft
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "ApnsCertificateExpired",
        Level = LogLevel.Error,
        Message = "APNs: The certificate of the '{Instance}' instance expired at {ExpiresAt}, so every push fails the TLS handshake. Renew it in the Apple Developer account."
    )]
    public static partial void LogCertificateExpired(this ILogger logger, string instance, DateTimeOffset expiresAt);

    // The reason comes from the certificate checks, whose messages never contain the certificate or its password.
    [LoggerMessage(
        EventId = 7,
        EventName = "ApnsCertificateReloadFailed",
        Level = LogLevel.Error,
        Message = "APNs: The changed certificate of the '{Instance}' instance was rejected, so the current certificate stays in use. {Reason}"
    )]
    public static partial void LogCertificateReloadFailed(this ILogger logger, string instance, string reason);

    [LoggerMessage(
        EventId = 8,
        EventName = "ApnsCertificateReloaded",
        Level = LogLevel.Information,
        Message = "APNs: The certificate of the '{Instance}' instance was reloaded; new connections present it. It expires at {ExpiresAt}."
    )]
    public static partial void LogCertificateReloaded(this ILogger logger, string instance, DateTimeOffset expiresAt);

    [LoggerMessage(
        EventId = 9,
        EventName = "ApnsCertificateCheckFailed",
        Level = LogLevel.Error,
        Message = "APNs: The periodic expiry check of the '{Instance}' instance's certificate failed."
    )]
    public static partial void LogCertificateCheckFailed(this ILogger logger, Exception exception, string instance);
}
