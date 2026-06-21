// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Services;

/// <summary>
/// Provides the BCP 47 language code appended to the reCAPTCHA script URL so the widget renders in the
/// correct language.
/// </summary>
/// <remarks>
/// The default implementation returns <c>CultureInfo.CurrentUICulture.ToString()</c>. Replace this service
/// in DI to supply a fixed language or derive the code from a different source (for example, the HTTP
/// request's <c>Accept-Language</c> header).
/// </remarks>
public interface IReCaptchaLanguageCodeProvider
{
    /// <summary>Returns the language code to use for the reCAPTCHA widget (for example <c>en</c> or <c>ar</c>).</summary>
    /// <returns>A non-null language tag accepted by the reCAPTCHA <c>hl</c> query parameter.</returns>
    string GetLanguageCode();
}

internal sealed class CultureInfoReCaptchaLanguageCodeProvider : IReCaptchaLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
