// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// A reCAPTCHA v3 verification result. Extends the common result with v3's provider-only <see cref="Score"/> — the
/// numeric risk score (0.0 – 1.0) that has no equivalent in other providers. A consumer reads <see cref="Score"/>
/// only by resolving <see cref="IReCaptchaV3Verifier"/>; the base <see cref="ICaptchaVerifier"/> view is pass/fail.
/// </summary>
[PublicAPI]
public sealed class ReCaptchaV3VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult
{
    /// <summary>
    /// The score for this request (0.0 – 1.0). On a successful verify Google always returns a score; on failure
    /// the field is absent in the wire response and defaults to <c>0f</c> here (pass/fail is authoritative via
    /// <see cref="CaptchaVerifyResult.Success"/>).
    /// </summary>
    public float Score { get; init; }
}
