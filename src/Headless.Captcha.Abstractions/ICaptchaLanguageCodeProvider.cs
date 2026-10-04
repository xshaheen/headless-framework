// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Provides the language or locale code rendered by captcha tag helpers into the client widget or script.
/// </summary>
[PublicAPI]
public interface ICaptchaLanguageCodeProvider
{
    /// <summary>Gets the language code to render, such as <c>en</c>, <c>en-US</c>, or Turnstile <c>auto</c>.</summary>
    /// <returns>The language code to render in the client widget.</returns>
    string GetLanguageCode();
}
