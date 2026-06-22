// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>The wire shape of a reCAPTCHA v2 siteverify response, mapped onto <see cref="CaptchaVerifyResult"/>.</summary>
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
