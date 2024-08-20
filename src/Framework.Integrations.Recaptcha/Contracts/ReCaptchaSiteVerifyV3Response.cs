using System.Text.Json.Serialization;
using Framework.Integrations.Recaptcha.Internals;

namespace Framework.Integrations.Recaptcha.Contracts;

public sealed class ReCaptchaSiteVerifyV3Response
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

    /// <summary>The score for this request (0.0 - 1.0)</summary>
    [JsonPropertyName("score")]
    public required float Score { get; set; }

    /// <summary>The action name for this request (important to verify)</summary>
    [JsonPropertyName("action")]
    public required string Action { get; set; }

    public static ReCaptchaError ParseError(string error)
    {
        return error.ToReCaptchaError();
    }

    public ReCaptchaError[] ParseErrors()
    {
        return ErrorCodes.ConvertAll(ParseError);
    }
}
