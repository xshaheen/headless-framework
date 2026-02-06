// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Sms.VictoryLink;

internal static partial class VictoryLinkLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EmptyResponse",
        Level = LogLevel.Warning,
        Message = "Received empty response from VictoryLink API"
    )]
    public static partial void LogEmptyResponse(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToSendSms",
        Level = LogLevel.Warning,
        Message = "Failed to send SMS to {DestinationCount} destination(s): {ResponseMessage}"
    )]
    public static partial void LogFailedToSendSms(this ILogger logger, int destinationCount, string responseMessage);
}
