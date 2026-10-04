// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>Represents the verification result for a reCAPTCHA v2 token.</summary>
[PublicAPI]
public sealed class ReCaptchaV2VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult;
