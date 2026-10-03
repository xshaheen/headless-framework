// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>The wire shape of a reCAPTCHA v3 siteverify response, mapped onto <see cref="ReCaptchaV3VerifyResult"/>.</summary>
internal sealed class ReCaptchaSiteVerifyV3Response
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset? ChallengeTimeStamp { get; init; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    /// <summary>The risk score (0.0 – 1.0). Verify server-side against a threshold; a high score is not on its own a pass.</summary>
    [JsonPropertyName("score")]
    public float? Score { get; init; }

    /// <summary>The action name supplied at execute time. Verify it matches the expected action to prevent cross-action token replay.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
