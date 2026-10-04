// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// A Turnstile verification request. Extends the common request with Turnstile's optional <c>idempotency_key</c>,
/// which lets a single token be safely re-verified without triggering a duplicate-token error.
/// </summary>
[PublicAPI]
public sealed class TurnstileVerifyRequest : CaptchaVerifyRequest
{
    /// <summary>Optional. An idempotency key so the same token can be re-verified without a duplicate error.</summary>
    public string? IdempotencyKey { get; init; }
}
