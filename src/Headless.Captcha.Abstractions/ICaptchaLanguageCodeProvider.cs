// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Supplies the language/locale code the captcha tag helpers render into the client widget or script — reCAPTCHA's
/// script-URL <c>?hl=</c> parameter and Turnstile's <c>data-language</c> attribute. Register a custom implementation
/// to override the default (which derives the code from the current UI culture).
/// </summary>
[PublicAPI]
public interface ICaptchaLanguageCodeProvider
{
    /// <summary>Gets the language code to render (for example <c>en</c> / <c>en-US</c>, or Turnstile's <c>auto</c>).</summary>
    string GetLanguageCode();
}
