// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Marker interface that identifies a verification result produced by a reCAPTCHA provider (v2 or v3). Scopes
/// <see cref="ReCaptchaResultExtensions.ToReCaptchaErrors"/> to reCAPTCHA results only so the extension does not
/// appear — with meaningless semantics — on unrelated types such as <c>TurnstileVerifyResult</c>.
/// </summary>
[PublicAPI]
public interface IReCaptchaVerifyResult;
