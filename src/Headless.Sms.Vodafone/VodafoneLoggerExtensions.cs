// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Sms.Vodafone;

internal static partial class VodafoneLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EmptyResponse",
        Level = LogLevel.Warning,
        Message = "Received empty response from Vodafone API"
    )]
    public static partial void LogEmptyResponse(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToSendSms",
        Level = LogLevel.Warning,
        Message = "Failed to send SMS to {DestinationCount} destination(s), status: {StatusCode}"
    )]
    public static partial void LogFailedToSendSms(this ILogger logger, int destinationCount, HttpStatusCode statusCode);
}
