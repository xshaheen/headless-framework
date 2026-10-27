// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Captcha;

internal static partial class TurnstileLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "TurnstileHttpFailure",
        Level = LogLevel.Warning,
        Message = "[Turnstile] verification failed with status code {StatusCode} and response {Response}"
    )]
    internal static partial void LogTurnstileHttpFailure(
        this ILogger logger,
        HttpStatusCode statusCode,
        string response
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "TurnstileValidationFailure",
        Level = LogLevel.Information,
        Message = "[Turnstile] validation failed with error codes {ErrorCodes}"
    )]
    internal static partial void LogTurnstileValidationFailure(this ILogger logger, string[]? errorCodes);
}
