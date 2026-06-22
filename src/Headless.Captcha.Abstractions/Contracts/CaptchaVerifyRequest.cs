// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// The inputs every CAPTCHA provider accepts when verifying a token: the required client response token and an
/// optional remote IP. Providers that accept extra inputs (for example Turnstile's <c>idempotency_key</c>) expose
/// them on a derived request type.
/// </summary>
[PublicAPI]
public class CaptchaVerifyRequest
{
    /// <summary>Required. The response token produced by the provider's client-side widget.</summary>
    public required string Response { get; init; }

    /// <summary>Optional. The end user's IP address.</summary>
    public string? RemoteIp { get; init; }
}
