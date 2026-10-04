// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>Represents the payload returned by the reCAPTCHA v3 site verification API.</summary>
internal sealed class ReCaptchaSiteVerifyV3Response
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset? ChallengeTimeStamp { get; init; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    /// <summary>Gets the risk score between 0.0 and 1.0.</summary>
    [JsonPropertyName("score")]
    public float? Score { get; init; }

    /// <summary>Gets the action name specified during client execution.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
