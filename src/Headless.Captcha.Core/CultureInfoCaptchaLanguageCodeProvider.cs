// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Default <see cref="ICaptchaLanguageCodeProvider"/> that derives the language code from the current UI culture
/// (<see cref="CultureInfo.CurrentUICulture"/>).
/// </summary>
[PublicAPI]
public sealed class CultureInfoCaptchaLanguageCodeProvider : ICaptchaLanguageCodeProvider
{
    /// <inheritdoc />
    public string GetLanguageCode()
    {
        return CultureInfo.CurrentUICulture.ToString();
    }
}
