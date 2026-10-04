// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>Represents the verification result for a reCAPTCHA v3 token.</summary>
[PublicAPI]
public sealed class ReCaptchaV3VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult
{
    /// <summary>
    /// Gets the score for this request between 0.0 and 1.0. Defaults to 0 when verification fails or the score is
    /// omitted.
    /// </summary>
    public float Score { get; init; }
}
