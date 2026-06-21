// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Contracts;

/// <summary>The response returned by the Google reCAPTCHA v3 siteverify API.</summary>
public sealed class ReCaptchaSiteVerifyV3Response
{
    /// <summary>Whether this request was a valid reCAPTCHA token for your site.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Timestamp of the challenge load. Not guaranteed to be present even when <see cref="Success"/> is
    /// <see langword="true"/>; null-check before use.
    /// </summary>
    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTimeStamp { get; init; }

    /// <summary>
    /// The hostname of the site where the reCAPTCHA was solved. Not guaranteed to be present even when
    /// <see cref="Success"/> is <see langword="true"/>; null-check before use.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    /// <summary>The score for this request (0.0 - 1.0). Verify it server-side against a threshold.</summary>
    [JsonPropertyName("score")]
    public float? Score { get; init; }

    /// <summary>The action name for this request. Verify it server-side to prevent cross-action token replay.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    /// <summary>Error codes returned when <see cref="Success"/> is <see langword="false"/>. Use <see cref="ReCaptchaErrorCodesExtensions.ToReCaptchaErrors"/> to parse.</summary>
    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
