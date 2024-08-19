using Framework.Integrations.Recaptcha.Contracts;
using Microsoft.Extensions.Logging;

namespace Framework.Integrations.Recaptcha.Internals;

internal static partial class ReCaptchaLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ReCaptchaFailure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] validation failed {Response}"
    )]
    public static partial void LogReCaptchaFailure(
        this ILogger logger,
        [LogProperties] ReCaptchaSiteVerifyResponse? response
    );
}
