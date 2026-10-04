// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// A reCAPTCHA v2 verification result. v2 adds no provider-specific fields beyond the common
/// <see cref="CaptchaVerifyResult"/> contract, but the concrete type carries <see cref="IReCaptchaVerifyResult"/>
/// so that <see cref="ReCaptchaResultExtensions.ToReCaptchaErrors"/> is scoped to reCAPTCHA results only.
/// </summary>
[PublicAPI]
public sealed class ReCaptchaV2VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult;
