// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>The wire shape of a reCAPTCHA v3 siteverify response, mapped onto <see cref="ReCaptchaV3VerifyResult"/>.</summary>
internal sealed class ReCaptchaSiteVerifyV3Response
{
    [JsonPropertyName("success")]
    [MemberNotNullWhen(true, nameof(HostName), nameof(ChallengeTimeStamp), nameof(Score), nameof(Action))]
    [MemberNotNullWhen(false, nameof(ErrorCodes))]
    public bool Success { get; init; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset? ChallengeTimeStamp { get; init; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    [JsonPropertyName("score")]
    public float? Score { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }
}
