using System.Text.Json.Serialization;

namespace Framework.Integrations.Recaptcha.Contracts;

public class ReCaptchaSiteVerifyResponse
{
    /// <summary>Whether this request was a valid reCAPTCHA token for your site.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Timestamp of the challenge load.</summary>
    [JsonPropertyName("challenge_ts")]
    public DateTime ChallengeTimeStamp { get; init; }

    /// <summary>The hostname of the site where the reCAPTCHA was solved.</summary>
    [JsonPropertyName("hostname")]
    public required string HostName { get; init; }

    /// <summary>Error code if not <see cref="Success"/>.</summary>
    [JsonPropertyName("error-codes")]
    public required string[] ErrorCodes { get; init; }

    public static ReCaptchaError? TryConvertErrorCode(string error)
    {
        return error switch
        {
            "bad-request" => ReCaptchaError.BadRequest,
            "timeout-or-duplicate" => ReCaptchaError.TimeOutOrDuplicate,
            "invalid-input-response" => ReCaptchaError.InvalidInputResponse,
            "missing-input-response" => ReCaptchaError.MissingInputResponse,
            "invalid-input-secret" => ReCaptchaError.InvalidInputSecret,
            "missing-input-secret" => ReCaptchaError.MissingInputSecret,
            _ => null,
        };
    }
}
