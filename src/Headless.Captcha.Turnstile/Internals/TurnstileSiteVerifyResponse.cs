// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>The wire shape of a Cloudflare Turnstile siteverify response, mapped onto <see cref="TurnstileVerifyResult"/>.</summary>
internal sealed class TurnstileSiteVerifyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset? ChallengeTimestamp { get; init; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("cdata")]
    public string? CData { get; init; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; init; }

    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; init; }
}
