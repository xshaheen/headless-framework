// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Sms.Twilio;

internal static partial class TwilioSmsLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSendSms",
        Level = LogLevel.Warning,
        Message = "Failed to send SMS using Twilio API to {DestinationCount} destination(s), errorCode: {ErrorCode}"
    )]
    public static partial void LogFailedToSendSms(this ILogger logger, int destinationCount, int? errorCode);

    [LoggerMessage(
        EventId = 2,
        EventName = "SmsSendException",
        Level = LogLevel.Error,
        Message = "Failed to send SMS using Twilio API to {DestinationCount} destination(s)"
    )]
    public static partial void LogSmsSendException(this ILogger logger, Exception exception, int destinationCount);
}
