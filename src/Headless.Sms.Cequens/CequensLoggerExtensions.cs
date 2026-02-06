// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Sms.Cequens;

internal static partial class CequensLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToGetToken",
        Level = LogLevel.Warning,
        Message = "Failed to get authentication token from Cequens API"
    )]
    public static partial void LogFailedToGetToken(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "SmsSentSuccessfully",
        Level = LogLevel.Information,
        Message = "SMS sent successfully to {DestinationCount} destination(s), status: {StatusCode}"
    )]
    public static partial void LogSmsSentSuccessfully(
        this ILogger logger,
        int destinationCount,
        HttpStatusCode statusCode
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "FailedToSendSms",
        Level = LogLevel.Warning,
        Message = "Failed to send SMS to {DestinationCount} destination(s), status: {StatusCode}"
    )]
    public static partial void LogFailedToSendSms(this ILogger logger, int destinationCount, HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 4,
        EventName = "FailedToGetTokenWithStatusCode",
        Level = LogLevel.Warning,
        Message = "Failed to get authentication token, status: {StatusCode}"
    )]
    public static partial void LogFailedToGetTokenWithStatusCode(this ILogger logger, HttpStatusCode statusCode);
}
