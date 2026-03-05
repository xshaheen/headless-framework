// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Sms.Infobip;

internal static partial class InfobipSmsLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SmsSentSuccessfully",
        Level = LogLevel.Trace,
        Message = "Infobip SMS sent successfully to {DestinationCount} recipients"
    )]
    public static partial void LogSmsSentSuccessfully(this ILogger logger, int destinationCount);

    [LoggerMessage(
        EventId = 2,
        EventName = "SmsSendFailed",
        Level = LogLevel.Error,
        Message = "Infobip SMS failed to {DestinationCount} recipients, ErrorCode={ErrorCode}"
    )]
    public static partial void LogSmsSendFailed(
        this ILogger logger,
        Exception exception,
        int destinationCount,
        int errorCode
    );
}
