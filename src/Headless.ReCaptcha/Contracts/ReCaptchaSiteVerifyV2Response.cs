// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Contracts;

public sealed class ReCaptchaSiteVerifyV2Response
{
    /// <summary>Whether this request was a valid reCAPTCHA token for your site.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Timestamp of the challenge load. Not guaranteed to be present even when <see cref="Success"/> is
    /// <see langword="true"/> (e.g. for Android app tokens); null-check before use.
    /// </summary>
    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTimeStamp { get; init; }

    /// <summary>
    /// The hostname of the site where the reCAPTCHA was solved. Not guaranteed to be present even when
    /// <see cref="Success"/> is <see langword="true"/> (e.g. for Android app tokens); null-check before use.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    /// <summary>Error codes returned when <see cref="Success"/> is <see langword="false"/>. Use <see cref="ReCaptchaErrorCodesExtensions.ToReCaptchaErrors"/> to parse.</summary>
    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
