// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Microsoft.Extensions.Logging;

namespace Framework.Recaptcha.Internals;

internal static partial class ReCaptchaLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ReCaptchaV2Failure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] V2 validation failed {Response}"
    )]
    public static partial void LogReCaptchaFailure(
        this ILogger logger,
        [LogProperties] ReCaptchaSiteVerifyV2Response? response
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "ReCaptchaV3Failure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] V3 validation failed {Response}"
    )]
    public static partial void LogReCaptchaFailure(
        this ILogger logger,
        [LogProperties] ReCaptchaSiteVerifyV3Response? response
    );
}
