using Microsoft.Extensions.Logging;

namespace Framework.Integrations.Recaptcha.Internals;

internal static partial class RecaptchaLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "RecaptchaFailure",
        Level = LogLevel.Information,
        Message = "[reCAPTCHA] validation failed {Response}"
    )]
    public static partial void LogRecaptchaFailure(
        this ILogger<RecaptchaV2Service> logger,
        [LogProperties] InternalRecaptchaV2Response? response
    );
}
