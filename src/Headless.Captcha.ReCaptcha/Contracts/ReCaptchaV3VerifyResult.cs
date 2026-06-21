// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// A reCAPTCHA v3 verification result. Extends the common result with v3's provider-only <see cref="Score"/> — the
/// numeric risk score (0.0 – 1.0) that has no equivalent in other providers. A consumer reads <see cref="Score"/>
/// only by resolving <see cref="IReCaptchaV3Verifier"/>; the base <see cref="ICaptchaVerifier"/> view is pass/fail.
/// </summary>
[PublicAPI]
public sealed class ReCaptchaV3VerifyResult : CaptchaVerifyResult
{
    /// <summary>The score for this request (0.0 – 1.0), when the verification succeeded.</summary>
    public float? Score { get; init; }
}
