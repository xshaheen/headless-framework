// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Captcha;

internal static partial class ReCaptchaLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ReCaptchaV2Failure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] V2 validation failed {Response}"
    )]
    internal static partial void LogReCaptchaFailure(
        this ILogger logger,
        [LogProperties] ReCaptchaSiteVerifyV2Response? response
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "ReCaptchaV3Failure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] V3 validation failed {Response}"
    )]
    internal static partial void LogReCaptchaFailure(
        this ILogger logger,
        [LogProperties] ReCaptchaSiteVerifyV3Response? response
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "ReCaptchaHttpRequestFailed",
        Level = LogLevel.Warning,
        Message = "[reCAPTCHA] siteverify HTTP request failed with status code {StatusCode} and response {Response}"
    )]
    internal static partial void LogReCaptchaHttpFailure(
        this ILogger logger,
        HttpStatusCode statusCode,
        string response
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "ReCaptchaPolicyRejected",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] valid token rejected by policy {Failures} (score {Score}, action {Action}, hostname {HostName})"
    )]
    internal static partial void LogReCaptchaPolicyRejected(
        this ILogger logger,
        string[] failures,
        float? score,
        string? action,
        string? hostName
    );
}
