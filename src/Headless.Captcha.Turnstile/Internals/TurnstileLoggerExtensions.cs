// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

internal static partial class TurnstileLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "TurnstileHttpFailure",
        Level = LogLevel.Information,
        Message = "[Turnstile] verification failed with status code {StatusCode} and response {Response}"
    )]
    public static partial void LogTurnstileHttpFailure(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(
        EventId = 2,
        EventName = "TurnstileValidationFailure",
        Level = LogLevel.Information,
        Message = "[Turnstile] validation failed with error codes {ErrorCodes}"
    )]
    public static partial void LogTurnstileValidationFailure(this ILogger logger, string[]? errorCodes);
}
