// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>Represents the payload returned by the reCAPTCHA v2 site verification API.</summary>
internal sealed class ReCaptchaSiteVerifyV2Response
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset? ChallengeTimeStamp { get; init; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
